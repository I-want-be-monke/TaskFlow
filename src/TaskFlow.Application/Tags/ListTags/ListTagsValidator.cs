using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;

namespace TaskFlow.Application.Tags.ListTags;

public static class ListTagsValidator
{
    public static Result Validate(ListTagsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new Pagination(query.Page, query.PageSize).Validate();
    }
}
