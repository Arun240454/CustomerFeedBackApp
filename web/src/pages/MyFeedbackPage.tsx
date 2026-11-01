import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api';
import { ErrorBanner } from '../components/Field';
import { StarRating } from '../components/StarRating';
import type { MyFeedback, Paged } from '../types';

export default function MyFeedbackPage() {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Paged<MyFeedback> | null>(null);
  const [error, setError] = useState<ApiError | null>(null);

  useEffect(() => {
    let cancelled = false;
    api.myFeedback(page)
      .then((result) => { if (!cancelled) { setData(result); setError(null); } })
      .catch((err) => { if (!cancelled) setError(err instanceof ApiError ? err : new ApiError('Could not load feedback.', 0)); });
    return () => { cancelled = true; };
  }, [page]);

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <section className="card">
      <h1>My feedback</h1>
      <ErrorBanner message={error?.message ?? null} traceId={error?.traceId} />
      {!data && !error && <p className="muted">Loading…</p>}
      {data?.items.length === 0 && <p>You have not submitted any feedback yet. <Link to="/feedback/new">Share your first experience</Link>.</p>}
      <ul className="list">
        {data?.items.map((f) => (
          <li key={f.submissionId}>
            <div className="list-head">
              <strong>{f.storeName}</strong>
              <StarRating name={`f-${f.submissionId}`} label="Overall rating" value={f.overallRating} readOnly />
              <time className="muted small" dateTime={f.submittedAtUtc}>{new Date(f.submittedAtUtc + (f.submittedAtUtc.endsWith('Z') ? '' : 'Z')).toLocaleString()}</time>
            </div>
            {f.comments && <p className="comment">{f.comments}</p>}
            {f.items.length > 0 && (
              <div className="chips">
                {f.items.map((i) => <span className="chip" key={i.categoryName}>{i.categoryName}: {i.rating}/5</span>)}
              </div>
            )}
          </li>
        ))}
      </ul>
      {data && totalPages > 1 && (
        <div className="pager">
          <button className="btn btn-ghost" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
          <span className="muted">Page {page} of {totalPages}</span>
          <button className="btn btn-ghost" disabled={page >= totalPages} onClick={() => setPage(page + 1)}>Next</button>
        </div>
      )}
    </section>
  );
}
