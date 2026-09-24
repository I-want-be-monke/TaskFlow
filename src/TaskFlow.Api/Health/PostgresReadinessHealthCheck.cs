using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TaskFlow.Infrastructure.Observability;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Api.Health;

public sealed class PostgresReadinessHealthCheck(
    TaskFlowDbContext dbContext,
    ILogger<PostgresReadinessHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext _,
        CancellationToken cancellationToken = default)
    {
        try
        {
            bool canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            if (canConnect)
            {
                return HealthCheckResult.Healthy("PostgreSQL is reachable.");
            }

            logger.LogWarning(
                TaskFlowLogEvents.DatabaseUnavailable,
                "PostgreSQL readiness check failed. ErrorType={error_type}",
                "connectivity_check_failed");
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                TaskFlowLogEvents.DatabaseUnavailable,
                "PostgreSQL readiness check failed. ErrorType={error_type}",
                exception.GetType().Name);
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.");
        }
    }
}
