using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations.Identity;

internal sealed class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder)
    {
        builder.ToTable("auth_user_claims");
        builder.HasKey(claim => claim.Id).HasName("pk_auth_user_claims");

        builder.Property(claim => claim.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();
        builder.Property(claim => claim.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(claim => claim.ClaimType).HasColumnName("claim_type").HasColumnType("text");
        builder.Property(claim => claim.ClaimValue).HasColumnName("claim_value").HasColumnType("text");

        builder.HasIndex(claim => claim.UserId)
            .HasDatabaseName("ix_auth_user_claims_user_id");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(claim => claim.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_auth_user_claims_auth_users_user_id");
    }
}
