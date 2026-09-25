using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tags;
using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.IntegrationTests.Persistence;

public sealed class PostgresSchemaTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_FromEmptyDatabase_CreatesExpectedTables()
    {
        string[] expectedTables =
        [
            "auth_users",
            "auth_user_claims",
            "auth_user_logins",
            "auth_user_tokens",
            "data_protection_keys",
            "projects",
            "task_items",
            "tags",
            "task_tags",
        ];

        await using NpgsqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();

        foreach (string table in expectedTables)
        {
            await using NpgsqlCommand command = new(
                "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @table);",
                connection);
            command.Parameters.AddWithValue("table", table);

            Assert.True((bool)(await command.ExecuteScalarAsync())!, $"Missing table {table}.");
        }
    }

    [Fact]
    public void EfModel_UsesConcurrencyTokensAndExpectedTableNames()
    {
        using TaskFlowDbContext dbContext = fixture.CreateDbContext();

        AssertEntity(dbContext, typeof(Project), "projects", "Version");
        AssertEntity(dbContext, typeof(TaskItem), "task_items", "Version");
        AssertEntity(dbContext, typeof(Tag), "tags", "Version");

        IEntityType taskTag = dbContext.Model.FindEntityType(typeof(TaskTag))!;
        Assert.Equal("task_tags", taskTag.GetTableName());
        StoreObjectIdentifier taskTagTable = StoreObjectIdentifier.Table("task_tags", null);
        Assert.Equal("task_id", taskTag.FindProperty(nameof(TaskTag.TaskId))!.GetColumnName(taskTagTable));
        Assert.Equal("tag_id", taskTag.FindProperty(nameof(TaskTag.TagId))!.GetColumnName(taskTagTable));

        IEntityType project = dbContext.Model.FindEntityType(typeof(Project))!;
        StoreObjectIdentifier projectTable = StoreObjectIdentifier.Table("projects", null);
        Assert.Equal("owner_user_id", project.FindProperty(nameof(Project.OwnerUserId))!.GetColumnName(projectTable));
        Assert.Equal("created_at", project.FindProperty(nameof(Project.CreatedAt))!.GetColumnName(projectTable));
    }

    [Fact]
    public async Task PostgreSqlSchema_ContainsRequiredIndexesAndCheckConstraints()
    {
        string[] expectedIndexes =
        [
            "ix_projects_owner_user_id_status_created_at",
            "ix_task_items_project_id",
            "ix_task_items_project_id_status",
            "ix_task_items_project_id_priority",
            "ix_task_items_due_at",
            "ux_tags_owner_user_id_normalized_name",
            "ix_tags_owner_user_id_name",
            "ix_task_tags_tag_id_task_id",
        ];

        string[] expectedChecks =
        [
            "ck_projects_name_non_empty",
            "ck_projects_status",
            "ck_projects_updated_at",
            "ck_projects_version",
            "ck_task_items_title_non_empty",
            "ck_task_items_status",
            "ck_task_items_priority",
            "ck_task_items_updated_at",
            "ck_task_items_version",
            "ck_tags_name_non_empty",
            "ck_tags_updated_at",
            "ck_tags_version",
        ];

        await using NpgsqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();

        HashSet<string> indexes = await ReadNamesAsync(
            connection,
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'public';");

        HashSet<string> checks = await ReadNamesAsync(
            connection,
            "SELECT conname FROM pg_constraint WHERE contype = 'c';");

        Assert.All(expectedIndexes, name => Assert.Contains(name, indexes));
        Assert.All(expectedChecks, name => Assert.Contains(name, checks));
    }

    [Fact]
    public async Task TagName_IsUniquePerOwnerAfterNormalization()
    {
        Guid ownerId = Guid.NewGuid();
        await SeedUserAsync(ownerId);

        await using TaskFlowDbContext first = fixture.CreateDbContext();
        first.Tags.Add(Tag.Create(Guid.NewGuid(), ownerId, "Backend", Now));
        await first.SaveChangesAsync();

        await using TaskFlowDbContext second = fixture.CreateDbContext();
        second.Tags.Add(Tag.Create(Guid.NewGuid(), ownerId, "  backend  ", Now));

        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => second.SaveChangesAsync());

        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)exception.InnerException!).SqlState);
    }

    [Fact]
    public async Task DeletingProject_CascadesTasksAndRelations_ButKeepsTags()
    {
        Guid ownerId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid taskId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();

        await SeedUserAsync(ownerId);

        await using (TaskFlowDbContext arrange = fixture.CreateDbContext())
        {
            Project project = Project.Create(projectId, ownerId, "Project", null, Now);
            TaskItem task = TaskItem.Create(
                taskId,
                projectId,
                "Task",
                null,
                TaskFlow.Domain.Tasks.TaskStatus.Todo,
                TaskPriority.Medium,
                null,
                Now);
            Tag tag = Tag.Create(tagId, ownerId, "Tag", Now);
            TaskTag relation = TaskTag.Create(taskId, tagId, Now);

            arrange.AddRange(project, task, tag, relation);
            await arrange.SaveChangesAsync();
        }

        await using (NpgsqlConnection connection = new(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = new("DELETE FROM projects WHERE id = @id;", connection);
            command.Parameters.AddWithValue("id", projectId);
            await command.ExecuteNonQueryAsync();
        }

        await using TaskFlowDbContext assertContext = fixture.CreateDbContext();
        Assert.False(await assertContext.TaskItems.AnyAsync(task => task.Id == taskId));
        Assert.False(await assertContext.TaskTags.AnyAsync(link => link.TaskId == taskId && link.TagId == tagId));
        Assert.True(await assertContext.Tags.AnyAsync(tag => tag.Id == tagId));
    }


    [Fact]
    public async Task DeletingTag_RemovesOnlyRelation_AndKeepsTask()
    {
        Guid ownerId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid taskId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();

        await SeedUserAsync(ownerId);

        await using (TaskFlowDbContext arrange = fixture.CreateDbContext())
        {
            Project project = Project.Create(projectId, ownerId, "Project", null, Now);
            TaskItem task = TaskItem.Create(
                taskId,
                projectId,
                "Task",
                null,
                TaskFlow.Domain.Tasks.TaskStatus.Todo,
                TaskPriority.Medium,
                null,
                Now);
            Tag tag = Tag.Create(tagId, ownerId, "Tag", Now);
            TaskTag relation = TaskTag.Create(taskId, tagId, Now);

            arrange.AddRange(project, task, tag, relation);
            await arrange.SaveChangesAsync();
        }

        await using (NpgsqlConnection connection = new(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = new("DELETE FROM tags WHERE id = @id;", connection);
            command.Parameters.AddWithValue("id", tagId);
            await command.ExecuteNonQueryAsync();
        }

        await using TaskFlowDbContext assertContext = fixture.CreateDbContext();
        Assert.True(await assertContext.TaskItems.AnyAsync(task => task.Id == taskId));
        Assert.False(await assertContext.TaskTags.AnyAsync(link => link.TaskId == taskId && link.TagId == tagId));
    }

    [Fact]
    public async Task CheckConstraint_RejectsInvalidProjectStatus()
    {
        Guid ownerId = Guid.NewGuid();
        await SeedUserAsync(ownerId);

        await using NpgsqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(
            """
            INSERT INTO projects
                (id, owner_user_id, name, description, status, created_at, updated_at, version)
            VALUES
                (@id, @owner, 'Invalid status project', NULL, 'Unknown', @now, @now, 1);
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("owner", ownerId);
        command.Parameters.AddWithValue("now", Now);

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_projects_status", exception.ConstraintName);
    }

    [Fact]
    public async Task UserDelete_IsRestrictedWhileOwnedResourcesExist()
    {
        Guid ownerId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        await SeedUserAsync(ownerId);

        await using (TaskFlowDbContext arrange = fixture.CreateDbContext())
        {
            arrange.Projects.Add(Project.Create(projectId, ownerId, "Project", null, Now));
            await arrange.SaveChangesAsync();
        }

        await using NpgsqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("DELETE FROM auth_users WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", ownerId);

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
    }

    [Fact]
    public async Task BusinessTimestamps_AreStoredAsTimestamptz()
    {
        await using NpgsqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = new(
            """
            SELECT data_type
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'projects'
              AND column_name = 'created_at';
            """,
            connection);

        Assert.Equal("timestamp with time zone", (string)(await command.ExecuteScalarAsync())!);
    }

    private async Task SeedUserAsync(Guid userId)
    {
        await using TaskFlowDbContext dbContext = fixture.CreateDbContext();
        if (await dbContext.Users.AnyAsync(user => user.Id == userId))
        {
            return;
        }

        ApplicationUser user = new(userId, $"user-{userId:N}", Now)
        {
            NormalizedUserName = $"USER-{userId:N}".ToUpperInvariant(),
            PasswordHash = "integration-test-password-hash",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
    }

    private static void AssertEntity(
        TaskFlowDbContext context,
        Type entityType,
        string expectedTable,
        string versionProperty)
    {
        IEntityType metadata = context.Model.FindEntityType(entityType)!;
        Assert.Equal(expectedTable, metadata.GetTableName());
        Assert.True(metadata.FindProperty(versionProperty)!.IsConcurrencyToken);
    }

    private static async Task<HashSet<string>> ReadNamesAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        HashSet<string> result = new(StringComparer.Ordinal);

        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
