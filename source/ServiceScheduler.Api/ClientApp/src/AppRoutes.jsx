import { Home } from './components/Home';
import { Counter } from './components/Counter';
import { Widgets } from './components/Widgets';
import { Login } from './components/Login';
import { ChangePassword } from './components/ChangePassword';
import { RequireAuth } from './AuthContext';

const AppRoutes = [
  {
    index: true,
    element: <Home />,
  },
  {
    path: '/counter',
    element: <Counter />,
  },
  {
    path: '/login',
    element: <Login />,
  },
  {
    // Protected: redirects to /login when signed out.
    path: '/widgets',
    element: (
      <RequireAuth>
        <Widgets />
      </RequireAuth>
    ),
  },
  {
    // Protected: redirects to /login when signed out.
    path: '/change-password',
    element: (
      <RequireAuth>
        <ChangePassword />
      </RequireAuth>
    ),
  },
];

export default AppRoutes;
