using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace TaskFlow.Infrastructure.Observability;

public sealed class TaskFlowJsonConsoleFormatter : ConsoleFormatter, IDisposable
{
    public const string FormatterName = "taskflow-json";
    public const string SchemaVersion = "1";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private static readonly HashSet<string> AllowedStructuredFields = new(StringComparer.Ordinal)
    {
        "request_id",
        "user_id",
        "http_method",
        "http_route",
        "http_status_code",
        "duration_ms",
        "error_type",
        "security_event_id",
        "outcome",
        "reason_code",
        "client_ip",
        "db_operation",
        "application_event_id",
        "entity_id",
        "project_id",
        "task_id",
        "tag_id",
        "process_type",
        "operation_id",
        "release_id",
        "from_schema_version",
        "target_schema_version",
        "applied_count",
        "failed_migration",
    };

    private readonly IDisposable? _reloadToken;
    private TaskFlowJsonConsoleFormatterOptions _options;

    public TaskFlowJsonConsoleFormatter(IOptionsMonitor<TaskFlowJsonConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _options = options.CurrentValue;
        _reloadToken = options.OnChange(updated => _options = updated);
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        string message = SanitizeMessage(logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception) ?? string.Empty);
        var structured = new Dictionary<string, object?>(StringComparer.Ordinal);

        CopyStructuredProperties(logEntry.State, structured);
        if (_options.IncludeScopes && scopeProvider is not null)
        {
            scopeProvider.ForEachScope(
                static (scope, target) => CopyStructuredProperties(scope, target),
                structured);
        }

        Activity? activity = Activity.Current;
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schema_version"] = SchemaVersion,
            ["@timestamp"] = DateTimeOffset.UtcNow,
            ["log_level"] = logEntry.LogLevel.ToString(),
            ["event_id"] = logEntry.EventId.Id,
            ["event_name"] = string.IsNullOrWhiteSpace(logEntry.EventId.Name)
                ? $"Event{logEntry.EventId.Id}"
                : logEntry.EventId.Name,
            ["message"] = message,
            ["service_name"] = _options.ServiceName,
            ["service_version"] = _options.ServiceVersion,
            ["deployment_environment"] = _options.DeploymentEnvironment,
            ["instance_id"] = _options.InstanceId,
            ["trace_id"] = activity?.TraceId.ToString(),
            ["span_id"] = activity?.SpanId.ToString(),
            ["request_id"] = structured.GetValueOrDefault("request_id"),
        };

        foreach ((string key, object? value) in structured)
        {
            if (key == "request_id")
            {
                continue;
            }

            payload[key] = NormalizeValue(value);
        }

        if (logEntry.Exception is not null)
        {
            payload["error_type"] = logEntry.Exception.GetType().FullName;
            if (!string.IsNullOrWhiteSpace(logEntry.Exception.StackTrace))
            {
                payload["exception_stack_trace"] = logEntry.Exception.StackTrace;
            }
        }

        textWriter.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    public void Dispose() => _reloadToken?.Dispose();

    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        if ((message.Contains("Host=", StringComparison.OrdinalIgnoreCase)
                && message.Contains("Password=", StringComparison.OrdinalIgnoreCase))
            || message.Contains("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ConnectionString=", StringComparison.OrdinalIgnoreCase))
        {
            return "[REDACTED_SENSITIVE_LOG_MESSAGE]";
        }

        string[] sensitiveMarkers =
        [
            "Authorization=",
            "Authorization:",
            "Cookie=",
            "Cookie:",
            "Set-Cookie=",
            "Set-Cookie:",
            "Password=",
            "Password:",
            "X-XSRF-TOKEN=",
            "X-XSRF-TOKEN:",
        ];

        foreach (string marker in sensitiveMarkers)
        {
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return "[REDACTED_SENSITIVE_LOG_MESSAGE]";
            }
        }

        return message;
    }

    private static void CopyStructuredProperties(object? state, Dictionary<string, object?> target)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> properties)
        {
            return;
        }

        foreach ((string key, object? value) in properties)
        {
            if (key == "{OriginalFormat}" || !AllowedStructuredFields.Contains(key))
            {
                continue;
            }

            target[key] = NormalizeValue(value);
        }
    }

    private static object? NormalizeValue(object? value) => value switch
    {
        Guid id => id.ToString("D"),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
        TimeSpan duration => duration.TotalMilliseconds,
        Enum enumValue => enumValue.ToString(),
        _ => value,
    };
}
