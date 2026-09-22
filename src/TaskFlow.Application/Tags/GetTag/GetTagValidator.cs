using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tags.Common;

namespace TaskFlow.Application.Tags.GetTag;

public static class GetTagValidator
{
    public static Result Validate(GetTagQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return TagValidation.ValidateTagId(query.TagId);
    }
}
