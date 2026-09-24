using TaskFlow.Application.Common.Errors;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tests.Tasks;

public sealed class TaskSearchQueryTests
{
    [Fact]
    public void Validate_DefaultQuery_Succeeds()
    {
        TaskSearchQuery query = new();

        Result result = query.Validate();

        Assert.True(result.IsSuccess);
        Assert.Equal(TaskSortOptions.CreatedAtDescending, query.Sort);
    }

    [Theory]
    [InlineData(TaskSortOptions.CreatedAtAscending)]
    [InlineData(TaskSortOptions.CreatedAtDescending)]
    [InlineData(TaskSortOptions.DueAtAscending)]
    [InlineData(TaskSortOptions.DueAtDescending)]
    [InlineData(TaskSortOptions.PriorityAscending)]
    [InlineData(TaskSortOptions.PriorityDescending)]
    [InlineData(TaskSortOptions.TitleAscending)]
    [InlineData(TaskSortOptions.TitleDescending)]
    public void Validate_WhitelistedSort_Succeeds(string sort)
    {
        Result result = new TaskSearchQuery(Sort: sort).Validate();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_UnknownSort_ReturnsValidationError()
    {
        Result result = new TaskSearchQuery(Sort: "drop-table:desc").Validate();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("tasks.invalid_sort", result.Error.Code.Value);
    }

    [Fact]
    public void Validate_PageBelowOne_ReturnsPaginationError()
    {
        Result result = new TaskSearchQuery(Page: 0).Validate();

        Assert.Equal("pagination.invalid_page", result.Error!.Code.Value);
    }

    [Fact]
    public void Validate_PageSizeAboveMaximum_ReturnsPaginationError()
    {
        Result result = new TaskSearchQuery(PageSize: Pagination.MaximumPageSize + 1).Validate();

        Assert.Equal("pagination.invalid_page_size", result.Error!.Code.Value);
    }

    [Fact]
    public void Validate_EmptyProjectId_ReturnsValidationError()
    {
        Result result = new TaskSearchQuery(ProjectId: Guid.Empty).Validate();

        Assert.Equal("tasks.invalid_project_id", result.Error!.Code.Value);
    }

    [Fact]
    public void Validate_EmptyTagId_ReturnsValidationError()
    {
        Result result = new TaskSearchQuery(TagId: Guid.Empty).Validate();

        Assert.Equal("tasks.invalid_tag_id", result.Error!.Code.Value);
    }

    [Fact]
    public void Validate_UnknownStatus_ReturnsValidationError()
    {
        Result result = new TaskSearchQuery(Status: (TaskFlow.Domain.Tasks.TaskStatus)999).Validate();

        Assert.Equal("tasks.invalid_status", result.Error!.Code.Value);
    }

    [Fact]
    public void Validate_UnknownPriority_ReturnsValidationError()
    {
        Result result = new TaskSearchQuery(Priority: (TaskPriority)999).Validate();

        Assert.Equal("tasks.invalid_priority", result.Error!.Code.Value);
    }

    [Fact]
    public void Validate_AllDocumentedFilters_Succeeds()
    {
        TaskSearchQuery query = new(
            ProjectId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Status: TaskFlow.Domain.Tasks.TaskStatus.InProgress,
            Priority: TaskPriority.High,
            TagId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            DueBefore: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            DueAfter: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            SearchText: "domain",
            Page: 2,
            PageSize: 25,
            Sort: TaskSortOptions.DueAtAscending);

        Assert.True(query.Validate().IsSuccess);
    }
}
