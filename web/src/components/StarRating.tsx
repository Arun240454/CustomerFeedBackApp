interface StarRatingProps {
  name: string;
  value: number;
  onChange?: (value: number) => void;
  label: string;
  readOnly?: boolean;
}

const LABELS = ['', 'Very dissatisfied', 'Dissatisfied', 'Neutral', 'Satisfied', 'Very satisfied'];

export function StarRating({ name, value, onChange, label, readOnly }: StarRatingProps) {
  if (readOnly) {
    return (
      <span className="stars" role="img" aria-label={`${label}: ${value} out of 5`}>
        {[1, 2, 3, 4, 5].map((n) => <span key={n} className={n <= value ? 'star on' : 'star'}>★</span>)}
      </span>
    );
  }
  return (
    <fieldset className="stars-input">
      <legend className="sr-only">{label}</legend>
      {[1, 2, 3, 4, 5].map((n) => (
        <label key={n} className={n <= value ? 'star on' : 'star'} title={LABELS[n]}>
          <input type="radio" name={name} value={n} checked={value === n} onChange={() => onChange?.(n)} />
          <span aria-hidden="true">★</span>
          <span className="sr-only">{n} - {LABELS[n]}</span>
        </label>
      ))}
      <span className="muted rating-label">{value ? LABELS[value] : 'Select a rating'}</span>
    </fieldset>
  );
}
