import { createContext, useContext, useEffect, useState } from 'react';
import { Navigate } from 'react-router-dom';

const AuthContext = createContext(null);

// Tokens live in httpOnly cookies the browser attaches automatically — JavaScript never sees them.
// Every request must opt into sending cookies with `credentials: 'include'`.
const withCredentials = (options = {}) => ({ ...options, credentials: 'include' });

// Ask the server to rotate the refresh cookie for a fresh access cookie. The browser sends the
// refresh cookie (scoped to this path) automatically; nothing is read or written in JS.
// Concurrent callers share one in-flight refresh.
let refreshing = null;
async function tryRefresh() {
  refreshing ??= (async () => {
    const res = await fetch('/api/auth/refresh', withCredentials({ method: 'POST' }));
    return res.ok;
  })();
  try {
    return await refreshing;
  } finally {
    refreshing = null;
  }
}

// fetch wrapper that sends cookies and, on a 401, transparently refreshes once and retries. Use
// this for every authenticated API call.
export async function authFetch(url, options = {}, retry = true) {
  const res = await fetch(url, withCredentials(options));
  if (res.status === 401 && retry && (await tryRefresh())) {
    return authFetch(url, options, false);
  }
  return res;
}

// Turn a failed response into a useful message: ASP.NET validation errors (e.g. weak password),
// a ProblemDetails title/detail, or the HTTP status (e.g. 502 when the API/proxy is unreachable).
async function problem(res, fallback) {
  const body = await res.json().catch(() => null);
  const firstError = body?.errors && Object.values(body.errors).flat()[0];
  return firstError || body?.detail || body?.title || `${fallback} (HTTP ${res.status})`;
}

// Cookie-based JWT auth against the API. The access + refresh tokens are httpOnly cookies set by the
// server; the client holds no token. authFetch sends them and silently rotates on expiry. Identity
// comes from /api/profile/me, not the token.
export function AuthProvider({ children }) {
  const [user, setUser] = useState(null); // { userName, roles } | null
  const [loading, setLoading] = useState(true);

  async function refresh() {
    try {
      const res = await authFetch('/api/profile/me');
      setUser(res.ok ? await res.json() : null);
    } catch {
      setUser(null);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    refresh();
  }, []);

  async function login(email, password) {
    const res = await fetch('/api/auth/login', withCredentials({
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    }));
    if (!res.ok) throw new Error(await problem(res, 'Login failed — check your email and password.'));
    await refresh();
  }

  async function register(email, password) {
    const res = await fetch('/api/auth/register', withCredentials({
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    }));
    if (!res.ok) throw new Error(await problem(res, 'Registration failed.'));
  }

  async function logout() {
    // Best-effort server-side revocation; the server clears both cookies in its response.
    await fetch('/api/auth/logout', withCredentials({ method: 'POST' })).catch(() => {});
    setUser(null);
  }

  // Change the signed-in user's password. The server revokes every other session and returns a fresh
  // cookie pair for this device, so the caller stays logged in while other devices are signed out.
  async function changePassword(currentPassword, newPassword) {
    const res = await authFetch('/api/auth/change-password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ currentPassword, newPassword }),
    });
    if (!res.ok) throw new Error(await problem(res, 'Could not change password.'));
  }

  const value = {
    user,
    loading,
    isAuthenticated: !!user,
    isAdmin: !!user?.roles?.includes('admin'),
    login,
    register,
    logout,
    changePassword,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export const useAuth = () => useContext(AuthContext);

/// Route guard: redirect to /login when not signed in.
export function RequireAuth({ children }) {
  const { isAuthenticated, loading } = useAuth();
  if (loading) return <p aria-busy="true">Loading…</p>;
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  return children;
}
