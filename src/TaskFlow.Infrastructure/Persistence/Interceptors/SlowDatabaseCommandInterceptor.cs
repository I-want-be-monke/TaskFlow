using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using TaskFlow.Infrastructure.Observability;

namespace TaskFlow.Infrastructure.Persistence.Interceptors;

public sealed class SlowDatabaseCommandInterceptor(
    ILogger<SlowDatabaseCommandInterceptor> logger,
    int thresholdMilliseconds) : DbCommandInterceptor
{
    private static readonly Action<ILogger, string, double, Exception?> LogSlowDatabaseOperation =
        LoggerMessage.Define<string, double>(
            LogLevel.Warning,
            TaskFlowLogEvents.SlowDatabaseOperation,
            "Slow database operation detected. Operation={db_operation} DurationMs={duration_ms}");

    private readonly TimeSpan _threshold = TimeSpan.FromMilliseconds(
        thresholdMilliseconds > 0
            ? thresholdMilliseconds
            : throw new ArgumentOutOfRangeException(nameof(thresholdMilliseconds)));

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LogIfSlow(eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        LogIfSlow(eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        LogIfSlow(eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(eventData);
        return ValueTask.FromResult(result);
    }

    private void LogIfSlow(CommandExecutedEventData eventData)
    {
        if (eventData.Duration < _threshold)
        {
            return;
        }

        LogSlowDatabaseOperation(
            logger,
            eventData.CommandSource.ToString(),
            Math.Round(eventData.Duration.TotalMilliseconds, 3),
            null);
    }
}
