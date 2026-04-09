# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Task Plan (Opgaveplan) is a full-stack app that optimally distributes household chores between two users across multiple weeks using mixed-integer programming. Three components communicate in a pipeline:

```
Frontend (React/TS, :5173) → HTTP/SSE → Backend (.NET 10, :5000) → ZeroMQ/Protobuf → Worker (Julia, :5555)
```

## Build & Run Commands

### Frontend (`frontend/`)
```bash
npm --prefix frontend install        # install dependencies
npm --prefix frontend run dev        # dev server on :5173
npm --prefix frontend run build      # typecheck + production build
npm --prefix frontend run lint       # ESLint
```

### Backend (`backend/TaskPlan.Api/`)
```bash
dotnet build backend/TaskPlan.Api  # build
dotnet run --project backend/TaskPlan.Api  # run on :5000
```

Protobuf C# code is auto-generated at build time via Grpc.Tools (configured in the .csproj).

### Worker (`worker/`)
```bash
julia worker/scripts/generate_proto.jl                    # regenerate protobuf bindings (after .proto changes)
julia --project=worker worker/src/server.jl               # start ZMQ server on :5555
julia --project=worker -e "using Pkg; Pkg.precompile()"   # precompile deps
```

### Running the full stack
Start all three processes (worker, backend, frontend) in separate terminals. The worker must be running before the backend can process jobs.

## Architecture

### Communication Flow
1. Frontend submits a job via `POST /api/jobs`, then subscribes to SSE at `/api/jobs/{id}/events`
2. Backend enqueues the job in an in-memory `Channel<Guid>` queue, processed by `JobQueueService` (a `BackgroundService`)
3. `ZmqWorkerClient` serializes the request to Protobuf, sends over ZeroMQ REQ socket to Julia
4. Julia worker deserializes, runs the MIP solver (`JuMP` + `HiGHS`), returns Protobuf response
5. Backend stores the result, notifies SSE listeners, frontend fetches result via `GET /api/jobs/{id}/result`

### Protobuf Schema (`protobuf/taskplan.proto`)
Shared contract between backend and worker. The backend has a copy at `backend/TaskPlan.Api/Proto/taskplan.proto`. Julia bindings are generated offline via `worker/scripts/generate_proto.jl` and committed to git. Re-run the script after modifying the proto file.

### Key Backend Files
- `Controllers/JobsController.cs` — REST endpoints (submit, SSE events, result, delete)
- `Services/JobQueueService.cs` — async job queue with `ConcurrentDictionary` storage and status listeners
- `Services/ZmqWorkerClient.cs` — ZeroMQ client with Protobuf serialization (thread-safe via lock)
- `Models/JobModels.cs` — all DTOs and the `JobStatus` enum (Queued → Processing → Done/Failed)

### Key Worker Files
- `worker/src/server.jl` — ZMQ REP server, protobuf encoding/decoding, conversion functions
- `worker/src/solver.jl` — `solve_task_schedule()` MIP model: assignment constraints, workload balancing, forced alternation

### Frontend Structure
- `src/api/jobsApi.ts` — API client (hardcoded to `localhost:5000`)
- `src/hooks/` — `useTasks` (localStorage-backed), `useUsers`, `useJobSubmission` (SSE + polling)
- `src/components/` — `TaskEditor`, `UserEditor`, `PeriodsInput`, `ResultTable`
- `public/default.json` — default task set (Danish-named household chores)

## Domain Constraints
- Exactly **2 users** (hardcoded in solver)
- Task frequencies: **1, 2, 4, or 12** weeks only
- Force alternation only applies to weekly (frequency=1) tasks
- Workload is in integer minutes
- All job storage is in-memory (lost on backend restart)

## Ports & Configuration
| Component | Port | Configured in |
|-----------|------|---------------|
| Frontend  | 5173 | Vite default |
| Backend   | 5000 | `Program.cs` |
| Worker    | 5555 | `server.jl` |

CORS is configured in `Program.cs` to allow `http://localhost:5173`. Backend URL is hardcoded in `frontend/src/api/jobsApi.ts`.

### Environment Variables
| Variable | Default | Description |
|---|---|---|
| `WORKER_ADDRESS` | `tcp://localhost:5555` | ZeroMQ address of the Julia worker |
| `WORKER_TIMEOUT_SECONDS` | `120` | Max seconds to wait for worker response before failing the job |

## Testing
No test infrastructure exists yet.
