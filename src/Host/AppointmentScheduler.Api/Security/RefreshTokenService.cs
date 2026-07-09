using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AppointmentScheduler.BuildingBlocks.Persistence;

namespace AppointmentScheduler.Api.Security;

/// <summary>Issues, rotates, and revokes persisted refresh tokens.</summary>
public interface IRefreshTokenService
{
    /// <summary>Mints a new refresh token for the user and returns the raw value (shown once).</summary>
    Task<RefreshTokenResult> IssueAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Validates a raw refresh token and, if active, revokes it and issues a replacement
    /// (rotation). Returns <c>null</c> if the token is unknown, expired, or already used/revoked.
    /// </summary>
    Task<RefreshRotation?> RotateAsync(string rawToken, CancellationToken ct = default);

    /// <summary>Revokes a refresh token (logout). No-op if unknown or already revoked.</summary>
    Task RevokeAsync(string rawToken, CancellationToken ct = default);

    /// <summary>Revokes every active refresh token for a user (logout via the access cookie).</summary>
    Task RevokeAllAsync(string userId, CancellationToken ct = default);
}

public sealed record RefreshTokenResult(string Token, DateTimeOffset ExpiresAt);

public sealed record RefreshRotation(string UserId, RefreshTokenResult NewToken);

internal sealed class RefreshTokenService(AppDbContext db, IOptions<JwtOptions> options, TimeProvider clock)
    : IRefreshTokenService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<RefreshTokenResult> IssueAsync(string userId, CancellationToken ct = default)
    {
        var (raw, hash) = Generate();
        var now = clock.GetUtcNow();
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays),
        };
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return new RefreshTokenResult(raw, entity.ExpiresAt);
    }

    public async Task<RefreshRotation?> RotateAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var now = clock.GetUtcNow();
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null)
        {
            return null;
        }

        // Presenting an already-revoked/expired token signals theft or a replay → revoke the
        // user's whole active chain so a leaked token can't be milked.
        if (!existing.IsActive(now))
        {
            await RevokeAllActiveAsync(existing.UserId, now, ct);
            return null;
        }

        var (raw, newHash) = Generate();
        existing.RevokedAt = now;
        existing.ReplacedByTokenHash = newHash;

        var replacement = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = existing.UserId,
            TokenHash = newHash,
            CreatedAt = now,
            // Fixed expiration: the rotated token inherits the original token's expiry — the TTL is
            // set once at login and never reset, so an actively-refreshing session still hits a hard
            // 7-day ceiling from the original login (bounds a stolen refresh token's exploit window).
            ExpiresAt = existing.ExpiresAt,
        };
        db.RefreshTokens.Add(replacement);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another concurrent refresh already rotated this exact token (the row's xmin changed
            // between our read and write). That request won the race and issued the replacement;
            // this one must not mint a second parallel chain. This is a benign concurrent refresh,
            // NOT token reuse, so reject it without revoking the chain.
            return null;
        }

        return new RefreshRotation(existing.UserId, new RefreshTokenResult(raw, replacement.ExpiresAt));
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (existing is not null)
        {
            existing.RevokedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    public Task RevokeAllAsync(string userId, CancellationToken ct = default) =>
        RevokeAllActiveAsync(userId, clock.GetUtcNow(), ct);

    private async Task RevokeAllActiveAsync(string userId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in active)
        {
            token.RevokedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    private static (string Raw, string Hash) Generate()
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
