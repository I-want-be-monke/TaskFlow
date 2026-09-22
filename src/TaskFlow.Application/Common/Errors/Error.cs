namespace TaskFlow.Application.Common.Errors;

public sealed record Error
{
    public Error(ErrorCode code, ErrorType type, string message)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Type = type;
        Message = message;
    }

    public ErrorCode Code { get; }

    public ErrorType Type { get; }

    public string Message { get; }
}
