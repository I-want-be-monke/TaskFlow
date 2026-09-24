using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tags.Common;
using TaskFlow.Domain.Tags;

namespace TaskFlow.Application.Tags.UpdateTag;

public sealed class UpdateTagHandler(
    ICurrentActor currentActor,
    ITagRepository tagRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<TagReadModel>> HandleAsync(
        UpdateTagCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = UpdateTagValidator.Validate(command);
        if (validation.IsFailure)
        {
            return Result.Failure<TagReadModel>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<TagReadModel>(TagErrors.Unauthenticated());
        }

        Tag? tag = await tagRepository.GetOwnedByIdAsync(
            currentActor.UserId,
            command.TagId,
            cancellationToken);

        if (tag is null)
        {
            return Result.Failure<TagReadModel>(TagErrors.NotFound());
        }

        if (tag.Version != command.Version)
        {
            return Result.Failure<TagReadModel>(TagErrors.VersionConflict(command.Version, tag.Version));
        }

        string normalizedName = TagValidation.NormalizeName(command.Name);
        bool duplicate = await tagRepository.ExistsOwnedByNormalizedNameAsync(
            currentActor.UserId,
            normalizedName,
            excludingTagId: tag.Id,
            cancellationToken);

        if (duplicate)
        {
            return Result.Failure<TagReadModel>(TagErrors.DuplicateName());
        }

        tag.Rename(command.Name, timeProvider.GetUtcNow());
        Result saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return Result.Failure<TagReadModel>(saveResult.Error!);
        }
        return Result.Success(tag.ToReadModel());
    }
}
