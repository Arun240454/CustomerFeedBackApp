import { useEffect, useRef, useState } from 'react';
import { api, ApiError } from '../api';
import { MONTH_NAMES } from '../reports/months';
import { ErrorBanner } from './Field';

interface Props {
  storeId: number;
  storeName: string;
  onClose: () => void;
}

const YEARS_BACK = 5;

/** Month/year picker in a native modal dialog. Downloads the chosen month as a PDF. */
export function MonthlyReportModal({ storeId, storeName, onClose }: Props) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const now = new Date();
  // Default to the last completed month.
  const lastMonth = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  const [year, setYear] = useState(lastMonth.getFullYear());
  const [month, setMonth] = useState(lastMonth.getMonth() + 1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const currentYear = now.getFullYear();
  const years = Array.from({ length: YEARS_BACK }, (_, i) => currentYear - i);
  const maxMonth = year === currentYear ? now.getMonth() + 1 : 12;

  useEffect(() => {
    const dialog = dialogRef.current;
    // No close() in a cleanup: StrictMode runs effect -> cleanup -> effect, and close() would fire onClose
    // and dismiss the popup immediately. Unmounting the component removes the dialog by itself.
    if (dialog && !dialog.open) dialog.showModal();
  }, []);

  function changeYear(next: number) {
    setYear(next);
    if (next === currentYear && month > now.getMonth() + 1) setMonth(now.getMonth() + 1);
  }

  async function download() {
    setBusy(true);
    setError(null);
    try {
      const report = await api.monthlyReport(storeId, year, month);
      // Loaded on demand so the PDF library is not part of the initial page load.
      const { downloadMonthlyReportPdf } = await import('../reports/monthlyReportPdf');
      downloadMonthlyReportPdf(report);
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError('Could not create the report.', 0));
    } finally {
      setBusy(false);
    }
  }

  return (
    <dialog
      ref={dialogRef}
      className="modal"
      aria-labelledby="report-title"
      onClose={onClose}
      onClick={(e) => { if (e.target === dialogRef.current) dialogRef.current?.close(); }}
    >
      <div className="modal-body">
        <div className="modal-head">
          <h2 id="report-title">Monthly feedback report</h2>
          <button type="button" className="btn btn-ghost" onClick={() => dialogRef.current?.close()} aria-label="Close">✕</button>
        </div>
        <p className="muted">{storeName}</p>
        <ErrorBanner message={error?.message ?? null} traceId={error?.traceId} />
        <div className="row">
          <div className="field">
            <label htmlFor="report-month">Month</label>
            <select id="report-month" value={month} onChange={(e) => setMonth(Number(e.target.value))}>
              {MONTH_NAMES.map((name, i) => (
                <option key={name} value={i + 1} disabled={i + 1 > maxMonth}>{name}</option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="report-year">Year</label>
            <select id="report-year" value={year} onChange={(e) => changeYear(Number(e.target.value))}>
              {years.map((y) => <option key={y} value={y}>{y}</option>)}
            </select>
          </div>
        </div>
        <div className="modal-actions">
          <button type="button" className="btn btn-ghost" onClick={() => dialogRef.current?.close()}>Cancel</button>
          <button type="button" className="btn" onClick={download} disabled={busy}>
            {busy ? 'Preparing PDF…' : 'Download PDF'}
          </button>
        </div>
      </div>
    </dialog>
  );
}
