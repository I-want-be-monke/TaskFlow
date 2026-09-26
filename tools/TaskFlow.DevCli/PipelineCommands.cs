namespace TaskFlow.DevCli;

internal sealed class PipelineCommands
{
    private readonly RepositoryContext _repository;
    private readonly ComposeCommands _compose;
    private readonly SmokeCommands _smoke;
    private readonly SupplyChainCommands _supplyChain;
    private readonly VerificationCommands _verification;
    private readonly BrowserE2eCommands _browser;

    public PipelineCommands(
        RepositoryContext repository,
        ComposeCommands compose,
        SmokeCommands smoke,
        SupplyChainCommands supplyChain,
        VerificationCommands verification,
        BrowserE2eCommands browser)
    {
        _repository = repository;
        _compose = compose;
        _smoke = smoke;
        _supplyChain = supplyChain;
        _verification = verification;
        _browser = browser;
    }

    public async Task<int> VerifyAsync(string scope)
    {
        var normalizedScope = NormalizeVerifyScope(scope);
        _verification.Static(normalizedScope);
        VerifySdk();

        switch (normalizedScope)
        {
            case "architecture":
                RestoreSolution();
                BuildSolution();
                RunUnitTests();
                break;

            case "security":
                EnsureDocker();
                RestoreSolution();
                BuildSolution();
                RunUnitTests();
                RunIntegrationTests();
                break;

            case "containers":
                EnsureDocker();
                await RunContainerPipelineAsync(scanContainers: true, browserE2e: true, installBrowser: true)
                    .ConfigureAwait(false);
                break;

            case "all":
                RestoreSolution();
                BuildSolution();
                RunUnitTests();
                EnsureDocker();
                RunIntegrationTests();
                RequireExit(_supplyChain.NuGetAudit(), "NuGet vulnerability audit failed.");
                RequireExit(_supplyChain.ScanSecrets(), "Secret scan failed.");
                await RunContainerPipelineAsync(scanContainers: true, browserE2e: true, installBrowser: true)
                    .ConfigureAwait(false);
                break;
        }

        Console.WriteLine($"Verification completed successfully: {normalizedScope}.");
        return 0;
    }

    private static string NormalizeVerifyScope(string scope)
    {
        var normalized = scope.Trim().ToLowerInvariant();
        return normalized switch
        {
            "architecture" => normalized,
            "security" => normalized,
            "containers" => normalized,
            "all" => normalized,
            _ => throw new ArgumentException(
                "verify scope must be one of: architecture, security, containers, all."),
        };
    }

    public async Task<int> CiAsync(string target)
    {
        switch (target.ToLowerInvariant())
        {
            case "quality":
                return Quality();
            case "integration":
                return Integration();
            case "supply-chain":
            case "supply_chain":
                return SupplyChain();
            case "containers":
                return await ContainersAsync().ConfigureAwait(false);
            case "all":
                Quality();
                Integration();
                SupplyChain();
                return await ContainersAsync().ConfigureAwait(false);
            default:
                throw new ArgumentException("CI target must be one of: quality, integration, supply-chain, containers, all.");
        }
    }

    private int Quality()
    {
        Console.WriteLine("== CI quality ==");
        _verification.Static("all");
        VerifySdk();
        RestoreSolution();
        BuildSolution();
        RunUnitTests();
        Console.WriteLine("CI quality passed.");
        return 0;
    }

    private int Integration()
    {
        Console.WriteLine("== CI integration ==");
        VerifySdk();
        EnsureDocker();
        RestoreSolution();
        BuildSolution();
        RunIntegrationTests();
        Console.WriteLine("CI integration passed.");
        return 0;
    }

    private int SupplyChain()
    {
        Console.WriteLine("== CI supply-chain ==");
        VerifySdk();
        EnsureDocker();
        RestoreSolution();
        RequireExit(_supplyChain.NuGetAudit(), "NuGet vulnerability audit failed.");
        RequireExit(_supplyChain.ScanSecrets(), "Secret scan failed.");
        Console.WriteLine("CI supply-chain passed.");
        return 0;
    }

