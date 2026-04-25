export interface Task {
  label: string;
  frequency: 1 | 2 | 4 | 12;
  workload: number;
}

export type JobStatus = "Queued" | "Processing" | "Done" | "Failed";

export interface TaskAssignment {
  label: string;
  workload: number;
}

export interface UserAssignment {
  userName: string;
  tasks: TaskAssignment[];
}

export interface PeriodSchedule {
  periodNumber: number;
  users: UserAssignment[];
}

export type JobResult = PeriodSchedule[];
