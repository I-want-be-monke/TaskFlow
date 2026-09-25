namespace TaskFlow.DevCli.Tests;

internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly Dictionary<string, string?> _previousValues = new(StringComparer.Ordinal);

    public EnvironmentVariableScope(params string[] names)
    {
        foreach (string name in names)
        {
            _previousValues[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach ((string name, string? value) in _previousValues)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
