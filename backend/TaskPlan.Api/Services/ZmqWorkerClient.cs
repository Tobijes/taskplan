using TaskPlan.Api.Models;
using Google.Protobuf;
using NetMQ;
using NetMQ.Sockets;

namespace TaskPlan.Api.Services;

public class ZmqWorkerClient : IDisposable
{
    private readonly RequestSocket _socket;
    private readonly object _lock = new();

    public ZmqWorkerClient()
    {
        _socket = new RequestSocket();
        var workerAddress = Environment.GetEnvironmentVariable("WORKER_ADDRESS") ?? "tcp://localhost:5555";
        _socket.Connect(workerAddress);
    }

    public Task<JobResultDto> SendJobAsync(SubmitJobRequest request)
    {
        return Task.Run(() =>
        {
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
                    Workload = task.Workload,
                    ForceAlternation = task.ForceAlternation
                });
            }

            byte[] requestBytes = protoRequest.ToByteArray();

            byte[] replyBytes;
            lock (_lock)
            {
                _socket.SendFrame(requestBytes);
                replyBytes = _socket.ReceiveFrameBytes();
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

    public void Dispose()
    {
        _socket.Dispose();
    }
}
