import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api';
import { homePathFor, useAuth } from '../auth';
import { ErrorBanner, Field } from '../components/Field';
import { sanitizeText, validateRegister, type Errors } from '../validation';

const initial = {
  email: '', password: '', confirmPassword: '', firstName: '', lastName: '', phoneNumber: '', role: 'Customer',
};

export default function RegisterPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [values, setValues] = useState(initial);
  const [errors, setErrors] = useState<Errors>({});
  const [serverError, setServerError] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);

  const set = (key: keyof typeof initial) => (e: { target: { value: string } }) => setValues({ ...values, [key]: e.target.value });

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setServerError(null);
    const found = validateRegister(values);
    setErrors(found);
    if (Object.keys(found).length) return;

    setBusy(true);
    try {
      await api.register({
        email: values.email.trim(),
        password: values.password,
        firstName: sanitizeText(values.firstName),
        lastName: sanitizeText(values.lastName),
        phoneNumber: values.phoneNumber.trim() || undefined,
        role: values.role,
      });
      const user = await login(values.email, values.password);
      navigate(homePathFor(user.role), { replace: true });
    } catch (err) {
      const apiError = err instanceof ApiError ? err : new ApiError('Unexpected error.', 0);
      // Map server-side field errors (camelCase keys) back onto the form.
      const mapped: Errors = {};
      for (const [field, messages] of Object.entries(apiError.fieldErrors)) mapped[field] = messages[0];
      setErrors(mapped);
      setServerError(apiError);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card narrow">
      <h1>Create your account</h1>
      <ErrorBanner message={serverError?.message ?? null} traceId={serverError?.traceId} />
      <form onSubmit={onSubmit} noValidate>
        <div className="row">
          <Field id="firstName" label="First name" error={errors.firstName}>
            {(p) => <input {...p} autoComplete="given-name" value={values.firstName} onChange={set('firstName')} maxLength={100} />}
          </Field>
          <Field id="lastName" label="Last name" error={errors.lastName}>
            {(p) => <input {...p} autoComplete="family-name" value={values.lastName} onChange={set('lastName')} maxLength={100} />}
          </Field>
        </div>
        <Field id="email" label="Email" error={errors.email}>
          {(p) => <input {...p} type="email" autoComplete="email" value={values.email} onChange={set('email')} maxLength={256} />}
        </Field>
        <Field id="phoneNumber" label="Phone (optional)" error={errors.phoneNumber}>
          {(p) => <input {...p} type="tel" autoComplete="tel" value={values.phoneNumber} onChange={set('phoneNumber')} maxLength={20} />}
        </Field>
        <Field id="password" label="Password" error={errors.password} hint="At least 10 characters with upper case, lower case and a digit.">
          {(p) => <input {...p} type="password" autoComplete="new-password" value={values.password} onChange={set('password')} maxLength={128} />}
        </Field>
        <Field id="confirmPassword" label="Confirm password" error={errors.confirmPassword}>
          {(p) => <input {...p} type="password" autoComplete="new-password" value={values.confirmPassword} onChange={set('confirmPassword')} maxLength={128} />}
        </Field>
        <Field id="role" label="I am a" error={errors.role}>
          {(p) => (
            <select {...p} value={values.role} onChange={set('role')}>
              <option value="Customer">Customer</option>
              <option value="StoreManager">Store manager</option>
            </select>
          )}
        </Field>
        {values.role === 'StoreManager' && (
          <p className="hint">Store managers see store data only after an administrator assigns them to a store.</p>
        )}
        <button className="btn" disabled={busy}>{busy ? 'Creating account…' : 'Create account'}</button>
      </form>
      <p className="muted">Already registered? <Link to="/login">Sign in</Link></p>
    </section>
  );
}
