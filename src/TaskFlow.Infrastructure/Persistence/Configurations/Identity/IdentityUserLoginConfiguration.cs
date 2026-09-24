using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations.Identity;

internal sealed class IdentityUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
    {
        builder.ToTable("auth_user_logins");

        builder.HasKey(login => new { login.LoginProvider, login.ProviderKey })
            .HasName("pk_auth_user_logins");

        builder.Property(login => login.LoginProvider)
            .HasColumnName("login_provider")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(login => login.ProviderKey)
            .HasColumnName("provider_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(login => login.ProviderDisplayName)
            .HasColumnName("provider_display_name")
            .HasColumnType("text");

        builder.Property(login => login.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.HasIndex(login => login.UserId)
            .HasDatabaseName("ix_auth_user_logins_user_id");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(login => login.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_auth_user_logins_auth_users_user_id");
    }
}
