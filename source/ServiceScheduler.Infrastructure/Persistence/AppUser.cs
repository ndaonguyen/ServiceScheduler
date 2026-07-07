using Microsoft.AspNetCore.Identity;

namespace ServiceScheduler.Infrastructure.Persistence;

/// <summary>
/// The application's user. Extend with profile fields as needed (display name, etc.).
/// Identity lives in Infrastructure so the Domain stays free of framework concerns.
/// </summary>
public sealed class AppUser : IdentityUser
{
}
