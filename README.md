# TaskFlow — Stage 0

TaskFlow is prepared for implementation as a modular monolith on C#/.NET. **Stage 0 only** is implemented here: repository structure, project boundaries, reproducible build contract, NuGet lock mode, analyzers, Git hygiene, and verification scripts. Domain behavior, EF Core/PostgreSQL, HTTP endpoints, authentication, logging, Docker, and UI flows belong to later stages and are intentionally not implemented yet.

## What is ready

- .NET SDK pinned to **10.0.401 LTS feature band** via `global.json`;
- 6 production projects and 3 test-project shells;
- production references follow the architecture exactly;
- `Nullable`, implicit usings, warnings-as-errors, code-style enforcement, built-in .NET analyzers;
- Central Package Management via `Directory.Packages.props`;
- NuGet lock files and CI locked mode contract;
- `.editorconfig`, `.gitignore`, `.gitattributes`, `.env.example`;
- static dependency checker and one-command stage verification;
- Git repository with logical commits and tag `stage-0-complete`.

## Required SDK

Install .NET SDK **10.0.401** (or a later patch within the 10.0.4xx feature band allowed by `latestPatch`). Preview SDKs are not accepted by `global.json`.

## Verify stage 0

From the repository root:

```bash
./scripts/verify-stage0.sh
```

Equivalent commands:

```bash
python3 scripts/verify_project_references.py
dotnet restore TaskFlow.sln --locked-mode
dotnet build TaskFlow.sln --no-restore --configuration Release
dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

Expected result: locked restore succeeds; build has **0 warnings / 0 errors**; `dotnet test` completes successfully. The test projects are only structural shells in stage 0; real Domain/Application/Integration tests are added together with the corresponding implementation stages.

## Important stage boundary

`TaskFlow.Client` is currently a Razor client boundary without server project references. The standalone Blazor WebAssembly host and its external package graph are intentionally introduced in **stage 12**, when the API contract is stable. This avoids adding a client dependency graph and HTTP/security behavior before the architecture says to implement them.

## Verification status of this delivered archive

Static architecture checks were executed while creating the archive. The generation environment did **not** contain a .NET SDK, so `dotnet restore/build/test` could not be executed here. Run `./scripts/verify-stage0.sh` on a machine with the pinned SDK before starting stage 1.

For design rationale and every stage-0 decision, read [`docs/STAGE_0_RATIONALE.md`](docs/STAGE_0_RATIONALE.md).
