using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Tags;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags", table =>
        {
            table.HasCheckConstraint("ck_tags_name_non_empty", "char_length(name) >= 1");
            table.HasCheckConstraint("ck_tags_updated_at", "updated_at >= created_at");
            table.HasCheckConstraint("ck_tags_version", "version >= 1");
        });

        builder.HasKey(tag => tag.Id).HasName("pk_tags");

        builder.Property(tag => tag.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(tag => tag.OwnerUserId)
            .HasColumnName("owner_user_id")
            .IsRequired();

        builder.Property(tag => tag.Name)
            .HasColumnName("name")
            .HasMaxLength(Tag.MaxNameLength)
            .IsRequired();

        builder.Property(tag => tag.NormalizedName)
            .HasColumnName("normalized_name")
            .HasMaxLength(Tag.MaxNameLength)
            .IsRequired();

        builder.Property(tag => tag.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(tag => tag.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(tag => tag.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(tag => new { tag.OwnerUserId, tag.NormalizedName })
            .HasDatabaseName("ux_tags_owner_user_id_normalized_name")
            .IsUnique();

        builder.HasIndex(tag => new { tag.OwnerUserId, tag.Name })
            .HasDatabaseName("ix_tags_owner_user_id_name");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(tag => tag.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tags_auth_users_owner_user_id");
    }
}
