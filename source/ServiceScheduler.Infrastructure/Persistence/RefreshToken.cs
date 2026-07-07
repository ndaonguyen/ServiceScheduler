namespace ServiceScheduler.Infrastructure.Persistence;

/// <summary>
/// A persisted refresh token. Only the SHA-256 <see cref="TokenHash"/> is stored (never the raw
/// value). Tokens are single-use: refreshing revokes the current token and issues a replacement
/// (rotation), so <see cref="ReplacedByTokenHash"/> links the chain and lets reuse be detected.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public string TokenHash { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
