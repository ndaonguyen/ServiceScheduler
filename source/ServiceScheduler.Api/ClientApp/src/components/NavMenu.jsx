import { Link, useNavigate } from 'react-router-dom';
import { ThemeToggle } from './ThemeToggle';
import { useAuth } from '../AuthContext';

export function NavMenu() {
  const { isAuthenticated, user, logout } = useAuth();
  const navigate = useNavigate();

  async function signOut() {
    await logout();
    navigate('/login');
  }

  return (
    <header className="container">
      <nav>
        <ul>
          <li><Link to="/"><strong>ServiceScheduler</strong></Link></li>
        </ul>
        <ul>
          <li><Link to="/">Home</Link></li>
          <li><Link to="/counter">Counter</Link></li>
          <li><Link to="/widgets">Widgets</Link></li>
        </ul>
        <ul>
          {isAuthenticated ? (
            <>
              <li><small>{user.userName}</small></li>
              <li><Link to="/change-password">Change password</Link></li>
              <li><a href="#" onClick={e => { e.preventDefault(); signOut(); }}>Log out</a></li>
            </>
          ) : (
            <li><Link to="/login">Log in</Link></li>
          )}
          <li><ThemeToggle /></li>
        </ul>
      </nav>
    </header>
  );
}
