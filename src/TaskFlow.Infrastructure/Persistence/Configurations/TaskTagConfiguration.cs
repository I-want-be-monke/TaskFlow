using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Tags;
using TaskFlow.Domain.TaskTags;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class TaskTagConfiguration : IEntityTypeConfiguration<TaskTag>
{
    public void Configure(EntityTypeBuilder<TaskTag> builder)
    {
        builder.ToTable("task_tags");

        builder.HasKey(taskTag => new { taskTag.TaskId, taskTag.TagId })
            .HasName("pk_task_tags");

        builder.Property(taskTag => taskTag.TaskId)
            .HasColumnName("task_id")
            .IsRequired();

        builder.Property(taskTag => taskTag.TagId)
            .HasColumnName("tag_id")
            .IsRequired();

        builder.Property(taskTag => taskTag.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(taskTag => new { taskTag.TagId, taskTag.TaskId })
            .HasDatabaseName("ix_task_tags_tag_id_task_id");

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(taskTag => taskTag.TaskId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_task_tags_task_items_task_id");

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(taskTag => taskTag.TagId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_task_tags_tags_tag_id");
    }
}
