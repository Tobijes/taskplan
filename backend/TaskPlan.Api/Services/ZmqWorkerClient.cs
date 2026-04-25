using TaskPlan.Api.Models;
using Google.Protobuf;
using NetMQ;
using NetMQ.Sockets;

namespace TaskPlan.Api.Services;

public class ZmqWorkerClient : IDisposable
{
    private RequestSocket _socket;
    private readonly object _lock = new();
    private readonly string _workerAddress;
    private readonly TimeSpan _timeout;

    public ZmqWorkerClient()
    {
        _workerAddress = Environment.GetEnvironmentVariable("WORKER_ADDRESS") ?? "tcp://localhost:5555";
        var timeoutSeconds = Environment.GetEnvironmentVariable("WORKER_TIMEOUT_SECONDS");
        _timeout = TimeSpan.FromSeconds(int.TryParse(timeoutSeconds, out var seconds) ? seconds : 120);

        _socket = new RequestSocket();
        _socket.Connect(_workerAddress);
    }

    public Task<JobResultDto> SendJobAsync(SubmitJobRequest request, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var protoRequest = new Taskplan.JobRequest
            {
                NPeriods = request.NPeriods
            };

            protoRequest.Users.AddRange(request.Users);

            foreach (var task in request.Tasks)
            {
                protoRequest.Tasks.Add(new Taskplan.TaskDef
                {
                    Label = task.Label,
                    Frequency = task.Frequency,
                    Workload = task.Workload
                });
            }

            byte[] requestBytes = protoRequest.ToByteArray();

            byte[] replyBytes;
            lock (_lock)
            {
                _socket.SendFrame(requestBytes);
                if (!_socket.TryReceiveFrameBytes(_timeout, out replyBytes!))
                {
                    RecreateSocket();
                    throw new TimeoutException(
                        $"Worker did not respond within {_timeout.TotalSeconds} seconds");
                }
            }

            var protoResult = Taskplan.JobResult.Parser.ParseFrom(replyBytes);

            if (!string.IsNullOrEmpty(protoResult.Error))
            {
                throw new InvalidOperationException($"Worker error: {protoResult.Error}");
            }

            return MapToDto(protoResult);
        });
    }

    private static JobResultDto MapToDto(Taskplan.JobResult protoResult)
    {
        var dto = new JobResultDto();

        foreach (var period in protoResult.Periods)
        {
            var periodDto = new PeriodScheduleDto
            {
                PeriodNumber = period.PeriodNumber
            };

            foreach (var user in period.Users)
            {
                var userDto = new UserAssignmentDto
                {
                    UserName = user.UserName
                };

                foreach (var task in user.Tasks)
                {
                    userDto.Tasks.Add(new TaskAssignmentDto
                    {
                        Label = task.Label,
                        Workload = task.Workload
                    });
                }

                periodDto.Users.Add(userDto);
            }

            dto.Periods.Add(periodDto);
        }

        return dto;
    }

    private void RecreateSocket()
    {
        _socket.Options.Linger = TimeSpan.Zero;
        _socket.Dispose();
        _socket = new RequestSocket();
        _socket.Connect(_workerAddress);
    }

    public void Dispose()
    {
        _socket.Options.Linger = TimeSpan.Zero;
        _socket.Dispose();
    }
}
