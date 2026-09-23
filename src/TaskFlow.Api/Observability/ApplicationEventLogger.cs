using Microsoft.Extensions.Logging;
using TaskFlow.Infrastructure.Observability;

namespace TaskFlow.Api.Observability;

public sealed class ApplicationEventLogger(ILogger<ApplicationEventLogger> logger)
{
    public void ProjectCreated(Guid projectId) => logger.LogInformation(
        TaskFlowLogEvents.ProjectCreated,
        "Project created. ApplicationEventId={application_event_id} ProjectId={project_id}",
        TaskFlowLogEvents.ProjectCreated.Id,
        projectId);

    public void ProjectArchived(Guid projectId) => logger.LogInformation(
        TaskFlowLogEvents.ProjectArchived,
        "Project archived. ApplicationEventId={application_event_id} ProjectId={project_id}",
        TaskFlowLogEvents.ProjectArchived.Id,
        projectId);

    public void TaskCreated(Guid taskId, Guid projectId) => logger.LogInformation(
        TaskFlowLogEvents.TaskCreated,
        "Task created. ApplicationEventId={application_event_id} TaskId={task_id} ProjectId={project_id}",
        TaskFlowLogEvents.TaskCreated.Id,
        taskId,
        projectId);

    public void TaskUpdated(Guid taskId) => logger.LogInformation(
        TaskFlowLogEvents.TaskUpdated,
        "Task updated. ApplicationEventId={application_event_id} TaskId={task_id}",
        TaskFlowLogEvents.TaskUpdated.Id,
        taskId);

    public void TagCreated(Guid tagId) => logger.LogInformation(
        TaskFlowLogEvents.TagCreated,
        "Tag created. ApplicationEventId={application_event_id} TagId={tag_id}",
        TaskFlowLogEvents.TagCreated.Id,
        tagId);
}
