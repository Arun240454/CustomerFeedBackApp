import { jsPDF } from 'jspdf';
import autoTable from 'jspdf-autotable';
import type { MonthlyReport } from '../types';
import { MONTH_NAMES } from './months';

const ACCENT: [number, number, number] = [37, 87, 214];
const MUTED: [number, number, number] = [93, 104, 121];

const TREND_LABEL: Record<MonthlyReport['trendDirection'], string> = {
  improving: 'Improving',
  declining: 'Declining',
  stable: 'Stable',
  insufficient_data: 'Not enough data to compare',
};

const fmt = (value: number | null, digits = 2) => (value === null ? '-' : value.toFixed(digits));
const signed = (value: number | null) => (value === null ? '-' : `${value > 0 ? '+' : ''}${value.toFixed(2)}`);

export function reportFileName(report: Pick<MonthlyReport, 'storeName' | 'year' | 'month'>): string {
  const store = report.storeName.replace(/[^A-Za-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'store';
  return `Monthly-Feedback-Report-${store}-${report.year}-${String(report.month).padStart(2, '0')}.pdf`;
}

/** Builds the monthly report as a PDF document (A4). Uses only built-in fonts, so no assets are downloaded. */
export function buildMonthlyReportPdf(report: MonthlyReport): jsPDF {
  const doc = new jsPDF({ unit: 'pt', format: 'a4' });
  const pageWidth = doc.internal.pageSize.getWidth();
  const margin = 40;
  const period = `${MONTH_NAMES[report.month - 1]} ${report.year}`;
  const lastY = () => (doc as jsPDF & { lastAutoTable?: { finalY: number } }).lastAutoTable?.finalY ?? 0;

  doc.setFont('helvetica', 'bold').setFontSize(20).setTextColor(...ACCENT);
  doc.text('Monthly Feedback Report', margin, 56);
  doc.setFont('helvetica', 'normal').setFontSize(12).setTextColor(0);
  doc.text(report.storeName, margin, 78);
  doc.setFontSize(10).setTextColor(...MUTED);
  doc.text(`Period: ${period}`, margin, 94);
  doc.text(`Generated: ${new Date(report.generatedAtUtc).toLocaleString()}`, pageWidth - margin, 94, { align: 'right' });
  doc.setDrawColor(...ACCENT).setLineWidth(1).line(margin, 102, pageWidth - margin, 102);

  const section = (title: string, y: number) => {
    doc.setFont('helvetica', 'bold').setFontSize(12).setTextColor(0);
    doc.text(title, margin, y);
    return y + 8;
  };
  const baseStyles = { headStyles: { fillColor: ACCENT }, styles: { fontSize: 10 }, margin: { left: margin, right: margin } };

  autoTable(doc, {
    ...baseStyles,
    startY: section('Summary', 126),
    head: [['Measure', 'Value']],
    body: [
      ['Responses this month', String(report.totalFeedback)],
      ['Average rating (out of 5)', fmt(report.avgRating)],
      ['Responses previous month', String(report.previousMonthFeedback)],
      ['Average rating previous month', fmt(report.previousMonthAvgRating)],
      ['Change vs previous month', `${signed(report.delta)}  (${TREND_LABEL[report.trendDirection]})`],
    ],
    columnStyles: { 0: { cellWidth: 220 } },
  });

  if (report.totalFeedback === 0) {
    doc.setFont('helvetica', 'italic').setFontSize(11).setTextColor(...MUTED);
    doc.text('No feedback was received in this month.', margin, lastY() + 28);
  } else {
    const buckets = [...report.ratingDistribution].reverse();
    const max = Math.max(1, ...buckets.map((b) => b.count));
    autoTable(doc, {
      ...baseStyles,
      startY: section('Rating distribution', lastY() + 28),
      head: [['Rating', 'Responses', 'Share', '']],
      body: buckets.map((b) => [
        `${b.rating} star${b.rating === 1 ? '' : 's'}`,
        String(b.count),
        `${((b.count / report.totalFeedback) * 100).toFixed(0)}%`,
        '',
      ]),
      columnStyles: { 0: { cellWidth: 70 }, 1: { cellWidth: 80 }, 2: { cellWidth: 60 } },
      didDrawCell: (data) => {
        if (data.section !== 'body' || data.column.index !== 3) return;
        const width = Math.max(0, (data.cell.width - 8) * (buckets[data.row.index].count / max));
        doc.setFillColor(...ACCENT).rect(data.cell.x + 4, data.cell.y + 4, width, data.cell.height - 8, 'F');
      },
    });

    if (report.categories.length > 0) {
      autoTable(doc, {
        ...baseStyles,
        startY: section('Feedback by category', lastY() + 28),
        head: [['Category', 'Ratings', 'Average']],
        body: report.categories.map((c) => [c.categoryName, String(c.ratingCount), c.avgRating.toFixed(2)]),
      });
    }

    const days = report.dailyTrend.filter((d) => d.submissionCount > 0);
    autoTable(doc, {
      ...baseStyles,
      startY: section('Daily breakdown (days with responses)', lastY() + 28),
      head: [['Date', 'Responses', 'Average rating']],
      body: days.map((d) => [d.date, String(d.submissionCount), fmt(d.avgRating)]),
    });
  }

  const pages = doc.getNumberOfPages();
  for (let page = 1; page <= pages; page++) {
    doc.setPage(page);
    doc.setFont('helvetica', 'normal').setFontSize(9).setTextColor(...MUTED);
    doc.text(`${report.storeName} - ${period}`, margin, doc.internal.pageSize.getHeight() - 24);
    doc.text(`Page ${page} of ${pages}`, pageWidth - margin, doc.internal.pageSize.getHeight() - 24, { align: 'right' });
  }
  return doc;
}

export function downloadMonthlyReportPdf(report: MonthlyReport): void {
  buildMonthlyReportPdf(report).save(reportFileName(report));
}
