using Microsoft.Extensions.Logging;

namespace TaskFlow.Infrastructure.Observability;

public static class TaskFlowLoggingExtensions
{
    public static ILoggingBuilder AddTaskFlowJsonConsole(
        this ILoggingBuilder builder,
        string serviceName,
        string serviceVersion,
        string deploymentEnvironment,
        string? instanceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentEnvironment);

        string resolvedInstanceId = ResolveInstanceId(instanceId);

        return builder
            .AddConsole(options => options.FormatterName = TaskFlowJsonConsoleFormatter.FormatterName)
            .AddConsoleFormatter<TaskFlowJsonConsoleFormatter, TaskFlowJsonConsoleFormatterOptions>(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.ServiceName = serviceName;
                options.ServiceVersion = serviceVersion;
                options.DeploymentEnvironment = deploymentEnvironment;
                options.InstanceId = resolvedInstanceId;
            });
    }

    private static string ResolveInstanceId(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        string? environmentInstance = Environment.GetEnvironmentVariable("TASKFLOW_INSTANCE_ID")
            ?? Environment.GetEnvironmentVariable("HOSTNAME");

        return string.IsNullOrWhiteSpace(environmentInstance)
            ? $"{Environment.MachineName}-{Environment.ProcessId}"
            : environmentInstance.Trim();
    }
}
