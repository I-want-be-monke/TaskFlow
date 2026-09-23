using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Projects;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects", table =>
        {
            table.HasCheckConstraint("ck_projects_name_non_empty", "char_length(name) >= 1");
            table.HasCheckConstraint("ck_projects_status", "status IN ('Active', 'Archived')");
            table.HasCheckConstraint("ck_projects_updated_at", "updated_at >= created_at");
            table.HasCheckConstraint("ck_projects_version", "version >= 1");
        });

        builder.HasKey(project => project.Id).HasName("pk_projects");

        builder.Property(project => project.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(project => project.OwnerUserId)
            .HasColumnName("owner_user_id")
            .IsRequired();

        builder.Property(project => project.Name)
            .HasColumnName("name")
            .HasMaxLength(Project.MaxNameLength)
            .IsRequired();

        builder.Property(project => project.Description)
            .HasColumnName("description")
            .HasMaxLength(Project.MaxDescriptionLength);

        builder.Property(project => project.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(project => project.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(project => project.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(project => project.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(project => new { project.OwnerUserId, project.Status, project.CreatedAt })
            .HasDatabaseName("ix_projects_owner_user_id_status_created_at")
            .IsDescending(false, false, true);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(project => project.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_projects_auth_users_owner_user_id");
    }
}
