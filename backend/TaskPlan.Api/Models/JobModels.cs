namespace TaskPlan.Api.Models;

public enum JobStatus { Queued, Processing, Done, Failed }

public class SubmitJobRequest
{
    public List<TaskDefDto> Tasks { get; set; } = [];
    public List<string> Users { get; set; } = [];
    public int NPeriods { get; set; }
}

public class TaskDefDto
{
    public string Label { get; set; } = "";
    public int Frequency { get; set; }
    public int Workload { get; set; }
}

public class JobEntry
{
    public Guid Id { get; set; }
    public JobStatus Status { get; set; }
    public SubmitJobRequest Request { get; set; } = null!;
    public object? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public List<Func<JobStatus, Task>> Listeners { get; } = [];
}

public class JobResultDto
{
    public List<PeriodScheduleDto> Periods { get; set; } = [];
}

public class PeriodScheduleDto
{
    public int PeriodNumber { get; set; }
    public List<UserAssignmentDto> Users { get; set; } = [];
}

public class UserAssignmentDto
{
    public string UserName { get; set; } = "";
    public List<TaskAssignmentDto> Tasks { get; set; } = [];
}

public class TaskAssignmentDto
{
    public string Label { get; set; } = "";
    public int Workload { get; set; }
}