    private async Task<int> ContainersAsync()
    {
        Console.WriteLine("== CI containers ==");
        VerifySdk();
        EnsureDocker();
        await RunContainerPipelineAsync(scanContainers: true, browserE2e: true, installBrowser: true)
            .ConfigureAwait(false);
        Console.WriteLine("CI containers passed.");
        return 0;
    }

    private async Task RunContainerPipelineAsync(bool scanContainers, bool browserE2e, bool installBrowser)
    {
        var composeAttempted = false;
        try
        {
            composeAttempted = true;
            _compose.Up();

            if (scanContainers)
            {
                RequireExit(_supplyChain.ScanContainers(), "Container vulnerability scan failed.");
            }

            RequireExit(await _smoke.ContainerSmokeAsync(waitOnly: false).ConfigureAwait(false), "Container smoke failed.");
            RequireExit(await _smoke.EdgeSmokeAsync().ConfigureAwait(false), "Edge smoke failed.");

            if (browserE2e)
            {
                RequireExit(await _browser.RunAsync(installBrowser).ConfigureAwait(false), "Browser E2E failed.");
            }
        }
        catch
        {
            if (composeAttempted)
            {
                SaveComposeDiagnostics();
            }

            throw;
        }
        finally
        {
            if (composeAttempted)
            {
                try
                {
                    _compose.Down(volumes: IsCi());
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Compose cleanup failed: {exception.Message}");
                }
            }
        }
    }

    private void RestoreSolution() =>
        ProcessRunner.RequireSuccess(
            "dotnet",
            ["restore", "TaskFlow.sln", "--locked-mode"],
            _repository.Root,
            "Locked solution restore failed");

    private void BuildSolution() =>
        ProcessRunner.RequireSuccess(
            "dotnet",
            ["build", "TaskFlow.sln", "--no-restore", "--configuration", "Release"],
            _repository.Root,
            "Release build failed");

    private void RunUnitTests()
    {
        RunTests("tests/TaskFlow.Domain.Tests/TaskFlow.Domain.Tests.csproj");
        RunTests("tests/TaskFlow.Application.Tests/TaskFlow.Application.Tests.csproj");
        RunTests("tests/TaskFlow.DevCli.Tests/TaskFlow.DevCli.Tests.csproj");
    }

    private void RunIntegrationTests() => RunTests("tests/TaskFlow.IntegrationTests/TaskFlow.IntegrationTests.csproj");

    private void RunTests(string project) =>
        ProcessRunner.RequireSuccess(
            "dotnet",
            ["test", project, "--no-build", "--no-restore", "--configuration", "Release"],
            _repository.Root,
            $"Tests failed: {project}");

    private void VerifySdk()
    {
        var status = DotNetSdkDiagnostics.RequireReady(_repository);
        Console.WriteLine(
            $".NET SDK: {status.SelectedVersion} " +
            $"(global.json: {status.Requirement.Version}, rollForward: {status.Requirement.RollForward})");
    }

    private void EnsureDocker()
    {
        var result = _compose.Doctor();
        RequireExit(result, "Docker/Compose prerequisite check failed.");
    }

    private void SaveComposeDiagnostics()
    {
        try
        {
            var artifacts = _repository.Resolve("artifacts");
            Directory.CreateDirectory(artifacts);
            var result = _compose.CaptureCompose(["logs", "--no-color"]);
            File.WriteAllText(Path.Combine(artifacts, "docker-compose.log"), result.CombinedOutput);
            Console.Error.WriteLine("Compose diagnostics saved to artifacts/docker-compose.log.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not save compose diagnostics: {exception.Message}");
        }
    }

    private static bool IsCi() =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    private static void RequireExit(int exitCode, string message)
    {
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{message} Exit code: {exitCode}.");
        }
    }
}
