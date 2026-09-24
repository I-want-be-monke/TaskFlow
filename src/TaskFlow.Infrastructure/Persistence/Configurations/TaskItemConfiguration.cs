using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
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

        builder.Property(task => task.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(task => task.ProjectId)
            .HasColumnName("project_id")
            .IsRequired();

        builder.Property(task => task.Title)
            .HasColumnName("title")
            .HasMaxLength(TaskItem.MaxTitleLength)
            .IsRequired();

        builder.Property(task => task.Description)
            .HasColumnName("description")
            .HasMaxLength(TaskItem.MaxDescriptionLength);

        builder.Property(task => task.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(task => task.Priority)
            .HasColumnName("priority")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(task => task.DueAt)
            .HasColumnName("due_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(task => task.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(task => task.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(task => task.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(task => task.ProjectId)
            .HasDatabaseName("ix_task_items_project_id");

        builder.HasIndex(task => new { task.ProjectId, task.Status })
            .HasDatabaseName("ix_task_items_project_id_status");

        builder.HasIndex(task => new { task.ProjectId, task.Priority })
            .HasDatabaseName("ix_task_items_project_id_priority");

        builder.HasIndex(task => task.DueAt)
            .HasDatabaseName("ix_task_items_due_at")
            .HasFilter("\"due_at\" IS NOT NULL");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(task => task.ProjectId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_task_items_projects_project_id");
    }
}
