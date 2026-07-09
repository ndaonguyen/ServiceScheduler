using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using AppointmentScheduler.Infrastructure.Persistence;

namespace AppointmentScheduler.Api.Security;

/// <summary>Issues signed JWT access tokens for an authenticated user.</summary>
public interface ITokenService
{
    AccessToken CreateToken(AppUser user, IEnumerable<string> roles);
}

/// <summary>A freshly minted access token plus its absolute expiry (UTC).</summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// HS256 JWT issuer. Claims use the standard <see cref="ClaimTypes"/> URIs so the default
/// token-validation name/role claim types light up <c>RequireRole</c>, <c>ICurrentUser</c>, and
/// <c>/api/profile/me</c> without extra mapping. Expiry is derived from <see cref="TimeProvider"/> so it
/// stays testable.
/// </summary>
internal sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken CreateToken(AppUser user, IEnumerable<string> roles)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (user.Email is { } email)
        {
            claims.Add(new Claim(ClaimTypes.Name, email));
            claims.Add(new Claim(ClaimTypes.Email, email));
        }
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken(token, expires);
    }
}
