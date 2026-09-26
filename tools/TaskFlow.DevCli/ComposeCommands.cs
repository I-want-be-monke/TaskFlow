using System.Net.Sockets;
using System.Net;

namespace TaskFlow.DevCli;

internal sealed class ComposeCommands
{
    private readonly RepositoryContext _repository;
    private readonly ComposeEnvironment _environment;
    private readonly ComposeProject _project;

    public ComposeCommands(
        RepositoryContext repository,
        ComposeEnvironment environment,
        ComposeProject project)
    {
        _repository = repository;
        _environment = environment;
        _project = project;
    }

    public int Doctor()
    {
        Console.WriteLine($"Project: {_repository.Root}");

        var sdk = DotNetSdkDiagnostics.Inspect(_repository);
        Console.WriteLine(
            $"Required SDK: {sdk.Requirement.Version} " +
            $"(rollForward: {sdk.Requirement.RollForward}, prerelease: {(sdk.Requirement.AllowPrerelease ? "allowed" : "disabled")})");

        if (!sdk.IsReady)
        {
            Console.Error.WriteLine($"Selected SDK: ERROR — {sdk.Error}");
            return 1;
        }

        Console.WriteLine($"Selected SDK: {sdk.SelectedVersion} (OK; resolved using global.json)");
        Console.WriteLine($"DevCli runtime: {Environment.Version}");
        Console.WriteLine($"Compose project: {_project.Name}");

        if (!ProcessRunner.CommandExists("docker", _repository.Root))
        {
            Console.Error.WriteLine("Docker CLI не найден. Установите Docker Desktop.");
            return 1;
        }

        var composeVersion = ProcessRunner.Capture("docker", ["compose", "version"], _repository.Root);
        if (composeVersion.ExitCode != 0)
        {
            Console.Error.WriteLine("Docker Compose v2 недоступен. Требуется команда 'docker compose'.");
            return 1;
        }

        Console.WriteLine(composeVersion.StandardOutput.Trim());

        var engine = ProcessRunner.Capture("docker", ["info"], _repository.Root);
        if (engine.ExitCode != 0)
        {
            Console.Error.WriteLine("Docker Engine недоступен. Запустите Docker Desktop.");
            return 1;
        }

        Console.WriteLine("Docker Engine: OK");
        IReadOnlyDictionary<string, string> endpointValues = _environment.Exists
            ? _environment.Read()
            : _environment.Defaults();
        Console.WriteLine($"HTTPS port: {endpointValues["TASKFLOW_HTTPS_PORT"]}");
        Console.WriteLine($"Backend subnet: {endpointValues["TASKFLOW_BACKEND_SUBNET"]}");
        Console.WriteLine($"Proxy IP: {endpointValues["TASKFLOW_PROXY_IP"]}");
        Console.WriteLine(_environment.Exists
            ? $"Compose env: {Path.GetRelativePath(_repository.Root, _environment.FilePath)}"
            : "Compose env: будет создан при первом 'up'");
        return 0;
    }

    public int Up()
    {
        EnsureDockerReady();
        EnsureNoLegacyProjectConflict();
        EnsureEnvironmentAvailableForPersistentState();

        var values = _environment.EnsureForStartup();
        EnsureHostPortAvailable(values);

        RunCompose(["config", "--quiet"], "Конфигурация Docker Compose некорректна.");
        var upExitCode = Compose(["up", "--build", "--detach", "--wait", "--wait-timeout", "240"]);
        if (upExitCode != 0)
        {
            PrintStartupDiagnostics();
            throw new InvalidOperationException($"Не удалось запустить TaskFlow. Код завершения: {upExitCode}.");
        }

        var port = values.GetValueOrDefault("TASKFLOW_HTTPS_PORT", "8443");
        Console.WriteLine();
        Console.WriteLine($"TaskFlow запущен: https://localhost:{port}/");
        Console.WriteLine($"Compose project: {_project.Name}");
        Console.WriteLine("Локальный TLS-сертификат самоподписанный — браузер может попросить подтвердить исключение.");
        Console.WriteLine("Логи:   dotnet run --project tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj -- logs --tail 50");
        Console.WriteLine("Статус: dotnet run --project tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj -- status");
        Console.WriteLine("Стоп:   dotnet run --project tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj -- down");
        return 0;
    }

