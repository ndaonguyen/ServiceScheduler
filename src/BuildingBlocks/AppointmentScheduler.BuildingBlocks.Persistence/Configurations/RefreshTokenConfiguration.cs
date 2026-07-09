using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppointmentScheduler.Infrastructure.Persistence.Configurations;

/// <summary>Fluent mapping for <see cref="RefreshToken"/> (snake_case columns, like widgets).</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");
        builder.Property(t => t.ReplacedByTokenHash).HasColumnName("replaced_by_token_hash");

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);

        // Optimistic concurrency for single-use rotation: a uint shadow property named "xmin",
        // marked IsRowVersion(), is auto-mapped by Npgsql's EF provider to PostgreSQL's system
        // `xmin` column so two concurrent refreshes of the SAME token can't both succeed. The
        // second SaveChanges sees a changed row version and throws DbUpdateConcurrencyException,
        // which RotateAsync turns into a rejection — preventing a race from minting two parallel
        // token chains (which would silently defeat reuse detection). No schema change: `xmin`
        // already exists on every Postgres row. (The in-memory test provider ignores this.)
        builder.Property<uint>("xmin").IsRowVersion();

        // FK to the Identity user; deleting a user removes their refresh tokens.
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
