using System.Text.Json;

namespace TaskFlow.DevCli;

internal sealed class SupplyChainCommands
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    private readonly RepositoryContext _repository;
    private readonly ComposeEnvironment _environment;

    public SupplyChainCommands(RepositoryContext repository, ComposeEnvironment environment)
    {
        _repository = repository;
        _environment = environment;
    }

    public int NuGetAudit()
    {
        var artifacts = _repository.Resolve("artifacts");
        Directory.CreateDirectory(artifacts);
        var findings = new List<JsonElement>();

        foreach (var (name, target) in new[]
                 {
                     ("solution", "TaskFlow.sln"),
                     ("devcli", "tools/TaskFlow.DevCli/TaskFlow.DevCli.csproj"),
                 })
        {
            var result = ProcessRunner.Capture(
                "dotnet",
                [
                    "package", "list", "--project", target, "--include-transitive", "--vulnerable",
                    "--format", "json", "--output-version", "1", "--no-restore",
                ],
                _repository.Root);
            if (result.ExitCode != 0)
            {
                Console.Write(result.StandardOutput);
                Console.Error.Write(result.StandardError);
                return result.ExitCode;
            }

            using var document = JsonDocument.Parse(result.StandardOutput);
            var formatted = JsonSerializer.Serialize(document.RootElement, IndentedJsonOptions);
            File.WriteAllText(Path.Combine(artifacts, $"nuget-vulnerabilities-{name}.json"), formatted + Environment.NewLine);
            CollectVulnerabilities(document.RootElement, findings);
        }

        if (findings.Count > 0)
        {
            Console.Error.WriteLine("Known NuGet vulnerabilities were found.");
            return 1;
        }

        Console.WriteLine("NuGet vulnerability gate passed for application solution and TaskFlow.DevCli.");
        return 0;
    }

    public int ScanSecrets()
    {
        var artifacts = _repository.Resolve("artifacts");
        Directory.CreateDirectory(artifacts);
        const string image = "zricethezav/gitleaks:v8.30.1";

        var scanMode = Directory.Exists(_repository.Resolve(".git")) || File.Exists(_repository.Resolve(".git"))
            ? "git"
            : "dir";
        var result = ProcessRunner.Run(
            "docker",
            [
                "run", "--rm",
                "-v", $"{_repository.Root}:/repo:ro",
                "-v", $"{artifacts}:/reports",
                image,
                scanMode, "--redact", "--report-format", "json", "--report-path", "/reports/gitleaks-report.json", "/repo",
            ],
            _repository.Root);
        if (result != 0)
        {
            return result;
        }

        Console.WriteLine($"Gitleaks secret scan passed using {image}.");
        return 0;
    }

    public int ScanContainers()
    {
        var values = _environment.Read();
        var tag = values.GetValueOrDefault("TASKFLOW_IMAGE_TAG")
            ?? throw new InvalidOperationException("TASKFLOW_IMAGE_TAG is missing from the compose environment.");

        var artifacts = _repository.Resolve(Path.Combine("artifacts", "trivy"));
        Directory.CreateDirectory(artifacts);
        ProcessRunner.RequireSuccess("docker", ["volume", "create", "taskflow-trivy-cache"], _repository.Root, "Cannot create Trivy cache volume");

        const string scanner = "aquasec/trivy:0.70.0";
        foreach (var image in new[] { $"taskflow-api:{tag}", $"taskflow-migrator:{tag}", $"taskflow-frontend:{tag}" })
        {
            var safeName = image.Replace(':', '-').Replace('/', '-');
            Console.WriteLine($"Scanning {image} (HIGH/CRITICAL report; fixable CRITICAL gate)...");

            var reportExitCode = ProcessRunner.Run(
                "docker",
                [
                    "run", "--rm",
                    "-v", "/var/run/docker.sock:/var/run/docker.sock",
                    "-v", "taskflow-trivy-cache:/root/.cache/",
                    "-v", $"{artifacts}:/reports",
                    scanner, "image", "--scanners", "vuln", "--severity", "HIGH,CRITICAL",
                    "--format", "json", "--output", $"/reports/{safeName}.json", image,
                ],
                _repository.Root);
            if (reportExitCode != 0)
            {
                return reportExitCode;
            }

            var gateExitCode = ProcessRunner.Run(
                "docker",
                [
                    "run", "--rm",
                    "-v", "/var/run/docker.sock:/var/run/docker.sock",
                    "-v", "taskflow-trivy-cache:/root/.cache/",
                    scanner, "image", "--scanners", "vuln", "--severity", "CRITICAL",
                    "--ignore-unfixed", "--exit-code", "1", image,
                ],
                _repository.Root);
            if (gateExitCode != 0)
            {
                return gateExitCode;
            }
        }

        Console.WriteLine("Container vulnerability gate passed for all TaskFlow images.");
        return 0;
    }

    private static void CollectVulnerabilities(JsonElement element, ICollection<JsonElement> findings)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("vulnerabilities", out var vulnerabilities) &&
                vulnerabilities.ValueKind == JsonValueKind.Array &&
                vulnerabilities.GetArrayLength() > 0)
            {
                findings.Add(element.Clone());
            }

            foreach (var property in element.EnumerateObject())
            {
                CollectVulnerabilities(property.Value, findings);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectVulnerabilities(item, findings);
            }
        }
    }

    private static string? TryString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
