import { useEffect, useState } from 'react';
import { useAuth, authFetch } from '../AuthContext';

export function Widgets() {
  const { isAdmin } = useAuth();
  const [widgets, setWidgets] = useState([]);
  const [name, setName] = useState('');
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(true);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const res = await authFetch('/api/widgets');
      if (!res.ok) throw new Error(`GET /api/widgets failed (${res.status})`);
      setWidgets(await res.json());
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  async function create(e) {
    e.preventDefault();
    if (!name.trim()) return;
    setError(null);
    try {
      const res = await authFetch('/api/widgets', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name }),
      });
      if (!res.ok) throw new Error(`POST /api/widgets failed (${res.status})`);
      setName('');
      await load();
    } catch (e) {
      setError(e.message);
    }
  }

  return (
    <div>
      <h1>Widgets</h1>
      <p>Create and list widgets via the <code>/api/widgets</code> API.</p>

      {isAdmin ? (
        <form onSubmit={create} role="group">
          <input
            type="text"
            value={name}
            placeholder="Widget name"
            aria-label="Widget name"
            onChange={e => setName(e.target.value)}
          />
          <button type="submit">Add</button>
        </form>
      ) : (
        <p><small>Creating widgets requires the <code>admin</code> role.</small></p>
      )}

      {error && <p style={{ color: 'var(--pico-del-color)' }}>{error}</p>}

      {loading ? (
        <p aria-busy="true">Loading…</p>
      ) : (
        <table>
          <thead>
            <tr><th>Name</th><th>Created</th><th>Id</th></tr>
          </thead>
          <tbody>
            {widgets.length === 0 ? (
              <tr><td colSpan={3}><em>No widgets yet.</em></td></tr>
            ) : (
              widgets.map(w => (
                <tr key={w.id}>
                  <td>{w.name}</td>
                  <td>{new Date(w.createdAt).toLocaleString()}</td>
                  <td><small>{w.id}</small></td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
