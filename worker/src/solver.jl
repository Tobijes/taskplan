using JuMP
import HiGHS

# ── Input / Output structs ──────────────────────────────────────────────────

struct TaskInput
    label::String
    frequency::Int
    workload::Int
end

struct TaskOutput
    label::String
    workload::Int
end

struct UserPeriodResult
    user_name::String
    tasks::Vector{TaskOutput}
end

struct PeriodResult
    period_number::Int
    users::Vector{UserPeriodResult}
end

# ── Solver ───────────────────────────────────────────────────────────────────

"""
    solve_task_schedule(tasks, users, n_periods) -> Vector{PeriodResult}

Solve the task-planning MIP for exactly 2 users.

* `tasks`     – `Vector{TaskInput}` with label, frequency, workload
* `users`     – `Vector{String}` of length 2 (user names)
* `n_periods` – number of planning periods (weeks)
"""
function solve_task_schedule(
    tasks::Vector{TaskInput},
    users::Vector{String},
    n_periods::Int,
)::Vector{PeriodResult}

    # -- Input validation
    isempty(tasks) && throw(ArgumentError("At least one task is required"))
    length(users) != 2 && throw(ArgumentError("Exactly 2 users are required, got $(length(users))"))
    n_periods < 1 && throw(ArgumentError("n_periods must be >= 1, got $n_periods"))
    for t in tasks
        t.frequency <= 0 && throw(ArgumentError("Task '$(t.label)' has invalid frequency $(t.frequency)"))
    end

    # -- Sets
    P = 1:n_periods           # Periods (weeks)
    U = 1:length(users)       # Users (persons)  – fixed to 2
    T = 1:length(tasks)       # Tasks

    # -- Constants
    F_t = map(x -> x.frequency, tasks)
    W_t = map(x -> x.workload, tasks)

    # -- Pre-computed shift matrices
    F = sort(unique(F_t))
    R = Dict{Int, Array{Bool, 2}}(
        f => [(((p-(s-1)) % f == 0)) for s in 1:f, p in P]
        for f in F
    )

    ## MODEL
    model = Model(HiGHS.Optimizer)
    set_silent(model)

    # Decision variables
    @variable(model, x[U, P, T], Bin)                       # user × period × task
    @variable(model, s[T, 1:maximum(F)], Bin)                # shift decision per task
    @variable(model, dWabs_parts[P, i=1:2] >= 0, Int)       # workload-balance helper

    # -- Constraints
    # Given a shift s_t, should the task t be taken in period p?
    for p in P
        for t in T
            f = F_t[t]
            @constraint(model,
                sum(x[u, p, t] for u in U) ==
                sum(R[f][i, p] * s[t, i] for i in 1:f))
        end
    end

    # Exactly one shift decision must be chosen for each task
    @constraint(model, [sum(s[t, i] for i in 1:F_t[t]) for t in T] .== 1)

    # Find absolute difference for each period (2-user balancing)
    @expression(model, w[u = U, p = P], sum(W_t[t] * x[u, p, t] for t in T))
    @expression(model, dW[p = P], sum(w[1, p] - w[2, p]))
    @constraint(model, dWabs_parts[:, 1] - dWabs_parts[:, 2] .== dW)
    absDiff = @expression(model, dWabs_parts[:, 1] + dWabs_parts[:, 2])

    # -- Objective function
    @objective(model, Min, sum(absDiff))

    ## SOLVE
    optimize!(model)

    ## BUILD RESULT
    x_sol = Array(value.(x))

    results = PeriodResult[]
    for p in P
        user_results = UserPeriodResult[]
        for u in U
            active_task_indexes = findall(t_idx -> x_sol[u, p, t_idx] >= 0.5, T)
            task_outputs = [
                TaskOutput(tasks[i].label, tasks[i].workload)
                for i in active_task_indexes
            ]
            push!(user_results, UserPeriodResult(users[u], task_outputs))
        end
        push!(results, PeriodResult(p, user_results))
    end

    return results
end
