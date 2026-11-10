import { useEffect, useState } from 'react';
import { api, ApiError } from '../api';
import { ErrorBanner } from '../components/Field';
import { MonthlyReportModal } from '../components/MonthlyReportModal';
import { StarRating } from '../components/StarRating';
import { TrendChart } from '../components/TrendChart';
import type { RecentFeedback, StoreSummary } from '../types';

type ManagedStore = Awaited<ReturnType<typeof api.managedStores>>[number];

const TREND_TEXT: Record<StoreSummary['trendDirection'], { label: string; className: string; arrow: string }> = {
  improving: { label: 'Improving', className: 'good', arrow: '▲' },
  declining: { label: 'Declining', className: 'bad', arrow: '▼' },
  stable: { label: 'Stable', className: 'flat', arrow: '▬' },
  insufficient_data: { label: 'Not enough data', className: 'flat', arrow: '–' },
};

export default function DashboardPage() {
  const [stores, setStores] = useState<ManagedStore[] | null>(null);
  const [storeId, setStoreId] = useState<number | null>(null);
  const [days, setDays] = useState(30);
  const [summary, setSummary] = useState<StoreSummary | null>(null);
  const [recent, setRecent] = useState<RecentFeedback[]>([]);
  const [error, setError] = useState<ApiError | null>(null);
  const [reportOpen, setReportOpen] = useState(false);

  const fail = (err: unknown) => setError(err instanceof ApiError ? err : new ApiError('Could not load the dashboard.', 0));

  useEffect(() => {
    api.managedStores().then((s) => { setStores(s); if (s.length) setStoreId(s[0].storeId); }).catch(fail);
  }, []);

  useEffect(() => {
    if (storeId === null) return;
    let cancelled = false;
    setSummary(null);
    Promise.all([api.summary(storeId, days), api.recent(storeId)])
      .then(([s, r]) => { if (!cancelled) { setSummary(s); setRecent(r); setError(null); } })
      .catch((err) => { if (!cancelled) fail(err); });
    return () => { cancelled = true; };
  }, [storeId, days]);

  if (stores?.length === 0) {
    return (
      <section className="card narrow center">
        <h1>No stores yet</h1>
        <p>You are not assigned to any store. Ask an administrator to assign you, then refresh this page.</p>
      </section>
    );
  }

  const trend = summary ? TREND_TEXT[summary.trendDirection] : null;
  const maxBucket = Math.max(1, ...(summary?.ratingDistribution.map((b) => b.count) ?? [1]));

  return (
    <div className="dashboard">
      <div className="toolbar">
        <h1>{summary?.storeName ?? 'Satisfaction overview'}</h1>
        <span className="spacer" />
        {stores && stores.length > 1 && (
          <select aria-label="Store" value={storeId ?? ''} onChange={(e) => setStoreId(Number(e.target.value))}>
            {stores.map((s) => <option key={s.storeId} value={s.storeId}>{s.storeName}</option>)}
          </select>
        )}
        <select aria-label="Time window" value={days} onChange={(e) => setDays(Number(e.target.value))}>
          {[7, 30, 90].map((d) => <option key={d} value={d}>Last {d} days</option>)}
        </select>
        <button type="button" className="btn" onClick={() => setReportOpen(true)} disabled={storeId === null}>
          Monthly feedback report
        </button>
      </div>
      {reportOpen && storeId !== null && (
        <MonthlyReportModal storeId={storeId} storeName={summary?.storeName ?? 'Store'} onClose={() => setReportOpen(false)} />
      )}

      <ErrorBanner message={error?.message ?? null} traceId={error?.traceId} />
      {!summary && !error && <p className="muted">Loading…</p>}

      {summary && trend && (
        <>
          <div className="kpis">
            <div className="kpi">
              <span className="kpi-label">Avg satisfaction</span>
              <span className="kpi-value">{summary.avgSatisfactionInWindow?.toFixed(2) ?? '–'}<small> / 5</small></span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Trend vs previous {summary.windowDays} days</span>
              <span className={`kpi-value ${trend.className}`}>
                <span aria-hidden="true">{trend.arrow}</span> {summary.trendDelta !== null ? `${summary.trendDelta > 0 ? '+' : ''}${summary.trendDelta.toFixed(2)}` : '–'}
              </span>
              <span className="muted small">{trend.label}</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Responses (window)</span>
              <span className="kpi-value">{summary.feedbackInWindow}</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Responses (all time)</span>
              <span className="kpi-value">{summary.totalFeedbackAllTime}</span>
            </div>
          </div>

          <div className="grid">
            <section className="card">
              <h2>Daily satisfaction</h2>
              <TrendChart points={summary.dailyTrend} />
            </section>
            <section className="card">
              <h2>Rating distribution</h2>
              <ul className="bars">
                {[...summary.ratingDistribution].reverse().map((b) => (
                  <li key={b.rating}>
                    <span className="bar-label">{b.rating}★</span>
                    <span className="bar-track"><span className="bar-fill" style={{ width: `${(b.count / maxBucket) * 100}%` }} /></span>
                    <span className="bar-count">{b.count}</span>
                  </li>
                ))}
              </ul>
            </section>
          </div>

          <div className="grid">
            <section className="card">
              <h2>Top categories</h2>
              {summary.topCategories.length === 0 ? <p className="muted">No categorised feedback yet.</p> : (
                <table>
                  <thead><tr><th scope="col">Category</th><th scope="col">Ratings</th><th scope="col">Average</th></tr></thead>
                  <tbody>
                    {summary.topCategories.map((c) => (
                      <tr key={c.categoryId}><td>{c.categoryName}</td><td>{c.ratingCount}</td><td>{c.avgRating.toFixed(2)}</td></tr>
                    ))}
                  </tbody>
                </table>
              )}
            </section>
            <section className="card">
              <h2>Recent feedback</h2>
              {recent.length === 0 ? <p className="muted">Nothing yet.</p> : (
                <ul className="list compact">
                  {recent.map((r) => (
                    <li key={r.submissionId}>
                      <div className="list-head">
                        <StarRating name={`r-${r.submissionId}`} label="Rating" value={r.overallRating} readOnly />
                        <span className="muted small">{r.isAnonymous ? 'Anonymous' : 'Customer'}</span>
                      </div>
                      {r.comments && <p className="comment">{r.comments}</p>}
                      {r.categories.length > 0 && <div className="chips">{r.categories.map((c) => <span className="chip" key={c}>{c}</span>)}</div>}
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>
        </>
      )}
    </div>
  );
}
