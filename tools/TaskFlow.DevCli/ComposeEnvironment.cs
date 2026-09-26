using System.Security.Cryptography;
using System.Text;

namespace TaskFlow.DevCli;

internal sealed class ComposeEnvironment
{
    private const string DefaultFileName = ".env.compose.local";
    private readonly RepositoryContext _repository;
    private readonly ComposeProject _project;

    public ComposeEnvironment(
        RepositoryContext repository,
        ComposeProject project,
        string? requestedPath = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        var configured = requestedPath ?? Environment.GetEnvironmentVariable("TASKFLOW_COMPOSE_ENV_FILE") ?? DefaultFileName;
        FilePath = Path.IsPathRooted(configured) ? configured : repository.Resolve(configured);
    }

    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public IReadOnlyDictionary<string, string> Read()
    {
        if (!File.Exists(FilePath))
        {
            throw new FileNotFoundException(
                $"Файл окружения не найден: {FilePath}. Сначала выполните команду 'up'.",
                FilePath);
        }

        return Parse(File.ReadAllLines(FilePath, Encoding.UTF8));
    }

    public IReadOnlyDictionary<string, string> Defaults() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TASKFLOW_HTTPS_PORT"] = ResolveOverride("TASKFLOW_HTTPS_PORT", _project.DefaultHttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ["TASKFLOW_BACKEND_SUBNET"] = ResolveOverride("TASKFLOW_BACKEND_SUBNET", _project.DefaultBackendSubnet),
            ["TASKFLOW_PROXY_IP"] = ResolveOverride("TASKFLOW_PROXY_IP", _project.DefaultProxyIp),
        };

    public IReadOnlyDictionary<string, string> EnsureForStartup()
    {
        var releaseId = ResolveReleaseId();
        var defaults = Defaults();
        List<string> lines;

        if (File.Exists(FilePath))
        {
            lines = File.ReadAllLines(FilePath, Encoding.UTF8).ToList();
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? _repository.Root);
            lines =
            [
                $"TASKFLOW_IMAGE_TAG={releaseId}",
                $"TASKFLOW_HTTPS_PORT={defaults["TASKFLOW_HTTPS_PORT"]}",
                $"TASKFLOW_BACKEND_SUBNET={defaults["TASKFLOW_BACKEND_SUBNET"]}",
                $"TASKFLOW_PROXY_IP={defaults["TASKFLOW_PROXY_IP"]}",
                $"POSTGRES_SUPERUSER_PASSWORD={NewHexSecret()}",
                $"TASKFLOW_APP_DB_PASSWORD={NewHexSecret()}",
                $"TASKFLOW_MIGRATOR_DB_PASSWORD={NewHexSecret()}",
            ];
            Console.WriteLine($"Создан локальный файл конфигурации {Path.GetRelativePath(_repository.Root, FilePath)}.");
        }

        SetValue(lines, "TASKFLOW_IMAGE_TAG", releaseId, replaceExisting: true);
        SetValue(lines, "TASKFLOW_HTTPS_PORT", defaults["TASKFLOW_HTTPS_PORT"], replaceExisting: false);
        SetValue(lines, "TASKFLOW_BACKEND_SUBNET", defaults["TASKFLOW_BACKEND_SUBNET"], replaceExisting: false);
        SetValue(lines, "TASKFLOW_PROXY_IP", defaults["TASKFLOW_PROXY_IP"], replaceExisting: false);
        SetValue(lines, "POSTGRES_SUPERUSER_PASSWORD", NewHexSecret(), replaceExisting: false);
        SetValue(lines, "TASKFLOW_APP_DB_PASSWORD", NewHexSecret(), replaceExisting: false);
        SetValue(lines, "TASKFLOW_MIGRATOR_DB_PASSWORD", NewHexSecret(), replaceExisting: false);

        File.WriteAllLines(FilePath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        RestrictFilePermissions();
        return Parse(lines);
    }

    private void RestrictFilePermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string ResolveOverride(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } configured ? configured : fallback;

    private static void SetValue(List<string> lines, string key, string value, bool replaceExisting)
    {
        var prefix = key + "=";
        var found = false;
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            found = true;
            var existingValue = lines[index][prefix.Length..].Trim();
            if (replaceExisting || string.IsNullOrWhiteSpace(existingValue))
            {
                lines[index] = prefix + value;
            }
        }

        if (!found)
        {
            lines.Add(prefix + value);
        }
    }

    private string ResolveReleaseId()
    {
        var gitMetadata = Path.Combine(_repository.Root, ".git");
        if ((!Directory.Exists(gitMetadata) && !File.Exists(gitMetadata)) ||
            !ProcessRunner.CommandExists("git", _repository.Root))
        {
            return "submission";
        }

        var insideWorkTree = ProcessRunner.Capture(
            "git",
            ["rev-parse", "--is-inside-work-tree"],
            _repository.Root);
        if (insideWorkTree.ExitCode != 0 ||
            !string.Equals(insideWorkTree.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            return "submission";
        }

        var head = ProcessRunner.Capture("git", ["rev-parse", "HEAD"], _repository.Root);
        if (head.ExitCode != 0 || string.IsNullOrWhiteSpace(head.StandardOutput))
        {
            return "submission";
        }

        var fullHash = head.StandardOutput.Trim();
        var status = ProcessRunner.Capture(
            "git",
            ["status", "--porcelain", "--untracked-files=normal"],
            _repository.Root);

        return status.ExitCode == 0 && !string.IsNullOrWhiteSpace(status.StandardOutput)
            ? $"local-{fullHash[..Math.Min(12, fullHash.Length)]}"
            : fullHash;
    }

    private static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            values[line[..separatorIndex].Trim()] = line[(separatorIndex + 1)..].Trim();
        }

        return values;
    }

    private static string NewHexSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
