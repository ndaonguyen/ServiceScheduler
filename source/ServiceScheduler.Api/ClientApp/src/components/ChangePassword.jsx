import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../AuthContext';

export function ChangePassword() {
  const { changePassword } = useAuth();
  const navigate = useNavigate();

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState(null);
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);

  async function submit(e) {
    e.preventDefault();
    setError(null);
    if (newPassword !== confirm) {
      setError('New password and confirmation do not match.');
      return;
    }
    setBusy(true);
    try {
      await changePassword(currentPassword, newPassword);
      setDone(true);
      setCurrentPassword('');
      setNewPassword('');
      setConfirm('');
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <h1>Change password</h1>

      {done && (
        <p style={{ color: 'var(--pico-ins-color)' }}>
          Password changed. Other devices have been signed out.
        </p>
      )}

      <form onSubmit={submit}>
        <label>
          Current password
          <input
            type="password"
            value={currentPassword}
            autoComplete="current-password"
            onChange={e => setCurrentPassword(e.target.value)}
            required
          />
        </label>
        <label>
          New password
          <input
            type="password"
            value={newPassword}
            autoComplete="new-password"
            onChange={e => setNewPassword(e.target.value)}
            required
          />
        </label>
        <label>
          Confirm new password
          <input
            type="password"
            value={confirm}
            autoComplete="new-password"
            onChange={e => setConfirm(e.target.value)}
            required
          />
        </label>

        {error && <p style={{ color: 'var(--pico-del-color)' }}>{error}</p>}

        <button type="submit" aria-busy={busy} disabled={busy}>Change password</button>
        {' '}
        <a href="#" onClick={e => { e.preventDefault(); navigate('/widgets'); }}>Cancel</a>
      </form>
    </div>
  );
}
