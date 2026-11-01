import type { DailyPoint } from '../types';

const W = 640;
const H = 220;
const PAD = { top: 12, right: 12, bottom: 28, left: 32 };

/** Dependency-free SVG line chart of average daily rating. Days without feedback leave a gap. */
export function TrendChart({ points }: { points: DailyPoint[] }) {
  const innerW = W - PAD.left - PAD.right;
  const innerH = H - PAD.top - PAD.bottom;
  const x = (i: number) => PAD.left + (points.length <= 1 ? innerW / 2 : (i / (points.length - 1)) * innerW);
  const y = (rating: number) => PAD.top + innerH - ((rating - 1) / 4) * innerH;

  const segments: string[] = [];
  let current: string[] = [];
  points.forEach((p, i) => {
    if (p.avgRating === null) {
      if (current.length) segments.push(current.join(' '));
      current = [];
    } else {
      current.push(`${current.length ? 'L' : 'M'}${x(i).toFixed(1)},${y(p.avgRating).toFixed(1)}`);
    }
  });
  if (current.length) segments.push(current.join(' '));

  const labelIdx = [0, Math.floor((points.length - 1) / 2), points.length - 1].filter((v, i, a) => a.indexOf(v) === i);
  const hasData = points.some((p) => p.avgRating !== null);

  return (
    <figure className="chart">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="Average daily satisfaction rating over time">
        {[1, 2, 3, 4, 5].map((r) => (
          <g key={r}>
            <line x1={PAD.left} x2={W - PAD.right} y1={y(r)} y2={y(r)} className="gridline" />
            <text x={PAD.left - 8} y={y(r) + 4} textAnchor="end" className="axis">{r}</text>
          </g>
        ))}
        {segments.map((d, i) => <path key={i} d={d} className="line" />)}
        {points.map((p, i) => p.avgRating !== null && (
          <circle key={p.date} cx={x(i)} cy={y(p.avgRating)} r={3.5} className="dot">
            <title>{`${p.date}: ${p.avgRating.toFixed(2)} avg from ${p.submissionCount} response(s)`}</title>
          </circle>
        ))}
        {labelIdx.map((i) => (
          <text key={i} x={x(i)} y={H - 8} textAnchor={i === 0 ? 'start' : i === points.length - 1 ? 'end' : 'middle'} className="axis">
            {points[i]?.date.slice(5)}
          </text>
        ))}
        {!hasData && <text x={W / 2} y={H / 2} textAnchor="middle" className="axis">No feedback in this period</text>}
      </svg>
    </figure>
  );
}
