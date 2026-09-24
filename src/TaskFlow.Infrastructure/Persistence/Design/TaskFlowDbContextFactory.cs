using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskFlow.Infrastructure.Persistence.Design;

public sealed class TaskFlowDbContextFactory : IDesignTimeDbContextFactory<TaskFlowDbContext>
{
    private const string ConnectionStringEnvironmentVariable = "ConnectionStrings__Postgres";

    public TaskFlowDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"Set {ConnectionStringEnvironmentVariable} before running dotnet ef commands.");

        DbContextOptions<TaskFlowDbContext> options = new DbContextOptionsBuilder<TaskFlowDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(TaskFlowDbContext).Assembly.FullName))
            .EnableSensitiveDataLogging(false)
            .Options;

        return new TaskFlowDbContext(options);
    }
}
