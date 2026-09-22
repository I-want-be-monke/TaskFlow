namespace TaskFlow.Application.Common.Abstractions;

public interface ICurrentActor
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }
}
