using Microsoft.EntityFrameworkCore;
using TaskFlow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace TaskFlow.IntegrationTests.Persistence;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("taskflow_tests")
        .WithUsername("taskflow_test")
        .WithPassword("taskflow_test_password")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        await using TaskFlowDbContext dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    public TaskFlowDbContext CreateDbContext()
    {
        DbContextOptions<TaskFlowDbContext> options = new DbContextOptionsBuilder<TaskFlowDbContext>()
            .UseNpgsql(ConnectionString)
            .EnableSensitiveDataLogging(false)
            .Options;

        return new TaskFlowDbContext(options);
    }
}
