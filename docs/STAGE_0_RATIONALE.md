# TaskFlow — Stage 0 rationale

## 1. Goal and scope

Stage 0 exists to make later development reproducible and to prevent architectural drift before business code appears. The repository therefore contains only the build/tooling skeleton and dependency boundaries. No Domain entities, Application use cases, EF Core model, auth pipeline, Docker setup, or production endpoints are implemented prematurely.

## 2. Repository layout

```text
taskflow/
├─ src/
│  ├─ TaskFlow.Domain/
│  ├─ TaskFlow.Application/
│  ├─ TaskFlow.Infrastructure/
│  ├─ TaskFlow.Api/
│  ├─ TaskFlow.DbMigrator/
│  └─ TaskFlow.Client/
├─ tests/
│  ├─ TaskFlow.Domain.Tests/
│  ├─ TaskFlow.Application.Tests/
│  └─ TaskFlow.IntegrationTests/
├─ deploy/k8s/
├─ docs/
├─ scripts/
├─ global.json
├─ Directory.Build.props
├─ Directory.Packages.props
├─ packages.lock.json
├─ .editorconfig
├─ .gitignore
├─ .env.example
├─ TaskFlow.sln
└─ README.md
```

This mirrors the architecture: one codebase contains source, tests, migration process boundary, and deployment artifacts. `deploy/k8s` is reserved now but remains empty until deployment stages.

## 3. Production dependency graph

The enforced graph is:

```text
Domain          -> nothing
Application     -> Domain
Infrastructure  -> Application + Domain
Api             -> Application + Infrastructure
DbMigrator      -> Infrastructure
Client          -> no server project references
```

Why this is fixed now: changing references after features are written is expensive. A tiny Python verifier parses the project files directly and fails when a production-layer edge deviates from the contract. It does not rely on developer discipline alone.

The test projects are outside the production dependency graph. `Domain.Tests` references Domain; `Application.Tests` references Application and Domain; `IntegrationTests` references API and Infrastructure so later stages can verify composition and persistence without weakening production boundaries.

## 4. .NET 10 LTS and `global.json`

The repository pins SDK `10.0.401`, disables prerelease SDKs, and allows only `latestPatch` roll-forward in the same 10.0.4xx feature band. This choice keeps the toolchain on the current LTS line while permitting servicing/security patches without silently moving to a new feature band or .NET 11 preview/RC.

C# language version is fixed to stable C# 14 (`LangVersion=14.0`) rather than `preview`.

## 5. Build rules

`Directory.Build.props` is the single source for compiler/analyzer policy:

- `Nullable=enable` catches nullability mistakes at compile time;
- `ImplicitUsings=enable` keeps project files/source concise;
- `TreatWarningsAsErrors=true` prevents warning debt from becoming baseline;
- `EnforceCodeStyleInBuild=true` moves style rules from IDE-only hints into CI/build;
- `EnableNETAnalyzers=true` uses analyzers shipped with the pinned SDK and avoids an unnecessary external analyzer package at stage 0;
- `AnalysisLevel=latest-recommended` means the recommendation set is deterministic with the pinned SDK feature band;
- `Deterministic=true` supports reproducible compilation;
- `ContinuousIntegrationBuild=true` is enabled when `CI=true`.

No warning category is globally suppressed. If a future warning must be suppressed, it should be local and justified.

## 6. NuGet strategy and lock files

Central Package Management is enabled in `Directory.Packages.props`. Stage 0 intentionally introduces no external NuGet dependencies, so the central version table is currently empty. The rule for later stages is: package versions are declared centrally, while `.csproj` files reference packages without a `Version` attribute.

`RestorePackagesWithLockFile=true` is enabled for all projects. NuGet lock files are project-scoped in actual MSBuild/NuGet behavior, so each project has its own `packages.lock.json`. A root `packages.lock.json` is also present to preserve the architecture's root build-contract artifact and provide an obvious repository-level dependency-lock marker.

CI sets `RestoreLockedMode=true`; locally the canonical explicit check is:

```bash
dotnet restore TaskFlow.sln --locked-mode
```

When a later stage intentionally changes packages, regenerate lock files deliberately (for example with a normal/force-evaluate restore), review the diff, then return to locked mode.

## 7. Why there are no test framework NuGet packages yet

Stage 0 requires the test project boundaries to exist and the solution/test command path to be established; it does not yet define Domain/Application test cases. Pulling xUnit/MSTest/NUnit and their transitive graph into the repository before the first actual tests would add dependency churn without business value.

Therefore the three `*.Tests` projects are compile-time shells at this stage. A real test framework is added at the first stage that introduces executable tests, with its version centralized and its lock graph committed in the same change. This keeps stage 0 minimal while preserving the intended test topology.

## 8. Why `TaskFlow.Client` is a Razor boundary at stage 0

The target architecture is Blazor WebAssembly, but the implementation plan introduces the client infrastructure at stage 12, after API/auth/security contracts stabilize. A standalone Blazor WebAssembly host requires an external package graph and begins making runtime/browser decisions.

At stage 0, the client is therefore a Razor SDK project with no server project references. This establishes the compile-time boundary now, while deferring the actual WebAssembly host, authentication-state provider, antiforgery handler, typed API clients, and browser runtime package graph to the stage explicitly responsible for them.

This is intentionally narrower than the final client and prevents accidental coupling to `TaskFlow.Api`, `TaskFlow.Infrastructure`, or Domain types.

## 9. API and DbMigrator placeholders

`TaskFlow.Api` uses `Microsoft.NET.Sdk.Web` so the eventual ASP.NET Core composition root already lives in the correct executable. It intentionally maps no business endpoints at stage 0.

`TaskFlow.DbMigrator` is a separate executable and references only Infrastructure. It does not run EF migrations yet; that behavior belongs to stage 11. Establishing the executable boundary now ensures the API will never need to become the migration runner later.

## 10. Configuration and secrets

`.env.example` contains only placeholder/example values and the configuration keys named by the architecture. `.env` and local variants are ignored by Git. No real password, connection string, token, certificate, or environment-specific endpoint is committed.

The frontend does not contain an environment-specific API host. The final architecture will use a same-origin relative `/api/v1/...` path.

## 11. Git history design

The repository is initialized as Git from the beginning. The history is split into logical changes rather than one generated dump:

1. bootstrap solution and project boundaries;
2. pin build/toolchain and dependency-lock policy;
3. add stage-0 verification tooling;
4. document usage and rationale.

This makes architectural decisions reviewable with `git log`, `git show`, and `git diff`. The completed state is tagged `stage-0-complete`.

## 12. Stage-0 verification contract

The one-command verification is:

```bash
./scripts/verify-stage0.sh
```

It first runs the static architecture/build checker, then:

```text
dotnet restore TaskFlow.sln --locked-mode
-> dotnet build TaskFlow.sln --no-restore --configuration Release
-> dotnet test TaskFlow.sln --no-build --no-restore --configuration Release
```

The target state is 0 warnings, 0 errors, successful test command, no forbidden production references, and no committed `bin/` or `obj/` directories.

The environment used to assemble this archive did not have a .NET SDK installed, so only the static part could be executed here. The repository records this fact explicitly instead of claiming an unperformed build.

## 13. What starts in stage 1

Only after the stage-0 verification is green should stage 1 add the pure Domain model: `ProjectStatus`, `TaskStatus`, `TaskPriority`, `Project`, `TaskItem`, `Tag`, `TaskTag`, domain invariants, version rules, time injection, and Domain tests. EF Core, ASP.NET Core Identity, Npgsql, repositories, and HTTP concerns remain outside Domain.
