using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Api.Errors;
using TaskFlow.Api.Middleware;
using TaskFlow.Infrastructure.Observability;

namespace TaskFlow.IntegrationTests.Api;

public sealed class ObservabilityLoggingTests
{
    [Fact]
    public void Formatter_WritesOneJsonLineWithStableRequiredFields()
    {
        using var formatter = CreateFormatter();
        using var activity = new Activity("observability-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var state = new List<KeyValuePair<string, object?>>
        {
            new("http_method", "GET"),
            new("http_route", "/api/v1/projects/{projectId:guid}"),
            new("http_status_code", 200),
            new("duration_ms", 12.5),
            new("request_id", "request-123"),
            new("{OriginalFormat}", "ignored"),
        };
        var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
            LogLevel.Information,
            "TaskFlow.Contract",
            TaskFlowLogEvents.RequestCompleted,
            state,
            exception: null,
            static (_, _) => "Request completed.");
        using var writer = new StringWriter(CultureInfo.InvariantCulture);

        formatter.Write(entry, scopeProvider: null, writer);

        string[] lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string line = Assert.Single(lines);
        using JsonDocument json = JsonDocument.Parse(line);
        JsonElement root = json.RootElement;

        Assert.Equal(TaskFlowJsonConsoleFormatter.SchemaVersion, root.GetProperty("schema_version").GetString());
        Assert.True(root.TryGetProperty("@timestamp", out _));
        Assert.Equal("Information", root.GetProperty("log_level").GetString());
        Assert.Equal(1001, root.GetProperty("event_id").GetInt32());
        Assert.Equal("RequestCompleted", root.GetProperty("event_name").GetString());
        Assert.Equal("Request completed.", root.GetProperty("message").GetString());
        Assert.Equal("TaskFlow.Api", root.GetProperty("service_name").GetString());
        Assert.Equal("integration-test", root.GetProperty("service_version").GetString());
        Assert.Equal("Testing", root.GetProperty("deployment_environment").GetString());
        Assert.Equal("test-instance", root.GetProperty("instance_id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("trace_id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("span_id").GetString()));
        Assert.Equal("request-123", root.GetProperty("request_id").GetString());
        Assert.Equal("GET", root.GetProperty("http_method").GetString());
        Assert.Equal("/api/v1/projects/{projectId:guid}", root.GetProperty("http_route").GetString());
        Assert.Equal(200, root.GetProperty("http_status_code").GetInt32());
    }

