namespace TaskFlow.DevCli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var repository = RepositoryContext.Discover();
            var parsed = CliArguments.Parse(args);
            var composeProject = new ComposeProject(repository);
            var environment = new ComposeEnvironment(repository, composeProject, parsed.EnvFile);
            var compose = new ComposeCommands(repository, environment, composeProject);
            var smoke = new SmokeCommands(environment, compose);
            var supplyChain = new SupplyChainCommands(repository, environment);
            var verification = new VerificationCommands(repository);
            var browser = new BrowserE2eCommands(environment);
            var pipeline = new PipelineCommands(repository, compose, smoke, supplyChain, verification, browser);
            var submission = new SubmissionPackCommands(repository);

            return parsed.Command switch
            {
                "up" => compose.Up(),
                "down" => compose.Down(parsed.Volumes),
                "status" or "ps" => compose.Status(),
                "logs" => compose.Logs(parsed.Tail, parsed.Positionals),
                "doctor" => compose.Doctor(),
                "smoke" => await smoke.ContainerSmokeAsync(parsed.WaitOnly).ConfigureAwait(false),
                "edge-smoke" => await smoke.EdgeSmokeAsync().ConfigureAwait(false),
                "browser-install" => BrowserE2eCommands.InstallChromium(parsed.WithDependencies),
                "browser-e2e" => await browser.RunAsync(!parsed.SkipBrowserInstall).ConfigureAwait(false),
                "verify-static" => verification.Static(parsed.SinglePositionalOrDefault("all", "verify-static")),
                "verify" => await pipeline.VerifyAsync(parsed.SinglePositionalOrDefault("all", "verify")).ConfigureAwait(false),
                "ci" => await pipeline.CiAsync(parsed.SinglePositionalOrDefault("all", "ci")).ConfigureAwait(false),
                "nuget-audit" => supplyChain.NuGetAudit(),
                "scan-secrets" => supplyChain.ScanSecrets(),
                "scan-containers" => supplyChain.ScanContainers(),
                "pack" => submission.Create(parsed.SinglePositionalOrDefault("artifacts/submission/TaskFlow-submission.zip", "pack")),
                "help" or "--help" or "-h" => PrintHelp(),
                _ => UnknownCommand(parsed.Command),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"TaskFlow CLI error: {exception.Message}");
            return 1;
        }
    }

    private static int PrintHelp()
    {
        Console.WriteLine(
            """
            TaskFlow .NET CLI

            Usage:
              dotnet run --project tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj -- <command> [options]

            Application lifecycle:
              doctor                         Check selected .NET SDK, runtime, Docker and Compose
              up                             Build and start the Docker Compose stack
              down [--volumes]               Stop the stack; optionally remove PostgreSQL volume
              status                         Show container state
              logs [--tail N] [services]     Show logs (default: 50 lines)
              smoke [--wait-only]            Container/auth/CRUD/restart smoke test
              edge-smoke                     HTTPS frontend + same-origin API smoke test

            Verification and CI/CD:
              verify-static [scope]           Static guards: architecture, security, containers, ci or all
              verify [scope]                  Local verification: architecture, security, containers or all
              ci [target]                     CI target: quality, integration, supply-chain, containers or all
              browser-install [--with-deps]   Install Playwright Chromium from .NET
              browser-e2e [--skip-browser-install]
                                              Run browser UI flow in C#/.NET
              nuget-audit                     Fail on known NuGet vulnerabilities
              scan-secrets                    Run Gitleaks in Docker
              scan-containers                 Run Trivy against TaskFlow images
              pack [output.zip]                Create a clean source archive without secrets/build output

            Common options:
              --env-file <path>               Override .env.compose.local
              --tail <N>                      Number of log lines for 'logs'
            """);
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Неизвестная команда: {command}");
        Console.Error.WriteLine("Используйте 'help' для списка команд.");
        return 2;
    }
}

internal sealed record CliArguments(
    string Command,
    string? EnvFile,
    bool Volumes,
    bool WaitOnly,
    bool WithDependencies,
    bool SkipBrowserInstall,
    int Tail,
    IReadOnlyList<string> Positionals)
{
    public static CliArguments Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CliArguments("help", null, false, false, false, false, 50, []);
        }

        var command = args[0].ToLowerInvariant();
        string? envFile = null;
        var volumes = false;
        var waitOnly = false;
        var withDependencies = false;
        var skipBrowserInstall = false;
        var tail = 50;
        var positionals = new List<string>();

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--env-file":
                    envFile = RequiredValue(args, ref index, "--env-file");
                    break;
                case "--volumes":
                    volumes = true;
                    break;
                case "--wait-only":
                    waitOnly = true;
                    break;
                case "--with-deps":
                    withDependencies = true;
                    break;
                case "--skip-browser-install":
                    skipBrowserInstall = true;
                    break;
                case "--tail":
                    var rawTail = RequiredValue(args, ref index, "--tail");
                    if (!int.TryParse(rawTail, out tail) || tail < 0)
                    {
                        throw new ArgumentException("--tail должен быть неотрицательным целым числом.");
                    }
                    break;
                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Неизвестный параметр: {argument}");
                    }

                    positionals.Add(argument);
                    break;
            }
        }

        return new CliArguments(
            command,
            envFile,
            volumes,
            waitOnly,
            withDependencies,
            skipBrowserInstall,
            tail,
            positionals);
    }

    public string SinglePositionalOrDefault(string defaultValue, string commandName)
    {
        if (Positionals.Count > 1)
        {
            throw new ArgumentException($"Команда '{commandName}' принимает не более одного target/scope.");
        }

        return Positionals.Count == 0 ? defaultValue : Positionals[0];
    }

    private static string RequiredValue(string[] args, ref int index, string option)
    {
        index++;
        if (index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Для {option} требуется значение.");
        }

        return args[index];
    }
}
