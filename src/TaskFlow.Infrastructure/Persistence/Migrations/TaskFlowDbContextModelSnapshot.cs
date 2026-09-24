using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tags;
using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TaskFlowDbContext))]
public sealed class TaskFlowDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.12")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        ConfigureApplicationUser(modelBuilder);
        ConfigureIdentityClaim(modelBuilder);
        ConfigureIdentityLogin(modelBuilder);
        ConfigureIdentityToken(modelBuilder);
        ConfigureDataProtectionKey(modelBuilder);
        ConfigureProject(modelBuilder);
        ConfigureTaskItem(modelBuilder);
        ConfigureTag(modelBuilder);
        ConfigureTaskTag(modelBuilder);
    }

    private static void ConfigureApplicationUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(builder =>
        {
            builder.ToTable("auth_users");
            builder.HasKey(user => user.Id).HasName("pk_auth_users");
            builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(user => user.UserName).HasColumnName("user_name").HasMaxLength(64).IsRequired();
            builder.Property(user => user.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(64).IsRequired();
            builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(256);
            builder.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256);
            builder.Property(user => user.EmailConfirmed).HasColumnName("email_confirmed").IsRequired();
            builder.Property(user => user.PasswordHash).HasColumnName("password_hash").HasColumnType("text").IsRequired();
            builder.Property(user => user.SecurityStamp).HasColumnName("security_stamp").HasColumnType("text").IsRequired();
            builder.Property(user => user.ConcurrencyStamp).HasColumnName("concurrency_stamp").HasColumnType("text");
            builder.Property(user => user.PhoneNumber).HasColumnName("phone_number").HasColumnType("text");
            builder.Property(user => user.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed").IsRequired();
            builder.Property(user => user.TwoFactorEnabled).HasColumnName("two_factor_enabled").IsRequired();
            builder.Property(user => user.LockoutEnd).HasColumnName("lockout_end").HasColumnType("timestamp with time zone");
            builder.Property(user => user.LockoutEnabled).HasColumnName("lockout_enabled").IsRequired();
            builder.Property(user => user.AccessFailedCount).HasColumnName("access_failed_count").IsRequired();
            builder.Property(user => user.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            builder.HasIndex(user => user.NormalizedUserName).HasDatabaseName("ux_auth_users_normalized_user_name").IsUnique();
            builder.HasIndex(user => user.NormalizedEmail).HasDatabaseName("ix_auth_users_normalized_email");
        });
    }

    private static void ConfigureIdentityClaim(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserClaim<Guid>>(builder =>
        {
            builder.ToTable("auth_user_claims");
            builder.HasKey(claim => claim.Id).HasName("pk_auth_user_claims");
            builder.Property(claim => claim.Id).HasColumnName("id").ValueGeneratedOnAdd();
            builder.Property(claim => claim.UserId).HasColumnName("user_id").IsRequired();
            builder.Property(claim => claim.ClaimType).HasColumnName("claim_type").HasColumnType("text");
            builder.Property(claim => claim.ClaimValue).HasColumnName("claim_value").HasColumnType("text");
            builder.HasIndex(claim => claim.UserId).HasDatabaseName("ix_auth_user_claims_user_id");
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(claim => claim.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auth_user_claims_auth_users_user_id");
        });
    }

    private static void ConfigureIdentityLogin(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserLogin<Guid>>(builder =>
        {
            builder.ToTable("auth_user_logins");
            builder.HasKey(login => new { login.LoginProvider, login.ProviderKey }).HasName("pk_auth_user_logins");
            builder.Property(login => login.LoginProvider).HasColumnName("login_provider").HasMaxLength(128).IsRequired();
            builder.Property(login => login.ProviderKey).HasColumnName("provider_key").HasMaxLength(128).IsRequired();
            builder.Property(login => login.ProviderDisplayName).HasColumnName("provider_display_name").HasColumnType("text");
            builder.Property(login => login.UserId).HasColumnName("user_id").IsRequired();
            builder.HasIndex(login => login.UserId).HasDatabaseName("ix_auth_user_logins_user_id");
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(login => login.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auth_user_logins_auth_users_user_id");
        });
    }

    private static void ConfigureIdentityToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserToken<Guid>>(builder =>
        {
            builder.ToTable("auth_user_tokens");
            builder.HasKey(token => new { token.UserId, token.LoginProvider, token.Name }).HasName("pk_auth_user_tokens");
            builder.Property(token => token.UserId).HasColumnName("user_id").IsRequired();
            builder.Property(token => token.LoginProvider).HasColumnName("login_provider").HasMaxLength(128).IsRequired();
            builder.Property(token => token.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            builder.Property(token => token.Value).HasColumnName("value").HasColumnType("text");
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auth_user_tokens_auth_users_user_id");
        });
    }

    private static void ConfigureDataProtectionKey(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataProtectionKey>(builder =>
        {
            builder.ToTable("data_protection_keys");
            builder.HasKey(key => key.Id).HasName("pk_data_protection_keys");
            builder.Property(key => key.Id).HasColumnName("id").ValueGeneratedOnAdd();
            builder.Property(key => key.FriendlyName).HasColumnName("friendly_name").HasColumnType("text");
            builder.Property(key => key.Xml).HasColumnName("xml").HasColumnType("text");
        });
    }

    private static void ConfigureProject(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(builder =>
        {
            builder.ToTable("projects", table =>
            {
                table.HasCheckConstraint("ck_projects_name_non_empty", "char_length(name) >= 1");
                table.HasCheckConstraint("ck_projects_status", "status IN ('Active', 'Archived')");
                table.HasCheckConstraint("ck_projects_updated_at", "updated_at >= created_at");
                table.HasCheckConstraint("ck_projects_version", "version >= 1");
            });
            builder.HasKey(project => project.Id).HasName("pk_projects");
            builder.Property(project => project.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(project => project.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
            builder.Property(project => project.Name).HasColumnName("name").HasMaxLength(Project.MaxNameLength).IsRequired();
            builder.Property(project => project.Description).HasColumnName("description").HasMaxLength(Project.MaxDescriptionLength);
            builder.Property(project => project.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
            builder.Property(project => project.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            builder.Property(project => project.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();
            builder.Property(project => project.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
            builder.HasIndex(project => new { project.OwnerUserId, project.Status, project.CreatedAt })
                .HasDatabaseName("ix_projects_owner_user_id_status_created_at").IsDescending(false, false, true);
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(project => project.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_projects_auth_users_owner_user_id");
        });
    }

    private static void ConfigureTaskItem(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskItem>(builder =>
        {
            builder.ToTable("task_items", table =>
            {
                table.HasCheckConstraint("ck_task_items_title_non_empty", "char_length(title) >= 1");
                table.HasCheckConstraint("ck_task_items_status", "status IN ('Todo', 'InProgress', 'Done')");
                table.HasCheckConstraint("ck_task_items_priority", "priority IN ('Low', 'Medium', 'High')");
                table.HasCheckConstraint("ck_task_items_updated_at", "updated_at >= created_at");
                table.HasCheckConstraint("ck_task_items_version", "version >= 1");
            });
            builder.HasKey(task => task.Id).HasName("pk_task_items");
            builder.Property(task => task.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(task => task.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(task => task.Title).HasColumnName("title").HasMaxLength(TaskItem.MaxTitleLength).IsRequired();
            builder.Property(task => task.Description).HasColumnName("description").HasMaxLength(TaskItem.MaxDescriptionLength);
            builder.Property(task => task.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(task => task.Priority).HasColumnName("priority").HasConversion<string>().HasMaxLength(16).IsRequired();
            builder.Property(task => task.DueAt).HasColumnName("due_at").HasColumnType("timestamp with time zone");
            builder.Property(task => task.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            builder.Property(task => task.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();
            builder.Property(task => task.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
            builder.HasIndex(task => task.ProjectId).HasDatabaseName("ix_task_items_project_id");
            builder.HasIndex(task => new { task.ProjectId, task.Status }).HasDatabaseName("ix_task_items_project_id_status");
            builder.HasIndex(task => new { task.ProjectId, task.Priority }).HasDatabaseName("ix_task_items_project_id_priority");
            builder.HasIndex(task => task.DueAt).HasDatabaseName("ix_task_items_due_at").HasFilter("\"due_at\" IS NOT NULL");
            builder.HasOne<Project>().WithMany().HasForeignKey(task => task.ProjectId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_task_items_projects_project_id");
        });
    }

    private static void ConfigureTag(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tag>(builder =>
        {
            builder.ToTable("tags", table =>
            {
                table.HasCheckConstraint("ck_tags_name_non_empty", "char_length(name) >= 1");
                table.HasCheckConstraint("ck_tags_updated_at", "updated_at >= created_at");
                table.HasCheckConstraint("ck_tags_version", "version >= 1");
            });
            builder.HasKey(tag => tag.Id).HasName("pk_tags");
            builder.Property(tag => tag.Id).HasColumnName("id").ValueGeneratedNever();
            builder.Property(tag => tag.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
            builder.Property(tag => tag.Name).HasColumnName("name").HasMaxLength(Tag.MaxNameLength).IsRequired();
            builder.Property(tag => tag.NormalizedName).HasColumnName("normalized_name").HasMaxLength(Tag.MaxNameLength).IsRequired();
            builder.Property(tag => tag.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            builder.Property(tag => tag.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();
            builder.Property(tag => tag.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
            builder.HasIndex(tag => new { tag.OwnerUserId, tag.NormalizedName })
                .HasDatabaseName("ux_tags_owner_user_id_normalized_name").IsUnique();
            builder.HasIndex(tag => new { tag.OwnerUserId, tag.Name }).HasDatabaseName("ix_tags_owner_user_id_name");
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(tag => tag.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_tags_auth_users_owner_user_id");
        });
    }

    private static void ConfigureTaskTag(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskTag>(builder =>
        {
            builder.ToTable("task_tags");
            builder.HasKey(taskTag => new { taskTag.TaskId, taskTag.TagId }).HasName("pk_task_tags");
            builder.Property(taskTag => taskTag.TaskId).HasColumnName("task_id").IsRequired();
            builder.Property(taskTag => taskTag.TagId).HasColumnName("tag_id").IsRequired();
            builder.Property(taskTag => taskTag.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            builder.HasIndex(taskTag => new { taskTag.TagId, taskTag.TaskId }).HasDatabaseName("ix_task_tags_tag_id_task_id");
            builder.HasOne<TaskItem>().WithMany().HasForeignKey(taskTag => taskTag.TaskId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_task_tags_task_items_task_id");
            builder.HasOne<Tag>().WithMany().HasForeignKey(taskTag => taskTag.TagId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_task_tags_tags_tag_id");
        });
    }
}
