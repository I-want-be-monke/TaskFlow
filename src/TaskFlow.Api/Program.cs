using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TaskFlow.Api.Auth;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Errors;
using TaskFlow.Api.Health;
using TaskFlow.Api.Middleware;
using TaskFlow.Api.Observability;
using TaskFlow.Api.RateLimiting;
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
using TaskFlow.Contracts.Auth;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Observability;
using TaskFlow.Infrastructure.Persistence.Interceptors;
using TaskFlow.Infrastructure.Persistence.Queries;
using TaskFlow.Infrastructure.Persistence.Transactions;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Repositories;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string configuredServiceVersion = builder.Configuration["Observability:ServiceVersion"] ?? "dev";
string loggingServiceVersion = string.IsNullOrWhiteSpace(configuredServiceVersion)
    ? "invalid"
    : configuredServiceVersion;

builder.Logging.ClearProviders();
builder.Logging.AddTaskFlowJsonConsole(
    serviceName: "TaskFlow.Api",
    serviceVersion: loggingServiceVersion,
    deploymentEnvironment: builder.Environment.EnvironmentName,
    instanceId: builder.Configuration["Observability:InstanceId"]);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Connection", LogLevel.Warning);

builder.Services.AddValidatedTaskFlowOptions(builder.Configuration);
builder.Services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>, ForwardedHeadersOptionsSetup>();
builder.Services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>, CorsOptionsSetup>();
builder.Services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutOptions>, RequestTimeoutOptionsSetup>();
builder.Services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>, KestrelRequestLimitOptionsSetup>();
builder.Services.AddSingleton<IConfigureOptions<RateLimiterOptions>, RateLimiterOptionsSetup>();

builder.Services.AddCors();
builder.Services.AddRequestTimeouts();
builder.Services.AddRateLimiter();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresReadinessHealthCheck>(
        "postgres",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

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
builder.Services.AddSingleton<SecurityEventLogger>();
builder.Services.AddSingleton<ApplicationEventLogger>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new SlowDatabaseCommandInterceptor(
    sp.GetRequiredService<ILogger<SlowDatabaseCommandInterceptor>>(),
    sp.GetRequiredService<IOptions<ObservabilityOptions>>().Value.SlowDbThresholdMs));

builder.Services.AddDbContext<TaskFlowDbContext>((serviceProvider, options) =>
{
    string postgresConnectionString = serviceProvider
        .GetRequiredService<IOptions<ConnectionStringsOptions>>()
        .Value
        .Postgres;
    options.UseNpgsql(postgresConnectionString);
    options.EnableSensitiveDataLogging(false);
    options.AddInterceptors(serviceProvider.GetRequiredService<SlowDatabaseCommandInterceptor>());
});

builder.Services
    .AddDataProtection()
    .SetApplicationName("TaskFlow")
    .PersistKeysToDbContext<TaskFlowDbContext>();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = PasswordPolicyRules.RequiredLength;
        options.Password.RequiredUniqueChars = PasswordPolicyRules.RequiredUniqueChars;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

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
    options.Events.OnRedirectToLogin = context =>
    {
        context.HttpContext.RequestServices
            .GetRequiredService<SecurityEventLogger>()
            .AuthorizationDenied(context.HttpContext, "authentication_required");
        return AuthenticationProblemWriter.WriteAsync(
            context.HttpContext,
            new Error(
                new ErrorCode("auth.authentication_required"),
                ErrorType.Unauthenticated,
                "Authentication is required."));
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.HttpContext.RequestServices
            .GetRequiredService<SecurityEventLogger>()
            .AuthorizationDenied(context.HttpContext, "access_forbidden");
        return AuthenticationProblemWriter.WriteAsync(
            context.HttpContext,
            new Error(
                new ErrorCode("auth.forbidden"),
                ErrorType.Forbidden,
                "Access is forbidden."));
    };
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

ILogger lifecycleLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TaskFlow.Lifecycle");
SecurityEventLogger securityEventLogger = app.Services.GetRequiredService<SecurityEventLogger>();
app.Lifetime.ApplicationStarted.Register(() => lifecycleLogger.LogInformation(
    TaskFlowLogEvents.ApplicationStarted,
    "Application started. ProcessType={process_type}",
    "api"));
app.Lifetime.ApplicationStopping.Register(() => lifecycleLogger.LogInformation(
    TaskFlowLogEvents.ApplicationStopping,
    "Application stopping. ProcessType={process_type}",
    "api"));
app.Lifetime.ApplicationStopped.Register(() => lifecycleLogger.LogInformation(
    TaskFlowLogEvents.ApplicationStopped,
    "Application stopped. ProcessType={process_type}",
    "api"));

app.UseForwardedHeaders();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseRouting();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseRequestTimeouts();

if (app.Environment.IsDevelopment())
{
    app.UseCors(CorsOptionsSetup.DevelopmentPolicyName);
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
    })
    .AllowAnonymous()
    .DisableRateLimiting();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
    })
    .DisableRateLimiting();

try
{
    app.Run();
}
catch (OptionsValidationException)
{
    securityEventLogger.ConfigurationError("options_validation_failed");
    throw;
}

public partial class Program
{
}
