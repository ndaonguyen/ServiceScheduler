import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../AuthContext';

export function Login() {
  const { login, register } = useAuth();
  const navigate = useNavigate();

  const [mode, setMode] = useState('login'); // 'login' | 'register'
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  async function submit(e) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (mode === 'register') {
        await register(email, password);
      }
      await login(email, password);
      navigate('/widgets');
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <h1>{mode === 'login' ? 'Log in' : 'Create account'}</h1>

      <form onSubmit={submit}>
        <label>
          Email
          <input
            type="email"
            value={email}
            autoComplete="username"
            onChange={e => setEmail(e.target.value)}
            required
          />
        </label>
        <label>
          Password
          <input
            type="password"
            value={password}
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
            onChange={e => setPassword(e.target.value)}
            required
          />
        </label>

        {error && <p style={{ color: 'var(--pico-del-color)' }}>{error}</p>}

        <button type="submit" aria-busy={busy} disabled={busy}>
          {mode === 'login' ? 'Log in' : 'Register & log in'}
        </button>
      </form>

      <p>
        {mode === 'login' ? (
          <>No account? <a href="#" onClick={e => { e.preventDefault(); setMode('register'); setError(null); }}>Register</a></>
        ) : (
          <>Have an account? <a href="#" onClick={e => { e.preventDefault(); setMode('login'); setError(null); }}>Log in</a></>
        )}
      </p>
    </div>
  );
}
