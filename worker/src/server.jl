using ProtoBuf
using ZMQ

# ── Pre-generated protobuf types (regenerate via: julia worker/scripts/generate_proto.jl)

include(joinpath(@__DIR__, "proto", "taskplan", "taskplan.jl"))

using .taskplan

# ── Include the solver ───────────────────────────────────────────────────────

include(joinpath(@__DIR__, "solver.jl"))

# ── Conversion helpers ───────────────────────────────────────────────────────

"""Convert a protobuf `JobRequest` into solver-native types."""
function request_to_solver_inputs(req::taskplan.JobRequest)
    tasks = TaskInput[
        TaskInput(
            td.label,
            Int(td.frequency),
            Int(td.workload),
            td.force_alternation,
        )
        for td in req.tasks
    ]
    users     = String[u for u in req.users]
    n_periods = Int(req.n_periods)
    return tasks, users, n_periods
end

"""Convert solver output into a protobuf `JobResult`."""
function solver_result_to_response(results::Vector{PeriodResult})
    periods = taskplan.PeriodSchedule[]
    for pr in results
        user_assignments = taskplan.UserAssignment[]
        for upr in pr.users
            task_assignments = taskplan.TaskAssignment[
                taskplan.TaskAssignment(to.label, Int32(to.workload))
                for to in upr.tasks
            ]
            push!(user_assignments, taskplan.UserAssignment(upr.user_name, task_assignments))
        end
        push!(periods, taskplan.PeriodSchedule(Int32(pr.period_number), user_assignments))
    end
    return taskplan.JobResult(periods, "")
end

# ── Health file ──────────────────────────────────────────────────────────────

const HEALTH_FILE = "/tmp/worker-alive"

function touch_health()
    write(HEALTH_FILE, string(time()))
end

# ── ZeroMQ REP server ───────────────────────────────────────────────────────

PORT = get(ENV, "PORT", "5555")

function run_server(; endpoint::String = "tcp://*:" * PORT)
    ctx = ZMQ.Context()
    sock = ZMQ.Socket(ctx, ZMQ.REP)
    ZMQ.bind(sock, endpoint)
    touch_health()

    println("Worker listening on $endpoint")

    try
        while true
            # Receive raw bytes
            raw = ZMQ.recv(sock)
            bytes = Vector{UInt8}(raw)

            println("Received job request ($(length(bytes)) bytes)")

            # Per-request error handling (ZMQ REQ/REP requires a reply for every request)
            resp_bytes = try
                # Decode protobuf request
                iob = IOBuffer(bytes)
                decoder = ProtoBuf.ProtoDecoder(iob)
                req = ProtoBuf.decode(decoder, taskplan.JobRequest)

                println("  Tasks: $(length(req.tasks)), Users: $(length(req.users)), Periods: $(req.n_periods)")

                # Convert, solve, convert back
                tasks, users, n_periods = request_to_solver_inputs(req)
                results = solve_task_schedule(tasks, users, n_periods)
                response = solver_result_to_response(results)

                # Encode protobuf response
                out = IOBuffer()
                encoder = ProtoBuf.ProtoEncoder(out)
                ProtoBuf.encode(encoder, response)
                take!(out)
            catch e
                e isa InterruptException && rethrow(e)
                err_msg = sprint(showerror, e)
                println("  ERROR processing request: $err_msg")
                error_response = taskplan.JobResult(taskplan.PeriodSchedule[], err_msg)
                out = IOBuffer()
                encoder = ProtoBuf.ProtoEncoder(out)
                ProtoBuf.encode(encoder, error_response)
                take!(out)
            end

            println("  Sending response ($(length(resp_bytes)) bytes)")
            ZMQ.send(sock, ZMQ.Message(resp_bytes))
            touch_health()
        end
    catch e
        if e isa InterruptException
            println("\nShutting down worker...")
        else
            rethrow(e)
        end
    finally
        close(sock)
        close(ctx)
    end
end

# ── Main entry point ─────────────────────────────────────────────────────────
println("Starting worker...")
# Convert SIGINT into an InterruptException instead of crashing at the C level
Base.exit_on_sigint(false)
run_server()
