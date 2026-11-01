import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api, configureApi } from './api';
import type { Role, User } from './types';

interface Session { token: string; expiresAtUtc: string; user: User }

interface AuthContextValue {
  user: User | null;
  login: (email: string, password: string) => Promise<User>;
  logout: () => void;
}

const STORAGE_KEY = 'cf.session';
const AuthContext = createContext<AuthContextValue | null>(null);

// sessionStorage (not localStorage): the token disappears when the tab closes.
function loadSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const session = JSON.parse(raw) as Session;
    return new Date(session.expiresAtUtc).getTime() > Date.now() ? session : null;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(loadSession);

  const logout = useCallback(() => {
    sessionStorage.removeItem(STORAGE_KEY);
    setSession(null);
  }, []);

  useEffect(() => {
    configureApi({ getToken: () => session?.token ?? null, onUnauthorized: logout });
  }, [session, logout]);

  const login = useCallback(async (email: string, password: string) => {
    const response = await api.login({ email: email.trim(), password });
    const next: Session = { token: response.accessToken, expiresAtUtc: response.expiresAtUtc, user: response.user };
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    configureApi({ getToken: () => next.token, onUnauthorized: logout });
    setSession(next);
    return response.user;
  }, [logout]);

  const value = useMemo(() => ({ user: session?.user ?? null, login, logout }), [session, login, logout]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider');
  return context;
}

export const homePathFor = (role: Role) => (role === 'StoreManager' ? '/dashboard' : '/feedback/new');
