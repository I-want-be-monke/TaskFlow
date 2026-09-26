using System.ComponentModel;
using System.Text.Json;

namespace TaskFlow.DevCli;

internal sealed record DotNetSdkRequirement(
    string Version,
    string RollForward,
    bool AllowPrerelease)
{
    public static DotNetSdkRequirement Load(RepositoryContext repository)
    {
        var path = repository.Resolve("global.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("global.json not found; .NET SDK requirement cannot be determined.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("sdk", out var sdk))
        {
            throw new InvalidOperationException("global.json does not contain an 'sdk' section.");
        }

        var version = sdk.GetProperty("version").GetString();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("global.json does not define sdk.version.");
        }

        var rollForward = sdk.TryGetProperty("rollForward", out var rollForwardProperty)
            ? rollForwardProperty.GetString() ?? "patch"
            : "patch";
        var allowPrerelease = sdk.TryGetProperty("allowPrerelease", out var allowPrereleaseProperty) &&
                              allowPrereleaseProperty.GetBoolean();

        return new DotNetSdkRequirement(version, rollForward, allowPrerelease);
    }
}

internal sealed record DotNetSdkStatus(
    DotNetSdkRequirement Requirement,
    string? SelectedVersion,
    string? Error)
{
    public bool IsReady => !string.IsNullOrWhiteSpace(SelectedVersion) && string.IsNullOrWhiteSpace(Error);
}

internal static class DotNetSdkDiagnostics
{
    public static DotNetSdkStatus Inspect(RepositoryContext repository)
    {
        var requirement = DotNetSdkRequirement.Load(repository);

        try
        {
            var result = ProcessRunner.Capture("dotnet", ["--version"], repository.Root);
            if (result.ExitCode != 0)
            {
                var error = result.CombinedOutput.Trim();
                return new DotNetSdkStatus(
                    requirement,
                    null,
                    string.IsNullOrWhiteSpace(error)
                        ? $"dotnet --version failed with exit code {result.ExitCode}."
                        : error);
            }

            var selectedVersion = result.StandardOutput.Trim();
            if (string.IsNullOrWhiteSpace(selectedVersion))
            {
                return new DotNetSdkStatus(requirement, null, "dotnet --version returned an empty SDK version.");
            }

            return new DotNetSdkStatus(requirement, selectedVersion, null);
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException)
        {
            return new DotNetSdkStatus(requirement, null, "dotnet CLI is not available on PATH.");
        }
    }

    public static DotNetSdkStatus RequireReady(RepositoryContext repository)
    {
        var status = Inspect(repository);
        if (!status.IsReady)
        {
            throw new InvalidOperationException(
                $"A .NET SDK compatible with global.json is required. " +
                $"Requested: {status.Requirement.Version} " +
                $"(rollForward={status.Requirement.RollForward}, allowPrerelease={status.Requirement.AllowPrerelease}). " +
                $"{status.Error}");
        }

        return status;
    }
}
