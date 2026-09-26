using System.Diagnostics;

namespace TaskFlow.DevCli;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => string.IsNullOrWhiteSpace(StandardError)
        ? StandardOutput
        : string.IsNullOrWhiteSpace(StandardOutput)
            ? StandardError
            : $"{StandardOutput}{Environment.NewLine}{StandardError}";
}

internal static class ProcessRunner
{
    public static int Run(string fileName, IEnumerable<string> arguments, string workingDirectory)
    {
        using var process = CreateProcess(fileName, arguments, workingDirectory, captureOutput: false);
        process.Start();
        process.WaitForExit();
        return process.ExitCode;
    }

    public static ProcessResult Capture(string fileName, IEnumerable<string> arguments, string workingDirectory)
    {
        using var process = CreateProcess(fileName, arguments, workingDirectory, captureOutput: true);
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    public static void RequireSuccess(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string failureMessage)
    {
        var exitCode = Run(fileName, arguments, workingDirectory);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{failureMessage} (exit code {exitCode}).");
        }
    }

    public static bool CommandExists(string fileName, string workingDirectory)
    {
        try
        {
            var result = Capture(fileName, ["--version"], workingDirectory);
            return result.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }

    private static Process CreateProcess(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        bool captureOutput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            CreateNoWindow = captureOutput,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = startInfo };
    }
}
