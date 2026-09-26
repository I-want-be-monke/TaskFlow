using System.Text.Json;
using System.Xml.Linq;

namespace TaskFlow.DevCli;

internal sealed class VerificationCommands
{
    private readonly RepositoryContext _repository;

    public VerificationCommands(RepositoryContext repository)
    {
        _repository = repository;
    }

    public int Static(string scope = "all")
    {
        var normalizedScope = NormalizeScope(scope);

        VerifyBuildContract();
        VerifyProjectReferences();
        VerifyRepositoryCleanliness();

        switch (normalizedScope)
        {
            case "architecture":
                VerifyArchitecture();
                break;
            case "security":
                VerifySecuritySuite();
                break;
            case "containers":
                VerifyContainers();
                break;
            case "ci":
                VerifyCi();
                break;
            case "all":
                VerifyArchitecture();
                VerifySecuritySuite();
                VerifyContainers();
                VerifyCi();
                break;
        }

        Console.WriteLine($"Static verification passed: {normalizedScope}.");
        return 0;
    }

    private void VerifyArchitecture()
    {
        VerifyContracts();
        VerifyDomainBoundary();
        VerifyApplicationBoundary();
        VerifyFeature("Projects", ["CreateProject", "GetProject", "ListProjects", "UpdateProject", "DeleteProject", "ArchiveProject", "RestoreProject"]);
        VerifyFeature("Tasks", ["CreateTask", "GetTask", "ListTasks", "UpdateTask", "DeleteTask", "AddTagToTask", "RemoveTagFromTask"]);
        VerifyFeature("Tags", ["CreateTag", "GetTag", "ListTags", "UpdateTag", "DeleteTag"]);
        VerifyInfrastructure();
        VerifyApi();
        VerifyObservability();
        VerifyMigrator();
        VerifyClientFoundation();
        VerifyClientUi();
    }

    private void VerifySecuritySuite()
    {
        VerifySecurity();
        VerifyHardening();
        VerifyClientSecurity();
    }

    private static string NormalizeScope(string scope)
    {
        var normalized = scope.Trim().ToLowerInvariant();
        return normalized switch
        {
            "architecture" => normalized,
            "security" => normalized,
            "containers" => normalized,
            "ci" => normalized,
            "all" => normalized,
            _ => throw new ArgumentException(
                "verify-static scope must be one of: architecture, security, containers, ci, all."),
        };
    }

    private void VerifyBuildContract()
    {
        RequireFile("Directory.Build.props");
        RequireFile("Directory.Packages.props");
        RequireFile("global.json");
        RequireFile("TaskFlow.sln");

        var props = XDocument.Load(_repository.Resolve("Directory.Build.props"));
        var requiredProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Nullable"] = "enable",
            ["ImplicitUsings"] = "enable",
            ["TreatWarningsAsErrors"] = "true",
            ["EnforceCodeStyleInBuild"] = "true",
            ["EnableNETAnalyzers"] = "true",
            ["LangVersion"] = "14.0",
            ["RestorePackagesWithLockFile"] = "true",
        };

        foreach (var (name, expected) in requiredProperties)
        {
            var actual = props.Descendants(name).LastOrDefault()?.Value.Trim();
            Require(string.Equals(actual, expected, StringComparison.Ordinal),
                $"Directory.Build.props: {name} must be '{expected}', got '{actual ?? "<missing>"}'.");
        }

        using var globalJson = JsonDocument.Parse(File.ReadAllText(_repository.Resolve("global.json")));
        var sdk = globalJson.RootElement.GetProperty("sdk");
        Require(sdk.GetProperty("version").GetString() == "10.0.401", "global.json must pin .NET SDK 10.0.401.");
        Require(!sdk.GetProperty("allowPrerelease").GetBoolean(), "global.json must disable prerelease SDKs.");

        foreach (var project in Directory.EnumerateFiles(_repository.Resolve("src"), "*.csproj", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(_repository.Resolve("tests"), "*.csproj", SearchOption.AllDirectories))
                     .Concat(Directory.EnumerateFiles(_repository.Resolve("tools"), "*.csproj", SearchOption.AllDirectories)))
        {
            Require(File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json")),
                $"Missing lock file for {Relative(project)}.");
        }

        var solution = Read("TaskFlow.sln");
        Require(solution.Contains("tools\\TaskFlow.DevCli\\TaskFlow.DevCli.csproj", StringComparison.Ordinal),
            "TaskFlow.DevCli must be part of TaskFlow.sln so repository-wide build quality rules cover it.");

