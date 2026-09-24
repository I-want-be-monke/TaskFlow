using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tags.Common;
using TaskFlow.Domain.Tags;

namespace TaskFlow.Application.Tags.DeleteTag;

public sealed class DeleteTagHandler(
    ICurrentActor currentActor,
    ITagRepository tagRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(
        DeleteTagCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = DeleteTagValidator.Validate(command);
        if (validation.IsFailure)
        {
            return validation;
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure(TagErrors.Unauthenticated());
        }

        Tag? tag = await tagRepository.GetOwnedByIdAsync(
            currentActor.UserId,
            command.TagId,
            cancellationToken);

        if (tag is null)
        {
            return Result.Failure(TagErrors.NotFound());
        }

        if (tag.Version != command.Version)
        {
            return Result.Failure(TagErrors.VersionConflict(command.Version, tag.Version));
        }

        tagRepository.Remove(tag);
        Result saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult;
        }
        return Result.Success();
    }
}
