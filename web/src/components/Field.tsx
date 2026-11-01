import type { ReactNode } from 'react';

interface FieldProps {
  id: string;
  label: string;
  error?: string;
  hint?: string;
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby'?: string }) => ReactNode;
}

/** Label + control + inline error, wired together for screen readers. */
export function Field({ id, label, error, hint, children }: FieldProps) {
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined;
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {children({ id, 'aria-invalid': Boolean(error), 'aria-describedby': describedBy })}
      {error ? <p id={`${id}-error`} className="error" role="alert">{error}</p> : hint ? <p id={`${id}-hint`} className="hint">{hint}</p> : null}
    </div>
  );
}

export function ErrorBanner({ message, traceId }: { message: string | null; traceId?: string }) {
  if (!message) return null;
  return (
    <div className="banner banner-error" role="alert">
      {message}
      {traceId ? <span className="trace"> (ref {traceId})</span> : null}
    </div>
  );
}
