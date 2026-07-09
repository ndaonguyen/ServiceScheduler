namespace AppointmentScheduler.Api.Security;

/// <summary>
/// Writes and clears the two auth cookies. Both are <c>HttpOnly</c> (JavaScript cannot read them —
/// XSS cannot exfiltrate a token) and <c>SameSite=Strict</c> (not sent on cross-site requests — CSRF
/// mitigation without a separate token). <c>Secure</c> tracks the request scheme so the flow works
/// over plain HTTP in local dev / tests and is enforced everywhere TLS terminates.
///
/// The access cookie is scoped to <c>/</c> (sent on every API request); the refresh cookie is scoped
/// to the refresh endpoint only, so it is not attached to ordinary requests. Both are persistent
/// (carry an <c>Expires</c>) so a closed/reopened tab stays logged in.
/// </summary>
public static class AuthCookies
{
    public const string AccessCookieName = "access_token";
    public const string RefreshCookieName = "refresh_token";
    public const string RefreshCookiePath = "/api/auth/refresh";

    public static void WriteAccess(HttpResponse response, AccessToken access) =>
        response.Cookies.Append(AccessCookieName, access.Token, Options(response, "/", access.ExpiresAt));

    public static void WriteRefresh(HttpResponse response, RefreshTokenResult refresh) =>
        response.Cookies.Append(RefreshCookieName, refresh.Token, Options(response, RefreshCookiePath, refresh.ExpiresAt));

    public static void ClearAccess(HttpResponse response) =>
        response.Cookies.Delete(AccessCookieName, DeleteOptions(response, "/"));

    public static void ClearRefresh(HttpResponse response) =>
        response.Cookies.Delete(RefreshCookieName, DeleteOptions(response, RefreshCookiePath));

    private static CookieOptions Options(HttpResponse response, string path, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = response.HttpContext.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = path,
        Expires = expires,
    };

    // Delete options must match the write's Path/Secure/SameSite for the browser to drop the cookie.
    private static CookieOptions DeleteOptions(HttpResponse response, string path) => new()
    {
        HttpOnly = true,
        Secure = response.HttpContext.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = path,
    };
}
