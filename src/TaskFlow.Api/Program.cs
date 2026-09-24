using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Auth;
using TaskFlow.Api.Errors;
using TaskFlow.Api.Security;
using TaskFlow.Application.Common.Abstractions;
using TaskFlow.Application.Common.Errors;
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
using TaskFlow.Infrastructure.Identity;
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
    .AddControllers(options => options.Filters.AddService<ApiAntiforgeryFilter>())
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
builder.Services.AddScoped<ApiAntiforgeryFilter>();
builder.Services.AddScoped<ICurrentActor, HttpContextCurrentActor>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<TaskFlowDbContext>(options =>
{
    options.UseNpgsql(postgresConnectionString);
    options.EnableSensitiveDataLogging(false);
});

builder.Services
    .AddDataProtection()
    .SetApplicationName("TaskFlow")
    .PersistKeysToDbContext<TaskFlowDbContext>();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = false;
    })
    .AddSignInManager()
    .AddEntityFrameworkStores<TaskFlowDbContext>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "__Host-TaskFlow.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    // Keep Identity's OnValidatePrincipal security-stamp validator installed by AddIdentityCookies.
    options.Events.OnRedirectToLogin = context => AuthenticationProblemWriter.WriteAsync(
        context.HttpContext,
        new Error(
            new ErrorCode("auth.authentication_required"),
            ErrorType.Unauthenticated,
            "Authentication is required."));
    options.Events.OnRedirectToAccessDenied = context => AuthenticationProblemWriter.WriteAsync(
        context.HttpContext,
        new Error(
            new ErrorCode("auth.forbidden"),
            ErrorType.Forbidden,
            "Access is forbidden."));
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "__Host-TaskFlow.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program
{
}