        var devCliProject = Read("tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj");
        foreach (var forbiddenOptOut in new[]
                 {
                     "<TreatWarningsAsErrors>false</TreatWarningsAsErrors>",
                     "<EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>",
                     "<RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>",
                     "<RestoreLockedMode>false</RestoreLockedMode>",
                 })
        {
            Require(!devCliProject.Contains(forbiddenOptOut, StringComparison.Ordinal),
                $"TaskFlow.DevCli must inherit repository quality settings; forbidden opt-out: {forbiddenOptOut}.");
        }
    }

    private void VerifyProjectReferences()
    {
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["src/TaskFlow.Domain/TaskFlow.Domain.csproj"] = [],
            ["src/TaskFlow.Contracts/TaskFlow.Contracts.csproj"] = [],
            ["src/TaskFlow.Application/TaskFlow.Application.csproj"] = ["TaskFlow.Domain"],
            ["src/TaskFlow.Infrastructure/TaskFlow.Infrastructure.csproj"] = ["TaskFlow.Application", "TaskFlow.Domain"],
            ["src/TaskFlow.Api/TaskFlow.Api.csproj"] = ["TaskFlow.Application", "TaskFlow.Contracts", "TaskFlow.Infrastructure"],
            ["src/TaskFlow.DbMigrator/TaskFlow.DbMigrator.csproj"] = ["TaskFlow.Infrastructure"],
            ["src/TaskFlow.Client/TaskFlow.Client.csproj"] = ["TaskFlow.Contracts"],
            ["tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj"] = [],
            ["tests/TaskFlow.DevCli.Tests/TaskFlow.DevCli.Tests.csproj"] = ["TaskFlow.DevCli"],
        };

        foreach (var (path, expectedReferences) in expected)
        {
            var document = XDocument.Load(_repository.Resolve(path));
            var actual = document.Descendants("ProjectReference")
                .Select(node => node.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Path.GetFileNameWithoutExtension(value!.Replace('\\', '/')))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var wanted = expectedReferences.Order(StringComparer.Ordinal).ToArray();
            Require(actual.SequenceEqual(wanted, StringComparer.Ordinal),
                $"{path}: expected references [{string.Join(", ", wanted)}], got [{string.Join(", ", actual)}].");
        }
    }

    private void VerifyRepositoryCleanliness()
    {
        Require(!Directory.Exists(_repository.Resolve("scripts")),
            "Legacy scripts/ directory must not return; developer automation belongs in TaskFlow.DevCli.");

        foreach (var python in Directory.EnumerateFiles(_repository.Root, "*.py", SearchOption.AllDirectories))
        {
            throw new InvalidOperationException($"Python automation is not allowed in the repository: {Relative(python)}.");
        }

        RequireFile("tools/TaskFlow.DevCli/SubmissionPackCommands.cs");
        RequireContains("tools/TaskFlow.DevCli/Program.cs", "\"pack\" =>", "DevCli must expose a clean submission pack command.");
        RequireContains(".gitignore", ".env.compose.local", "Local Compose secrets must be ignored by Git.");
        RequireContains(".gitignore", "**/bin/", "Build output must be ignored by Git.");
        RequireContains(".gitignore", "**/obj/", "Intermediate build output must be ignored by Git.");
    }

    private void VerifyContracts()
    {
        RequireFile("src/TaskFlow.Contracts/TaskFlow.Contracts.csproj");
        RequireFile("src/TaskFlow.Contracts/Auth/PasswordPolicy.cs");
        var contracts = ReadTree("src/TaskFlow.Contracts", ["*.cs", "*.csproj"]);
        RequireNone(
            contracts,
            ["TaskFlow.Domain", "TaskFlow.Application", "TaskFlow.Infrastructure", "Microsoft.EntityFrameworkCore"],
            "Shared transport contracts must remain dependency-free from server/domain layers");
        RequireContains("src/TaskFlow.Api/TaskFlow.Api.csproj", "TaskFlow.Contracts", "API must consume shared transport contracts.");
        RequireContains("src/TaskFlow.Client/TaskFlow.Client.csproj", "TaskFlow.Contracts", "Client must consume shared transport contracts.");
        RequireContains("src/TaskFlow.Api/Program.cs", "PasswordPolicyRules.RequiredLength", "Server Identity policy must use the shared password policy constants.");
        RequireContains("src/TaskFlow.Client/Ui/FormModels.cs", "[PasswordPolicy]", "Client registration form must use the shared password validator.");
    }

    private void VerifyDomainBoundary()
    {
        var text = ReadTree("src/TaskFlow.Domain", ["*.cs", "*.csproj"]);
        RequireNone(text,
            ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "TaskFlow.Infrastructure", "TaskFlow.Api", "DateTime.UtcNow", "DateTimeOffset.UtcNow"],
            "Domain boundary violation");
    }

    private void VerifyApplicationBoundary()
    {
        var text = ReadTree("src/TaskFlow.Application", ["*.cs", "*.csproj"]);
        RequireNone(text,
            ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "TaskFlow.Infrastructure", "TaskFlow.Api", "HttpContext", "IQueryable<"],
            "Application boundary violation");
        RequireContains("src/TaskFlow.Application/Common/Results/Result.cs", "class Result", "Application Result contract is missing.");
        RequireContains("src/TaskFlow.Application/Common/Errors/Error.cs", "record Error", "Application Error contract is missing.");
    }

    private void VerifyFeature(string feature, IReadOnlyList<string> operations)
    {
        var root = $"src/TaskFlow.Application/{feature}";
        RequireDirectory(root);
        foreach (var operation in operations)
        {
            RequireDirectory($"{root}/{operation}");
            Require(Directory.EnumerateFiles(_repository.Resolve($"{root}/{operation}"), "*.cs", SearchOption.TopDirectoryOnly).Any(),
                $"{feature}/{operation} must contain C# implementation files.");
        }
    }

    private void VerifyInfrastructure()
    {
        foreach (var path in new[]
                 {
                     "src/TaskFlow.Infrastructure/Persistence/TaskFlowDbContext.cs",
                     "src/TaskFlow.Infrastructure/Repositories/ProjectRepository.cs",
                     "src/TaskFlow.Infrastructure/Repositories/TaskRepository.cs",
                     "src/TaskFlow.Infrastructure/Repositories/TagRepository.cs",
                     "src/TaskFlow.Infrastructure/Persistence/Transactions/EfTransactionManager.cs",
                     "src/TaskFlow.Infrastructure/Persistence/Interceptors/VersionConcurrencyInterceptor.cs",
                 })
        {
            RequireFile(path);
        }

        Require(Directory.EnumerateFiles(_repository.Resolve("src/TaskFlow.Infrastructure/Persistence/Migrations"), "*.cs").Any(),
            "Infrastructure must contain EF Core migrations.");
        RequireContains("src/TaskFlow.Infrastructure/Persistence/TaskFlowDbContext.cs", "IdentityUserContext", "TaskFlowDbContext must own Identity schema.");
    }

    private void VerifyApi()
    {
        foreach (var controller in new[] { "AuthController.cs", "ProjectsController.cs", "TasksController.cs", "TagsController.cs" })
        {
            RequireFile($"src/TaskFlow.Api/Controllers/{controller}");
        }

        var controllers = ReadTree("src/TaskFlow.Api/Controllers", ["*.cs"]);
        RequireNone(controllers,
            ["TaskFlowDbContext", "TaskFlow.Domain.Projects.Project", "TaskFlow.Domain.Tasks.TaskItem", "TaskFlow.Domain.Tags.Tag"],
            "API controller boundary violation");
        RequireContains("src/TaskFlow.Api/Program.cs", "MapControllers", "API must map controllers.");
        RequireNotContains("src/TaskFlow.Api/Program.cs", "MigrateAsync", "API startup must not run migrations.");
    }

    private void VerifySecurity()
    {
        foreach (var path in new[]
                 {
                     "src/TaskFlow.Api/Security/ApiAntiforgeryFilter.cs",
                     "src/TaskFlow.Api/Security/AuthenticationProblemWriter.cs",
                     "src/TaskFlow.Api/Auth/HttpContextCurrentActor.cs",
                 })
        {
            RequireFile(path);
        }

        var program = Read("src/TaskFlow.Api/Program.cs");
        foreach (var token in new[] { "AddAntiforgery", "AddAuthentication", "AddAuthorization", "UseAuthentication", "UseAuthorization" })
        {
            Require(program.Contains(token, StringComparison.Ordinal), $"API security composition is missing {token}.");
        }
    }

    private void VerifyHardening()
    {
        foreach (var path in new[]
                 {
                     "src/TaskFlow.Api/Configuration/SecurityOptions.cs",
                     "src/TaskFlow.Api/Configuration/ProxyOptions.cs",
                     "src/TaskFlow.Api/Configuration/RequestLimitOptions.cs",
                     "src/TaskFlow.Api/RateLimiting/RateLimitPolicies.cs",
                     "src/TaskFlow.Api/Middleware/RequestBodyLimitMiddleware.cs",
                 })
        {
            RequireFile(path);
        }

        RequireContains("src/TaskFlow.Api/Program.cs", "UseRateLimiter", "API rate limiting must be enabled.");
        RequireContains("src/TaskFlow.Api/Program.cs", "UseRequestTimeouts", "API request timeouts must be enabled.");
    }

    private void VerifyObservability()
    {
        foreach (var path in new[]
                 {
                     "src/TaskFlow.Infrastructure/Observability/TaskFlowJsonConsoleFormatter.cs",
                     "src/TaskFlow.Infrastructure/Observability/TaskFlowLogEvents.cs",
                     "src/TaskFlow.Infrastructure/Observability/TaskFlowLoggingExtensions.cs",
                     "src/TaskFlow.Api/Middleware/RequestLoggingMiddleware.cs",
                 })
        {
            RequireFile(path);
        }

        var formatter = Read("src/TaskFlow.Infrastructure/Observability/TaskFlowJsonConsoleFormatter.cs");
        foreach (var field in new[] { "schema_version", "event_id", "event_name", "service_name", "service_version" })
        {
            Require(formatter.Contains(field, StringComparison.Ordinal), $"Structured formatter is missing '{field}'.");
        }
    }

    private void VerifyMigrator()
    {
        foreach (var path in new[]
                 {
                     "src/TaskFlow.DbMigrator/Program.cs",
                     "src/TaskFlow.DbMigrator/MigrationRunner.cs",
                     "src/TaskFlow.DbMigrator/DbMigratorExitCodes.cs",
                 })
        {
            RequireFile(path);
        }

        RequireContains("src/TaskFlow.DbMigrator/MigrationRunner.cs", "MigrateAsync", "DbMigrator must execute EF migrations.");
        RequireNotContains("src/TaskFlow.Api/Program.cs", "MigrateAsync", "API must remain migration-free.");
    }

    private void VerifyClientFoundation()
    {
        foreach (var path in new[]
                 {
                     "src/TaskFlow.Client/Auth/ApiAuthenticationStateProvider.cs",
                     "src/TaskFlow.Client/Auth/AuthApiClient.cs",
                     "src/TaskFlow.Client/Http/ApiHttpClient.cs",
                     "src/TaskFlow.Client/Security/AntiforgeryHandler.cs",
                     "src/TaskFlow.Client/Security/AntiforgeryTokenProvider.cs",
                 })
        {
            RequireFile(path);
        }

    }

    private void VerifyClientSecurity()
    {
        var client = ReadTree("src/TaskFlow.Client", ["*.cs", "*.razor", "*.csproj"]);
        RequireNone(client, ["localStorage", "sessionStorage", "Bearer "], "Client security/storage violation");
    }

    private void VerifyClientUi()
    {
        var pages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/TaskFlow.Client/Pages/Auth/Login.razor"] = "/auth/login",
            ["src/TaskFlow.Client/Pages/Auth/Register.razor"] = "/auth/register",
            ["src/TaskFlow.Client/Pages/Projects/Projects.razor"] = "/projects",
            ["src/TaskFlow.Client/Pages/Tags/Tags.razor"] = "/tags",
        };

        foreach (var (path, route) in pages)
        {
            RequireContains(path, $"@page \"{route}\"", $"{path} must expose route {route}.");
        }

        RequireContains("src/TaskFlow.Client/Pages/Projects/Projects.razor", "project-create-form", "Projects UI create form is missing.");
        RequireContains("src/TaskFlow.Client/Pages/Projects/ProjectDetails.razor", "task-list", "Task list UI is missing.");
        RequireContains("src/TaskFlow.Client/Pages/Tags/Tags.razor", "tags-list", "Tag list UI is missing.");
    }

    private void VerifyContainers()
    {
        var sdkVersion = DotNetSdkRequirement.Load(_repository).Version;
        foreach (var dockerfile in new[]
                 {
                     "src/TaskFlow.Api/Dockerfile",
                     "src/TaskFlow.DbMigrator/Dockerfile",
                     "src/TaskFlow.Client/Dockerfile",
                 })
        {
            var text = Read(dockerfile);
            Require(text.Split('\n').Count(line => line.StartsWith("FROM ", StringComparison.Ordinal)) >= 2,
                $"{dockerfile} must use a multi-stage build.");
            Require(text.Contains(sdkVersion, StringComparison.Ordinal), $"{dockerfile} must pin SDK {sdkVersion} from global.json.");
            Require(text.Contains("USER ", StringComparison.Ordinal), $"{dockerfile} must run as an explicit user.");
            Require(!text.Contains(":latest", StringComparison.OrdinalIgnoreCase), $"{dockerfile} must not use latest image tags.");
            Require(text.Contains("--locked-mode", StringComparison.Ordinal), $"{dockerfile} must use locked NuGet restore.");
            Require(text.Contains("--no-restore", StringComparison.Ordinal), $"{dockerfile} publish must reuse the restore result.");

            var restoreIndex = text.IndexOf("dotnet restore", StringComparison.Ordinal);
            var sourceCopyIndex = text.IndexOf("COPY . .", StringComparison.Ordinal);
            Require(restoreIndex >= 0 && sourceCopyIndex > restoreIndex,
                $"{dockerfile} must copy full source only after dependency restore to preserve Docker restore caching.");
            foreach (var descriptor in new[] { "global.json", "Directory.Build.props", "Directory.Packages.props" })
            {
                Require(text[..restoreIndex].Contains(descriptor, StringComparison.Ordinal),
                    $"{dockerfile} must copy {descriptor} before restore.");
            }

            bool lockFileAvailableBeforeRestore =
                text[..restoreIndex].Contains("packages.lock.json", StringComparison.Ordinal) ||
                dockerfile.EndsWith("TaskFlow.Client/Dockerfile", StringComparison.Ordinal) &&
                text[..restoreIndex].Contains("COPY src/TaskFlow.Client/ src/TaskFlow.Client/", StringComparison.Ordinal);
            Require(lockFileAvailableBeforeRestore,
                $"{dockerfile} must make packages.lock.json available before locked restore.");
        }

        RequireContains(
            "src/TaskFlow.Api/Dockerfile",
            "src/TaskFlow.Contracts/packages.lock.json",
            "API Docker restore graph must include TaskFlow.Contracts.");
        RequireContains(
            "src/TaskFlow.Client/Dockerfile",
            "src/TaskFlow.Contracts/packages.lock.json",
            "Client Docker restore graph must include the locked TaskFlow.Contracts descriptor.");
        RequireContains(
            "src/TaskFlow.Client/Dockerfile",
            "COPY src/TaskFlow.Client/ src/TaskFlow.Client/",
            "Blazor restore must see Razor source so .NET 10 internal assets remain consistent with the lock file.");
        RequireContains("deploy/nginx/default.conf.template", "gzip_static on;", "NGINX must serve precompressed Blazor assets.");
        RequireContains("deploy/nginx/default.conf.template", "max-age=31536000, immutable", "Fingerprintable framework assets need immutable caching.");
        RequireContains("deploy/nginx/default.conf.template", "Cache-Control \"no-cache\"", "Stable Blazor bootstrap files must be revalidated instead of cached immutably.");
        RequireContains("deploy/nginx/default.conf.template", "Cache-Control \"no-store\"", "index.html must not be long-lived cached.");
        RequireContains(
            "src/TaskFlow.Client/Dockerfile",
            "apk add --no-cache openssl curl",
            "Frontend image must explicitly install curl used by its healthcheck.");
        RequireContains(
            "src/TaskFlow.Client/Dockerfile",
            "nginx -t",
            "Frontend image build must validate the effective NGINX configuration.");
        RequireContains(
            "deploy/nginx/default.conf.template",
            "location ~ \"^/_framework/",
            "NGINX regex locations containing quantifier braces must be quoted so the config parser does not treat them as block delimiters.");

        var compose = Read("docker-compose.yml");
        foreach (var service in new[] { "postgres:", "migrator:", "api:", "frontend:" })
        {
            Require(compose.Contains(service, StringComparison.Ordinal), $"docker-compose.yml is missing {service.TrimEnd(':')} service.");
        }

        foreach (var token in new[] { "read_only: true", "cap_drop:", "no-new-privileges:true", "healthcheck:" })
        {
            Require(compose.Contains(token, StringComparison.Ordinal), $"docker-compose.yml is missing hardening token '{token}'.");
        }

        Require(!compose.Contains("name: taskflow", StringComparison.Ordinal),
            "docker-compose.yml must not hard-code a shared Compose project name.");
        RequireFile("tools/TaskFlow.DevCli/ComposeProject.cs");
        RequireContains(
            "tools/TaskFlow.DevCli/ComposeCommands.cs",
            "--project-name",
            "DevCli must pass an isolated Compose project name explicitly.");
        RequireContains("tools/TaskFlow.DevCli/ComposeProject.cs", "DefaultHttpsPort", "DevCli must derive a per-copy HTTPS port.");
        RequireContains("tools/TaskFlow.DevCli/ComposeProject.cs", "DefaultBackendSubnet", "DevCli must derive a per-copy backend subnet.");
        RequireContains("tools/TaskFlow.DevCli/ComposeCommands.cs", "CleanupLegacyProjectForRepository", "DevCli down must clean legacy Compose resources safely.");
        RequireContains("docker-compose.yml", "TASKFLOW_BACKEND_SUBNET is required", "Compose must receive the per-copy subnet explicitly.");
        RequireContains("docker-compose.yml", "TASKFLOW_HTTPS_PORT is required", "Compose must receive the per-copy host port explicitly.");
        Require(
            compose.Contains("curl", StringComparison.Ordinal) && compose.Contains("http://127.0.0.1:8080/healthz", StringComparison.Ordinal),
            "Frontend healthcheck must use the explicitly installed curl binary against the local HTTP health endpoint.");
        Require(!compose.Contains("[\"CMD\", \"wget\"", StringComparison.Ordinal),
            "Frontend healthcheck must not depend on an optional wget/BusyBox applet.");
    }

    private void VerifyCi()
    {
        var workflow = Read(".github/workflows/ci.yml");
        foreach (var token in new[]
                 {
                     "ci quality",
                     "ci integration",
                     "ci supply-chain",
                     "ci containers",
                     "coverage:",
                     "dotnet-coverage",
                     "upload-artifact",
                 })
        {
            Require(workflow.Contains(token, StringComparison.Ordinal), $"CI workflow is missing required capability '{token}'.");
        }

        Require(workflow.Contains("dotnet restore tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj --locked-mode", StringComparison.Ordinal),
            "CI must restore TaskFlow.DevCli in locked mode.");
        Require(!workflow.Contains("python", StringComparison.OrdinalIgnoreCase), "CI workflow must not depend on Python.");
        Require(!workflow.Contains("scripts/", StringComparison.Ordinal), "CI workflow must not depend on legacy scripts/.");
        VerifyBrowserAutomation();
    }

    private void VerifyBrowserAutomation()
    {
        RequireFile("tools/TaskFlow.DevCli/BrowserE2eCommands.cs");

        var project = XDocument.Load(_repository.Resolve("tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj"));
        var hasPlaywright = project
            .Descendants("PackageReference")
            .Any(reference => string.Equals(
                reference.Attribute("Include")?.Value,
                "Microsoft.Playwright",
                StringComparison.Ordinal));

        Require(hasPlaywright, "TaskFlow.DevCli must reference Microsoft.Playwright for browser E2E automation.");
    }

    private string Read(string relativePath)
    {
        var path = _repository.Resolve(relativePath);
        Require(File.Exists(path), $"Missing required file: {relativePath}.");
        return File.ReadAllText(path);
    }

    private string ReadTree(string relativeDirectory, IReadOnlyList<string> patterns)
    {
        var directory = _repository.Resolve(relativeDirectory);
        Require(Directory.Exists(directory), $"Missing required directory: {relativeDirectory}.");
        var files = patterns.SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories));
        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private void RequireFile(string relativePath) =>
        Require(File.Exists(_repository.Resolve(relativePath)), $"Missing required file: {relativePath}.");

    private void RequireDirectory(string relativePath) =>
        Require(Directory.Exists(_repository.Resolve(relativePath)), $"Missing required directory: {relativePath}.");

    private void RequireContains(string relativePath, string token, string message) =>
        Require(Read(relativePath).Contains(token, StringComparison.Ordinal), message);

    private void RequireNotContains(string relativePath, string token, string message) =>
        Require(!Read(relativePath).Contains(token, StringComparison.Ordinal), message);

    private static void RequireNone(string text, IReadOnlyList<string> forbidden, string description)
    {
        foreach (var token in forbidden)
        {
            Require(!text.Contains(token, StringComparison.Ordinal), $"{description}: forbidden token '{token}'.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private string Relative(string path) => Path.GetRelativePath(_repository.Root, path).Replace('\\', '/');
}
