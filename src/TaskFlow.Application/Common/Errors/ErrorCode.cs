namespace TaskFlow.Application.Common.Errors;

public sealed record ErrorCode
{
    public ErrorCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
