using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Contracts.Common;
using TaskFlow.Api.Contracts.Tags;
using TaskFlow.Api.Errors;
using TaskFlow.Application.Common.Pagination;
using TaskFlow.Application.Common.Results;
using TaskFlow.Application.Tags;
using TaskFlow.Application.Tags.CreateTag;
using TaskFlow.Application.Tags.DeleteTag;
using TaskFlow.Application.Tags.GetTag;
using TaskFlow.Application.Tags.ListTags;
using TaskFlow.Application.Tags.UpdateTag;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/v1/tags")]
public sealed class TagsController(
    CreateTagHandler createHandler,
    GetTagHandler getHandler,
    ListTagsHandler listHandler,
    UpdateTagHandler updateHandler,
    DeleteTagHandler deleteHandler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<TagResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<TagResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        Result<PagedResult<TagReadModel>> result = await listHandler.HandleAsync(
            new ListTagsQuery(page, pageSize),
            cancellationToken);

        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        return Ok(PagedResponse<TagResponse>.From(result.Value, static tag => tag.ToResponse()));
    }

    [HttpGet("{tagId:guid}", Name = RouteNames.GetTag)]
    [ProducesResponseType(typeof(TagResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TagResponse>> Get(
        Guid tagId,
        CancellationToken cancellationToken)
    {
        Result<TagReadModel> result = await getHandler.HandleAsync(
            new GetTagQuery(tagId),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : Ok(result.Value.ToResponse());
    }

    [HttpPost]
    [ProducesResponseType(typeof(TagResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<TagResponse>> Create(
        CreateTagRequest request,
        CancellationToken cancellationToken)
    {
        Result<TagReadModel> result = await createHandler.HandleAsync(
            new CreateTagCommand(request.Name),
            cancellationToken);

        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        TagResponse response = result.Value.ToResponse();
        return CreatedAtRoute(RouteNames.GetTag, new { tagId = response.Id }, response);
    }

    [HttpPut("{tagId:guid}")]
    [ProducesResponseType(typeof(TagResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TagResponse>> Update(
        Guid tagId,
        UpdateTagRequest request,
        CancellationToken cancellationToken)
    {
        Result<TagReadModel> result = await updateHandler.HandleAsync(
            new UpdateTagCommand(tagId, request.Name, request.Version),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : Ok(result.Value.ToResponse());
    }

    [HttpDelete("{tagId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid tagId,
        [FromQuery] long version,
        CancellationToken cancellationToken)
    {
        Result result = await deleteHandler.HandleAsync(
            new DeleteTagCommand(tagId, version),
            cancellationToken);

        return result.IsFailure
            ? this.ToProblem(result.Error!)
            : NoContent();
    }
}
