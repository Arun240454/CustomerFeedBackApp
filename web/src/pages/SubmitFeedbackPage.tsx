import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api';
import { ErrorBanner, Field } from '../components/Field';
import { StarRating } from '../components/StarRating';
import type { Category, Store } from '../types';
import { sanitizeText, validateFeedback, type Errors } from '../validation';

export default function SubmitFeedbackPage() {
  const [stores, setStores] = useState<Store[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [storeId, setStoreId] = useState<number | null>(null);
  const [overall, setOverall] = useState(0);
  const [comments, setComments] = useState('');
  const [visitDate, setVisitDate] = useState('');
  const [anonymous, setAnonymous] = useState(false);
  const [ratings, setRatings] = useState<Record<number, { rating: number; comment: string }>>({});
  const [errors, setErrors] = useState<Errors>({});
  const [serverError, setServerError] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  useEffect(() => {
    Promise.all([api.stores(), api.categories()])
      .then(([s, c]) => { setStores(s); setCategories(c); })
      .catch((err) => setServerError(err instanceof ApiError ? err : new ApiError('Could not load the form.', 0)));
  }, []);

  function reset() {
    setStoreId(null); setOverall(0); setComments(''); setVisitDate(''); setAnonymous(false); setRatings({}); setErrors({}); setDone(false);
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setServerError(null);
    const items = Object.entries(ratings).filter(([, v]) => v.rating > 0).map(([id, v]) => ({ categoryId: Number(id), ...v }));
    const found = validateFeedback({ storeId, overallRating: overall, comments, visitDate, items });
    setErrors(found);
    if (Object.keys(found).length) return;

    setBusy(true);
    try {
      await api.submitFeedback({
        storeId: storeId!,
        overallRating: overall,
        comments: sanitizeText(comments, true) || undefined,
        visitDate: visitDate || undefined,
        isAnonymous: anonymous,
        items: items.map((i) => ({ categoryId: i.categoryId, rating: i.rating, comment: sanitizeText(i.comment, true) || undefined })),
      });
      setDone(true);
    } catch (err) {
      const apiError = err instanceof ApiError ? err : new ApiError('Unexpected error.', 0);
      const mapped: Errors = {};
      for (const [field, messages] of Object.entries(apiError.fieldErrors)) mapped[field] = messages[0];
      setErrors(mapped);
      setServerError(apiError);
    } finally {
      setBusy(false);
    }
  }

  if (done) {
    return (
      <section className="card narrow center">
        <h1>Thank you!</h1>
        <p>Your feedback was received. It will be categorised automatically and shared with the store team.</p>
        <button className="btn" onClick={reset}>Send more feedback</button>
        <p><Link to="/feedback/mine">View my feedback</Link></p>
      </section>
    );
  }

  return (
    <section className="card">
      <h1>Share your experience</h1>
      <ErrorBanner message={serverError?.message ?? null} traceId={serverError?.traceId} />
      <form onSubmit={onSubmit} noValidate>
        <div className="row">
          <Field id="store" label="Store" error={errors.storeId}>
            {(p) => (
              <select {...p} value={storeId ?? ''} onChange={(e) => setStoreId(e.target.value ? Number(e.target.value) : null)}>
                <option value="">Select a store…</option>
                {stores.map((s) => <option key={s.storeId} value={s.storeId}>{s.storeName}{s.city ? ` – ${s.city}` : ''}</option>)}
              </select>
            )}
          </Field>
          <Field id="visitDate" label="Visit date (optional)" error={errors.visitDate}>
            {(p) => <input {...p} type="date" value={visitDate} max={new Date().toISOString().slice(0, 10)} onChange={(e) => setVisitDate(e.target.value)} />}
          </Field>
        </div>

        <div className="field">
          <span className="label">Overall satisfaction</span>
          <StarRating name="overall" label="Overall satisfaction" value={overall} onChange={setOverall} />
          {errors.overallRating && <p className="error" role="alert">{errors.overallRating}</p>}
        </div>

        <Field id="comments" label="Comments (optional)" error={errors.comments} hint={`${comments.length}/2000`}>
          {(p) => <textarea {...p} rows={4} maxLength={2000} value={comments} onChange={(e) => setComments(e.target.value)} placeholder="What went well? What could be better?" />}
        </Field>

        <fieldset className="categories">
          <legend>Rate specific areas (optional)</legend>
          {categories.map((c) => {
            const entry = ratings[c.categoryId] ?? { rating: 0, comment: '' };
            return (
              <div key={c.categoryId} className="category-row">
                <div>
                  <strong>{c.categoryName}</strong>
                  {c.description && <div className="muted small">{c.description}</div>}
                </div>
                <StarRating name={`cat-${c.categoryId}`} label={c.categoryName} value={entry.rating}
                  onChange={(rating) => setRatings({ ...ratings, [c.categoryId]: { ...entry, rating } })} />
              </div>
            );
          })}
          {errors.items && <p className="error" role="alert">{errors.items}</p>}
        </fieldset>

        <label className="checkbox">
          <input type="checkbox" checked={anonymous} onChange={(e) => setAnonymous(e.target.checked)} />
          Submit anonymously (your name will not be linked to this feedback)
        </label>

        <button className="btn" disabled={busy}>{busy ? 'Sending…' : 'Submit feedback'}</button>
      </form>
    </section>
  );
}
