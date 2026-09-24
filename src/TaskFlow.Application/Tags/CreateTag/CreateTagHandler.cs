using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tags.Common;
using TaskFlow.Domain.Tags;

namespace TaskFlow.Application.Tags.CreateTag;

public sealed class CreateTagHandler(
    ICurrentActor currentActor,
    ITagRepository tagRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<TagReadModel>> HandleAsync(
        CreateTagCommand command,
        CancellationToken cancellationToken)
    {
        Result validation = CreateTagValidator.Validate(command);
        if (validation.IsFailure)
        {
            return Result.Failure<TagReadModel>(validation.Error!);
        }

        if (!currentActor.IsAuthenticated)
        {
            return Result.Failure<TagReadModel>(TagErrors.Unauthenticated());
        }

        string normalizedName = TagValidation.NormalizeName(command.Name);
        bool duplicate = await tagRepository.ExistsOwnedByNormalizedNameAsync(
            currentActor.UserId,
            normalizedName,
            excludingTagId: null,
            cancellationToken);

        if (duplicate)
        {
            return Result.Failure<TagReadModel>(TagErrors.DuplicateName());
        }

        Tag tag = Tag.Create(
            Guid.NewGuid(),
            currentActor.UserId,
            command.Name,
            timeProvider.GetUtcNow());

        await tagRepository.AddAsync(tag, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(tag.ToReadModel());
    }
}
