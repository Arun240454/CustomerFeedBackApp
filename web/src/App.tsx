import { Navigate, NavLink, Outlet, Route, Routes } from 'react-router-dom';
import { homePathFor, useAuth } from './auth';
import type { Role } from './types';
import LoginPage from './pages/LoginPage';
import RegisterPage from './pages/RegisterPage';
import SubmitFeedbackPage from './pages/SubmitFeedbackPage';
import MyFeedbackPage from './pages/MyFeedbackPage';
import DashboardPage from './pages/DashboardPage';

/** Client-side route guard. It is a convenience only: every API call is re-authorised by the server. */
function RequireRole({ role }: { role: Role }) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  if (user.role !== role) return <Navigate to={homePathFor(user.role)} replace />;
  return <Outlet />;
}

function Shell() {
  const { user, logout } = useAuth();
  return (
    <>
      <header className="topbar">
        <span className="brand">Customer Feedback</span>
        {user && (
          <nav aria-label="Main">
            {user.role === 'Customer' && (
              <>
                <NavLink to="/feedback/new">Give feedback</NavLink>
                <NavLink to="/feedback/mine">My feedback</NavLink>
              </>
            )}
            {user.role === 'StoreManager' && <NavLink to="/dashboard">Dashboard</NavLink>}
          </nav>
        )}
        <span className="spacer" />
        {user && (
          <>
            <span className="muted">{user.firstName} · {user.role === 'StoreManager' ? 'Store manager' : 'Customer'}</span>
            <button className="btn btn-ghost" onClick={logout}>Sign out</button>
          </>
        )}
      </header>
      <main>
        <Outlet />
      </main>
    </>
  );
}

export default function App() {
  const { user } = useAuth();
  return (
    <Routes>
      <Route element={<Shell />}>
        <Route path="/login" element={user ? <Navigate to={homePathFor(user.role)} replace /> : <LoginPage />} />
        <Route path="/register" element={user ? <Navigate to={homePathFor(user.role)} replace /> : <RegisterPage />} />
        <Route element={<RequireRole role="Customer" />}>
          <Route path="/feedback/new" element={<SubmitFeedbackPage />} />
          <Route path="/feedback/mine" element={<MyFeedbackPage />} />
        </Route>
        <Route element={<RequireRole role="StoreManager" />}>
          <Route path="/dashboard" element={<DashboardPage />} />
        </Route>
        <Route path="*" element={<Navigate to={user ? homePathFor(user.role) : '/login'} replace />} />
      </Route>
    </Routes>
  );
}
