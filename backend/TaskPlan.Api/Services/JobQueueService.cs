using System.Collections.Concurrent;
using System.Threading.Channels;
using TaskPlan.Api.Models;

namespace TaskPlan.Api.Services;

public class JobQueueService : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, JobEntry> _jobs = new();
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly ZmqWorkerClient _zmqClient;
    private readonly ILogger<JobQueueService> _logger;

    public JobQueueService(ZmqWorkerClient zmqClient, ILogger<JobQueueService> logger)
    {
        _zmqClient = zmqClient;
        _logger = logger;
    }

    public Guid EnqueueJob(SubmitJobRequest request)
    {
        var entry = new JobEntry
        {
            Id = Guid.NewGuid(),
            Status = JobStatus.Queued,
            Request = request
        };

        _jobs[entry.Id] = entry;
        _queue.Writer.TryWrite(entry.Id);
        _logger.LogInformation("Job {JobId} queued", entry.Id);

        return entry.Id;
    }

    public JobEntry? GetJob(Guid id)
    {
        return _jobs.TryGetValue(id, out var entry) ? entry : null;
    }

    public void RegisterListener(Guid id, Func<JobStatus, Task> listener)
    {
        if (_jobs.TryGetValue(id, out var entry))
        {
            lock (entry.Listeners)
            {
                entry.Listeners.Add(listener);
            }
        }
    }

    public bool RemoveJob(Guid id)
    {
        return _jobs.TryRemove(id, out _);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_jobs.TryGetValue(jobId, out var entry))
            {
                // Job was deleted while queued; skip it
                continue;
            }

            try
            {
                entry.Status = JobStatus.Processing;
                await NotifyListeners(entry);

                _logger.LogInformation("Job {JobId} sending request to worker", jobId);
                var result = await _zmqClient.SendJobAsync(entry.Request, stoppingToken);
                _logger.LogInformation("Job {JobId} received response from worker", jobId);
                entry.Result = result;
                entry.Status = JobStatus.Done;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                entry.Status = JobStatus.Failed;
                entry.ErrorMessage = ex.Message;
            }

            await NotifyListeners(entry);
        }
    }

    private static async Task NotifyListeners(JobEntry entry)
    {
        Func<JobStatus, Task>[] listeners;
        lock (entry.Listeners)
        {
            listeners = [.. entry.Listeners];
        }

        foreach (var listener in listeners)
        {
            try
            {
                await listener(entry.Status);
            }
            catch
            {
                // Swallow listener errors (e.g. client disconnected)
            }
        }
    }
}