    public int Down(bool volumes)
    {
        EnsureDockerCli();

        if (!_environment.Exists)
        {
            CleanupWithoutEnvironment(volumes);
            CleanupLegacyProjectForRepository(volumes);
            Console.WriteLine(volumes
                ? "TaskFlow остановлен; PostgreSQL volumes также удалены. Compose env отсутствовал, поэтому ресурсы найдены по project label."
                : "TaskFlow остановлен. Compose env отсутствовал, поэтому ресурсы найдены по project label.");
            return 0;
        }

        var arguments = new List<string> { "down", "--remove-orphans" };
        if (volumes)
        {
            arguments.Add("--volumes");
        }

        RunCompose(arguments, "Не удалось остановить TaskFlow.");
        CleanupLegacyProjectForRepository(volumes);
        Console.WriteLine(volumes
            ? "TaskFlow остановлен; PostgreSQL volumes также удалены."
            : "TaskFlow остановлен.");
        return 0;
    }

    public int Status()
    {
        EnsureDockerCli();
        _environment.Read();
        return Compose(["ps"]);
    }

    public int Logs(int tail, IReadOnlyList<string> services)
    {
        EnsureDockerCli();
        _environment.Read();
        var arguments = new List<string> { "logs", "--tail", tail.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        arguments.AddRange(services);
        return Compose(arguments);
    }

    public int Restart(string service)
    {
        EnsureDockerReady();
        _environment.Read();
        RunCompose(["restart", service], $"Не удалось перезапустить сервис '{service}'.");
        return 0;
    }

    public ProcessResult CaptureCompose(IEnumerable<string> arguments)
    {
        var composeArguments = BuildComposeArguments(arguments);
        return ProcessRunner.Capture("docker", composeArguments, _repository.Root);
    }

    private void PrintStartupDiagnostics()
    {
        try
        {
            ProcessResult status = CaptureCompose(["ps", "--all"]);
            if (!string.IsNullOrWhiteSpace(status.CombinedOutput))
            {
                Console.Error.WriteLine("\nDocker Compose status:");
                Console.Error.WriteLine(status.CombinedOutput.Trim());
            }

            ProcessResult frontendIdResult = CaptureCompose(["ps", "--all", "--quiet", "frontend"]);
            string frontendId = frontendIdResult.StandardOutput.Trim();
            if (!string.IsNullOrWhiteSpace(frontendId))
            {
                ProcessResult health = ProcessRunner.Capture(
                    "docker",
                    ["inspect", "--format", "{{json .State.Health}}", frontendId],
                    _repository.Root);
                if (!string.IsNullOrWhiteSpace(health.CombinedOutput))
                {
                    Console.Error.WriteLine("\nFrontend health state:");
                    Console.Error.WriteLine(health.CombinedOutput.Trim());
                }
            }

            ProcessResult frontendLogs = CaptureCompose(["logs", "--no-color", "--tail", "80", "frontend"]);
            if (!string.IsNullOrWhiteSpace(frontendLogs.CombinedOutput))
            {
                Console.Error.WriteLine("\nFrontend logs:");
                Console.Error.WriteLine(frontendLogs.CombinedOutput.Trim());
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Не удалось автоматически собрать startup diagnostics: {exception.Message}");
        }
    }

    private void EnsureDockerReady()
    {
        EnsureDockerCli();
        var info = ProcessRunner.Capture("docker", ["info"], _repository.Root);
        if (info.ExitCode != 0)
        {
            throw new InvalidOperationException("Docker Engine недоступен. Запустите Docker Desktop.");
        }
    }

    private void EnsureDockerCli()
    {
        if (!ProcessRunner.CommandExists("docker", _repository.Root))
        {
            throw new InvalidOperationException("Docker CLI не найден. Установите Docker Desktop.");
        }

        var compose = ProcessRunner.Capture("docker", ["compose", "version"], _repository.Root);
        if (compose.ExitCode != 0)
        {
            throw new InvalidOperationException("Требуется Docker Compose v2 (команда 'docker compose').");
        }
    }

    private void EnsureEnvironmentAvailableForPersistentState()
    {
        if (_environment.Exists)
        {
            return;
        }

        if (!DockerVolumeExists(_project.PostgresVolumeName) && !ProjectContainersExist(_project.Name))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Обнаружены Docker-ресурсы Compose project '{_project.Name}', но файл " +
            $"'{Path.GetRelativePath(_repository.Root, _environment.FilePath)}' отсутствует. " +
            "DevCli не будет генерировать новые пароли поверх существующей PostgreSQL БД. " +
            "Восстановите прежний env-файл либо, если локальные данные не нужны, выполните " +
            "'dotnet run --project tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj -- down --volumes', " +
            "а затем повторите 'up'.");
    }

    private void EnsureNoLegacyProjectConflict()
    {
        const string legacyProjectName = "taskflow";
        if (string.Equals(_project.Name, legacyProjectName, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var containerId in GetDockerIds(
                     "ps",
                     ["--all", "--filter", $"label=com.docker.compose.project={legacyProjectName}", "--format", "{{.ID}}"] ))
        {
            var workingDirectory = ProcessRunner.Capture(
                "docker",
                [
                    "inspect",
                    "--format",
                    "{{ index .Config.Labels \"com.docker.compose.project.working_dir\" }}",
                    containerId,
                ],
                _repository.Root);

            if (workingDirectory.ExitCode != 0 || !SamePath(workingDirectory.StandardOutput.Trim(), _repository.Root))
            {
                continue;
            }

            throw new InvalidOperationException(
                "Обнаружен Docker Compose stack старой версии этого проекта с общим project name 'taskflow'. " +
                "Остановите его встроенной командой DevCli: " +
                "'dotnet run --project tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj -- down --volumes' " +
                "(без --volumes, если нужно сохранить прежнюю локальную БД), затем повторите 'up'. " +
                "Новая версия использует уникальный Compose project name, порт и subnet для каждой копии репозитория.");
        }
    }

    private void CleanupWithoutEnvironment(bool removeVolumes)
    {
        RemoveDockerResources(
            _project.Name,
            "container",
            GetDockerIds(
                "ps",
                ["--all", "--filter", $"label=com.docker.compose.project={_project.Name}", "--format", "{{.ID}}"]),
            ["rm", "--force"]);

        RemoveDockerResources(
            _project.Name,
            "network",
            GetDockerIds(
                "network",
                ["ls", "--filter", $"label=com.docker.compose.project={_project.Name}", "--format", "{{.ID}}"]),
            ["network", "rm"]);

        if (removeVolumes && DockerVolumeExists(_project.PostgresVolumeName))
        {
            ProcessRunner.RequireSuccess(
                "docker",
                ["volume", "rm", _project.PostgresVolumeName],
                _repository.Root,
                $"Не удалось удалить PostgreSQL volume '{_project.PostgresVolumeName}'");
        }
    }

    private void RemoveDockerResources(
        string projectName,
        string resourceName,
        string[] resourceIds,
        string[] removeCommand)
    {
        if (resourceIds.Length == 0)
        {
            return;
        }

        var arguments = new List<string>(removeCommand);
        arguments.AddRange(resourceIds);
        ProcessRunner.RequireSuccess(
            "docker",
            arguments,
            _repository.Root,
            $"Не удалось удалить Docker {resourceName} resources для Compose project '{projectName}'");
    }

    private void CleanupLegacyProjectForRepository(bool removeVolumes)
    {
        const string legacyProjectName = "taskflow";
        if (string.Equals(_project.Name, legacyProjectName, StringComparison.Ordinal))
        {
            return;
        }

        string[] legacyContainers = GetDockerIds(
            "ps",
            ["--all", "--filter", $"label=com.docker.compose.project={legacyProjectName}", "--format", "{{.ID}}"]);

        bool belongsToRepository = legacyContainers.Any(containerId => ContainerBelongsToRepository(containerId));
        if (!belongsToRepository)
        {
            return;
        }

        RemoveDockerResources(legacyProjectName, "container", legacyContainers, ["rm", "--force"]);
        RemoveDockerResources(
            legacyProjectName,
            "network",
            GetDockerIds(
                "network",
                ["ls", "--filter", $"label=com.docker.compose.project={legacyProjectName}", "--format", "{{.ID}}"]),
            ["network", "rm"]);

        if (removeVolumes)
        {
            RemoveDockerResources(
                legacyProjectName,
                "volume",
                GetDockerIds(
                    "volume",
                    ["ls", "--filter", $"label=com.docker.compose.project={legacyProjectName}", "--format", "{{.Name}}"]),
                ["volume", "rm"]);
        }
    }

    private bool ContainerBelongsToRepository(string containerId)
    {
        var workingDirectory = ProcessRunner.Capture(
            "docker",
            [
                "inspect",
                "--format",
                "{{ index .Config.Labels \"com.docker.compose.project.working_dir\" }}",
                containerId,
            ],
            _repository.Root);

        return workingDirectory.ExitCode == 0 &&
               SamePath(workingDirectory.StandardOutput.Trim(), _repository.Root);
    }

    private void EnsureHostPortAvailable(IReadOnlyDictionary<string, string> values)
    {
        if (ProjectContainersExist(_project.Name))
        {
            return;
        }

        if (!values.TryGetValue("TASKFLOW_HTTPS_PORT", out string? rawPort) ||
            !int.TryParse(rawPort, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port) ||
            port is < 1 or > 65535)
        {
            throw new InvalidOperationException("TASKFLOW_HTTPS_PORT must be an integer between 1 and 65535.");
        }

        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
        }
        catch (SocketException exception)
        {
            throw new InvalidOperationException(
                $"HTTPS port {port} is already in use. Set TASKFLOW_HTTPS_PORT to another free port and repeat 'up'.",
                exception);
        }
    }

    private bool ProjectContainersExist(string projectName) =>
        GetDockerIds(
            "ps",
            ["--all", "--filter", $"label=com.docker.compose.project={projectName}", "--format", "{{.ID}}"])
        .Length > 0;

    private bool DockerVolumeExists(string volumeName)
    {
        var result = ProcessRunner.Capture("docker", ["volume", "inspect", volumeName], _repository.Root);
        return result.ExitCode == 0;
    }

    private string[] GetDockerIds(
        string command,
        string[] arguments)
    {
        var dockerArguments = new List<string> { command };
        dockerArguments.AddRange(arguments);
        var result = ProcessRunner.Capture("docker", dockerArguments, _repository.Root);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Не удалось получить список Docker resources: {result.StandardError.Trim()}");
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool SamePath(string candidate, string expected)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            var normalizedExpected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected));
            return string.Equals(
                normalizedCandidate,
                normalizedExpected,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private int Compose(IEnumerable<string> arguments) =>
        ProcessRunner.Run("docker", BuildComposeArguments(arguments), _repository.Root);

    private void RunCompose(IEnumerable<string> arguments, string failureMessage)
    {
        var exitCode = Compose(arguments);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{failureMessage} Код завершения: {exitCode}.");
        }
    }

    private IEnumerable<string> BuildComposeArguments(IEnumerable<string> arguments)
    {
        yield return "compose";
        yield return "--project-name";
        yield return _project.Name;
        yield return "--env-file";
        yield return _environment.FilePath;
        foreach (var argument in arguments)
        {
            yield return argument;
        }
    }
}
