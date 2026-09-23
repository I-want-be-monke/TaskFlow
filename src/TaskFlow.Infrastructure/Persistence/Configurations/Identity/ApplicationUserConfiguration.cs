using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations.Identity;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("auth_users");

        builder.HasKey(user => user.Id).HasName("pk_auth_users");

        builder.Property(user => user.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(user => user.UserName)
            .HasColumnName("user_name")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(user => user.NormalizedUserName)
            .HasColumnName("normalized_user_name")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(256);

        builder.Property(user => user.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(256);

        builder.Property(user => user.EmailConfirmed)
            .HasColumnName("email_confirmed")
            .IsRequired();

        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(user => user.SecurityStamp)
            .HasColumnName("security_stamp")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(user => user.ConcurrencyStamp)
            .HasColumnName("concurrency_stamp")
            .HasColumnType("text");

        builder.Property(user => user.PhoneNumber)
            .HasColumnName("phone_number")
            .HasColumnType("text");

        builder.Property(user => user.PhoneNumberConfirmed)
            .HasColumnName("phone_number_confirmed")
            .IsRequired();

        builder.Property(user => user.TwoFactorEnabled)
            .HasColumnName("two_factor_enabled")
            .IsRequired();

        builder.Property(user => user.LockoutEnd)
            .HasColumnName("lockout_end")
            .HasColumnType("timestamp with time zone");

        builder.Property(user => user.LockoutEnabled)
            .HasColumnName("lockout_enabled")
            .IsRequired();

        builder.Property(user => user.AccessFailedCount)
            .HasColumnName("access_failed_count")
            .IsRequired();

        builder.Property(user => user.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(user => user.NormalizedUserName)
            .HasDatabaseName("ux_auth_users_normalized_user_name")
            .IsUnique();

        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("ix_auth_users_normalized_email");
    }
}
