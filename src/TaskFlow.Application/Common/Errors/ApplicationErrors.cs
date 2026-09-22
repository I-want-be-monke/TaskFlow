namespace TaskFlow.Application.Common.Errors;

public static class ApplicationErrors
{
    public static Error Validation(string code, string message) =>
        Create(code, ErrorType.Validation, message);

    public static Error Unauthenticated(string code, string message) =>
        Create(code, ErrorType.Unauthenticated, message);

    public static Error Forbidden(string code, string message) =>
        Create(code, ErrorType.Forbidden, message);

    public static Error NotFound(string code, string message) =>
        Create(code, ErrorType.NotFound, message);

    public static Error Conflict(string code, string message) =>
        Create(code, ErrorType.Conflict, message);

    public static Error ForbiddenByState(string code, string message) =>
        Create(code, ErrorType.ForbiddenByState, message);

    public static Error InfrastructureFailure(string code, string message) =>
        Create(code, ErrorType.InfrastructureFailure, message);

    private static Error Create(string code, ErrorType type, string message) =>
        new(new ErrorCode(code), type, message);
}
