import { describe, expect, it } from 'vitest';
import { buildMonthlyReportPdf, reportFileName } from './monthlyReportPdf';
import type { MonthlyReport } from '../types';

const report: MonthlyReport = {
  storeId: 1, storeName: 'Connaught Place Store', year: 2026, month: 5, generatedAtUtc: '2026-06-15T12:00:00Z',
  totalFeedback: 4, avgRating: 4.5, previousMonthFeedback: 4, previousMonthAvgRating: 3, delta: 1.5, trendDirection: 'improving',
  ratingDistribution: [1, 2, 3, 4, 5].map((rating) => ({ rating, count: rating >= 4 ? 2 : 0 })),
  dailyTrend: [{ date: '2026-05-01', submissionCount: 2, avgRating: 5 }, { date: '2026-05-02', submissionCount: 0, avgRating: null }],
  categories: [{ categoryId: 2, categoryName: 'Staff Service', ratingCount: 3, avgRating: 4.33 }],
};

describe('monthly report pdf', () => {
  it('names the file after store and period', () => {
    expect(reportFileName(report)).toBe('Monthly-Feedback-Report-Connaught-Place-Store-2026-05.pdf');
  });

  it('produces a PDF document', () => {
    const bytes = new Uint8Array(buildMonthlyReportPdf(report).output('arraybuffer'));
    expect(new TextDecoder().decode(bytes.slice(0, 5))).toBe('%PDF-');
  });

  it('still produces a PDF for a month with no feedback', () => {
    const empty = { ...report, totalFeedback: 0, avgRating: null, delta: null, trendDirection: 'insufficient_data' as const, categories: [], dailyTrend: [] };
    expect(buildMonthlyReportPdf(empty).getNumberOfPages()).toBe(1);
  });
});