    [Fact]
    public void Formatter_DropsSensitiveStructuredFieldsAndExceptionMessage()
    {
        const string password = "SuperSecretPassword!";
        const string cookie = "session-cookie-value";
        const string token = "csrf-token-value";
        const string connectionString = "Host=db;Database=taskflow;Username=taskflow;Password=db-secret";

        using var formatter = CreateFormatter();
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Password", password),
            new("Cookie", cookie),
            new("antiforgery_token", token),
            new("connection_string", connectionString),
            new("reason_code", "safe_reason"),
        };
        var exception = new InvalidOperationException($"ConnectionStrings:Postgres={connectionString}; Cookie={cookie}");
        var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
            LogLevel.Error,
            "TaskFlow.Contract",
            TaskFlowLogEvents.UnhandledException,
            state,
            exception,
            (_, _) => $"Password={password}");
        using var writer = new StringWriter(CultureInfo.InvariantCulture);

        formatter.Write(entry, scopeProvider: null, writer);

        string output = writer.ToString();
        Assert.DoesNotContain(password, output, StringComparison.Ordinal);
        Assert.DoesNotContain(cookie, output, StringComparison.Ordinal);
        Assert.DoesNotContain(token, output, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, output, StringComparison.Ordinal);
        Assert.Contains("[REDACTED_SENSITIVE_LOG_MESSAGE]", output, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", output, StringComparison.Ordinal);
        Assert.Contains("safe_reason", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestLoggingMiddleware_EmitsExactlyOneCompletionEventAndNeverErrorFor500()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            },
            logger);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        LogRecord record = Assert.Single(logger.Records);
        Assert.Equal(TaskFlowLogEvents.RequestCompleted.Id, record.EventId.Id);
        Assert.Equal(LogLevel.Warning, record.Level);
    }

    [Fact]
    public async Task UnexpectedException_ProducesOneBoundaryErrorEventAndSafeProblemDetails()
    {
        var logger = new CapturingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(logger);
        using ServiceProvider services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
        };
        context.Response.Body = new MemoryStream();
        var exception = new InvalidOperationException("Password=must-not-escape");

        bool handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        LogRecord record = Assert.Single(logger.Records);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal(TaskFlowLogEvents.UnhandledException.Id, record.EventId.Id);
        Assert.Same(exception, record.Exception);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        string problem = await reader.ReadToEndAsync();
        Assert.DoesNotContain("must-not-escape", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void EventCatalog_HasStableIdsAndNames()
    {
        AssertEvent(TaskFlowLogEvents.RequestCompleted, 1001, "RequestCompleted");
        AssertEvent(TaskFlowLogEvents.RequestRejected, 1002, "RequestRejected");
        AssertEvent(TaskFlowLogEvents.UnhandledException, 1003, "UnhandledException");
        AssertEvent(TaskFlowLogEvents.ProjectCreated, 2001, "ProjectCreated");
        AssertEvent(TaskFlowLogEvents.ProjectArchived, 2002, "ProjectArchived");
        AssertEvent(TaskFlowLogEvents.TaskCreated, 2101, "TaskCreated");
        AssertEvent(TaskFlowLogEvents.TaskUpdated, 2102, "TaskUpdated");
        AssertEvent(TaskFlowLogEvents.TagCreated, 2201, "TagCreated");
        AssertEvent(TaskFlowLogEvents.DatabaseUnavailable, 3001, "DatabaseUnavailable");
        AssertEvent(TaskFlowLogEvents.SlowDatabaseOperation, 3002, "SlowDatabaseOperation");
        AssertEvent(TaskFlowLogEvents.ConcurrencyConflict, 3003, "ConcurrencyConflict");
        AssertEvent(TaskFlowLogEvents.LoginSucceeded, 4001, "LoginSucceeded");
        AssertEvent(TaskFlowLogEvents.LoginFailed, 4002, "LoginFailed");
        AssertEvent(TaskFlowLogEvents.AccountLockedOut, 4003, "AccountLockedOut");
        AssertEvent(TaskFlowLogEvents.Logout, 4004, "Logout");
        AssertEvent(TaskFlowLogEvents.AuthorizationDenied, 4010, "AuthorizationDenied");
        AssertEvent(TaskFlowLogEvents.CsrfValidationFailed, 4011, "CsrfValidationFailed");
        AssertEvent(TaskFlowLogEvents.RateLimitRejected, 4012, "RateLimitRejected");
        AssertEvent(TaskFlowLogEvents.SecurityConfigurationError, 4013, "SecurityConfigurationError");
        AssertEvent(TaskFlowLogEvents.ApplicationStarted, 5001, "ApplicationStarted");
        AssertEvent(TaskFlowLogEvents.ApplicationStopping, 5002, "ApplicationStopping");
        AssertEvent(TaskFlowLogEvents.ApplicationStopped, 5003, "ApplicationStopped");
        AssertEvent(TaskFlowLogEvents.MigrationStarted, 5101, "MigrationStarted");
        AssertEvent(TaskFlowLogEvents.MigrationCompleted, 5102, "MigrationCompleted");
        AssertEvent(TaskFlowLogEvents.MigrationFailed, 5103, "MigrationFailed");
    }

    private static TaskFlowJsonConsoleFormatter CreateFormatter() => new(
        new StaticOptionsMonitor<TaskFlowJsonConsoleFormatterOptions>(new TaskFlowJsonConsoleFormatterOptions
        {
            IncludeScopes = true,
            UseUtcTimestamp = true,
            ServiceName = "TaskFlow.Api",
            ServiceVersion = "integration-test",
            DeploymentEnvironment = "Testing",
            InstanceId = "test-instance",
        }));

    private static void AssertEvent(EventId actual, int id, string name)
    {
        Assert.Equal(id, actual.Id);
        Assert.Equal(name, actual.Name);
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed record LogRecord(LogLevel Level, EventId EventId, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogRecord> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Records.Add(new LogRecord(logLevel, eventId, exception));
    }
}
