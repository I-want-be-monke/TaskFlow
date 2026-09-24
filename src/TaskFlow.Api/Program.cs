using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Auth;
using TaskFlow.Api.Errors;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Projects.ArchiveProject;
using TaskFlow.Application.Projects.CreateProject;
using TaskFlow.Application.Projects.DeleteProject;
using TaskFlow.Application.Projects.GetProject;
using TaskFlow.Application.Projects.ListProjects;
using TaskFlow.Application.Projects.RestoreProject;
using TaskFlow.Application.Projects.UpdateProject;
using TaskFlow.Application.Tags.CreateTag;
using TaskFlow.Application.Tags.DeleteTag;
using TaskFlow.Application.Tags.GetTag;
using TaskFlow.Application.Tags.ListTags;
using TaskFlow.Application.Tags.UpdateTag;
using TaskFlow.Application.Tasks.AddTagToTask;
using TaskFlow.Application.Tasks.CreateTask;
using TaskFlow.Application.Tasks.DeleteTask;
using TaskFlow.Application.Tasks.GetTask;
using TaskFlow.Application.Tasks.ListTasks;
using TaskFlow.Application.Tasks.RemoveTagFromTask;
using TaskFlow.Application.Tasks.UpdateTask;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Persistence.Queries;
using TaskFlow.Infrastructure.Persistence.Transactions;
using TaskFlow.Infrastructure.Repositories;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = "The request body or parameters are invalid.",
                Type = "about:blank",
            };
            problem.Extensions["code"] = "http.invalid_request";
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            var result = new BadRequestObjectResult(problem);
            result.ContentTypes.Add("application/problem+json");
            return result;
        };
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, HttpContextCurrentActor>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<TaskFlowDbContext>(options =>
{
    options.UseNpgsql(postgresConnectionString);
    options.EnableSensitiveDataLogging(false);
});

builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<ITagRepository, TagRepository>();
builder.Services.AddScoped<IProjectQueries, ProjectQueries>();
builder.Services.AddScoped<ITaskQueries, TaskQueries>();
builder.Services.AddScoped<ITagQueries, TagQueries>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<ITransactionManager, EfTransactionManager>();

builder.Services.AddScoped<CreateProjectHandler>();
builder.Services.AddScoped<GetProjectHandler>();
builder.Services.AddScoped<ListProjectsHandler>();
builder.Services.AddScoped<UpdateProjectHandler>();
builder.Services.AddScoped<ArchiveProjectHandler>();
builder.Services.AddScoped<RestoreProjectHandler>();
builder.Services.AddScoped<DeleteProjectHandler>();

builder.Services.AddScoped<CreateTaskHandler>();
builder.Services.AddScoped<GetTaskHandler>();
builder.Services.AddScoped<ListTasksHandler>();
builder.Services.AddScoped<UpdateTaskHandler>();
builder.Services.AddScoped<DeleteTaskHandler>();
builder.Services.AddScoped<AddTagToTaskHandler>();
builder.Services.AddScoped<RemoveTagFromTaskHandler>();

builder.Services.AddScoped<CreateTagHandler>();
builder.Services.AddScoped<GetTagHandler>();
builder.Services.AddScoped<ListTagsHandler>();
builder.Services.AddScoped<UpdateTagHandler>();
builder.Services.AddScoped<DeleteTagHandler>();

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();

app.Run();

public partial class Program
{
}
