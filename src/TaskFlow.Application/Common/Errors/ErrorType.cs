namespace TaskFlow.Application.Common.Errors;

public enum ErrorType
{
    Validation = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    NotFound = 4,
    Conflict = 5,
    ForbiddenByState = 6,
    InfrastructureFailure = 7,
}
