using Microsoft.Extensions.Logging;

namespace TaskFlow.Infrastructure.Observability;

public static class TaskFlowLogEvents
{
    // 1000-1999: HTTP / API lifecycle
    public static readonly EventId RequestCompleted = new(1001, nameof(RequestCompleted));
    public static readonly EventId RequestRejected = new(1002, nameof(RequestRejected));
    public static readonly EventId UnhandledException = new(1003, nameof(UnhandledException));

    // 2000-2999: Application-significant use cases
    public static readonly EventId ProjectCreated = new(2001, nameof(ProjectCreated));
    public static readonly EventId ProjectArchived = new(2002, nameof(ProjectArchived));
    public static readonly EventId TaskCreated = new(2101, nameof(TaskCreated));
    public static readonly EventId TaskUpdated = new(2102, nameof(TaskUpdated));
    public static readonly EventId TagCreated = new(2201, nameof(TagCreated));

    // 3000-3999: Persistence / PostgreSQL
    public static readonly EventId DatabaseUnavailable = new(3001, nameof(DatabaseUnavailable));
    public static readonly EventId SlowDatabaseOperation = new(3002, nameof(SlowDatabaseOperation));
    public static readonly EventId ConcurrencyConflict = new(3003, nameof(ConcurrencyConflict));

    // 4000-4999: Authentication / Authorization / Security
    public static readonly EventId LoginSucceeded = new(4001, nameof(LoginSucceeded));
    public static readonly EventId LoginFailed = new(4002, nameof(LoginFailed));
    public static readonly EventId AccountLockedOut = new(4003, nameof(AccountLockedOut));
    public static readonly EventId Logout = new(4004, nameof(Logout));
    public static readonly EventId AuthorizationDenied = new(4010, nameof(AuthorizationDenied));
    public static readonly EventId CsrfValidationFailed = new(4011, nameof(CsrfValidationFailed));
    public static readonly EventId RateLimitRejected = new(4012, nameof(RateLimitRejected));
    public static readonly EventId SecurityConfigurationError = new(4013, nameof(SecurityConfigurationError));

    // 5000-5999: Admin / migration / startup
    public static readonly EventId ApplicationStarted = new(5001, nameof(ApplicationStarted));
    public static readonly EventId ApplicationStopping = new(5002, nameof(ApplicationStopping));
    public static readonly EventId ApplicationStopped = new(5003, nameof(ApplicationStopped));
    public static readonly EventId MigrationStarted = new(5101, nameof(MigrationStarted));
    public static readonly EventId MigrationCompleted = new(5102, nameof(MigrationCompleted));
    public static readonly EventId MigrationFailed = new(5103, nameof(MigrationFailed));
}
