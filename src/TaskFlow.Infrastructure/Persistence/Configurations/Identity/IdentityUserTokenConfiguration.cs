using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations.Identity;

internal sealed class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
    {
        builder.ToTable("auth_user_tokens");

        builder.HasKey(token => new { token.UserId, token.LoginProvider, token.Name })
            .HasName("pk_auth_user_tokens");

        builder.Property(token => token.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(token => token.LoginProvider)
            .HasColumnName("login_provider")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(token => token.Name)
            .HasColumnName("name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(token => token.Value)
            .HasColumnName("value")
            .HasColumnType("text");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_auth_user_tokens_auth_users_user_id");
    }
}
