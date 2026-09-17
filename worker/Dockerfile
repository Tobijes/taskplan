FROM julia:1.12.4
ENV JULIA_NUM_PRECOMPILE_TASKS=2

WORKDIR /app

COPY Project.toml Manifest.toml ./
RUN julia --project=. -e "using Pkg; Pkg.instantiate(); Pkg.precompile()"

COPY src/ src/

ENV PORT=5555
EXPOSE 80

CMD ["julia", "--project=.", "src/server.jl"]
