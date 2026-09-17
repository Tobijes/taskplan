FROM julia:1.12.4

# GitHub Action runners run on AMD, we need to compile for Intel aswell
ENV JULIA_CPU_TARGET="generic;sandybridge,-xsaveopt,clone_all;haswell,-rdrnd,base(1),clone_all;skylake-avx512,clone_all;znver3,clone_all"

WORKDIR /app

COPY Project.toml Manifest.toml ./

RUN julia --project=. -e "using Pkg; Pkg.instantiate(); Pkg.precompile()"
# Check that precompile was succesful
RUN julia --project=. --compiled-modules=existing -e "using ProtoBuf, ZMQ; using JuMP, HiGHS"

COPY src/ src/

ENV PORT=5555
EXPOSE 80

CMD ["julia", "--project=.", "src/server.jl"]
