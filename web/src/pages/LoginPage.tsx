import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ApiError } from '../api';
import { homePathFor, useAuth } from '../auth';
import { ErrorBanner, Field } from '../components/Field';
import { validateLogin, type Errors } from '../validation';

export default function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [values, setValues] = useState({ email: '', password: '' });
  const [errors, setErrors] = useState<Errors>({});
  const [serverError, setServerError] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    const found = validateLogin(values);
    setErrors(found);
    setServerError(null);
    if (Object.keys(found).length) return;

    setBusy(true);
    try {
      const user = await login(values.email, values.password);
      navigate(homePathFor(user.role), { replace: true });
    } catch (err) {
      setServerError(err instanceof ApiError ? err : new ApiError('Unexpected error.', 0));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card narrow">
      <h1>Sign in</h1>
      <ErrorBanner message={serverError?.message ?? null} traceId={serverError?.traceId} />
      <form onSubmit={onSubmit} noValidate>
        <Field id="email" label="Email" error={errors.email}>
          {(p) => <input {...p} type="email" autoComplete="username" value={values.email} onChange={(e) => setValues({ ...values, email: e.target.value })} />}
        </Field>
        <Field id="password" label="Password" error={errors.password}>
          {(p) => <input {...p} type="password" autoComplete="current-password" value={values.password} onChange={(e) => setValues({ ...values, password: e.target.value })} />}
        </Field>
        <button className="btn" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
      </form>
      <p className="muted">New here? <Link to="/register">Create an account</Link></p>
    </section>
  );
}
