// Client-side validation mirrors the server rules (the server remains the source of truth and re-validates everything).

export type Errors = Record<string, string>;

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const NAME = /^[\p{L}\p{M}][\p{L}\p{M}' .-]*$/u;
const PHONE = /^\+?[0-9 ()-]{7,20}$/;

/** Removes markup and control characters from free text before it is sent. */
export function sanitizeText(value: string, allowNewLines = false): string {
  const withoutMarkup = value.replace(/<[^>]*>/g, '').replace(/[<>]/g, '');
  const controls = allowNewLines ? /[\u0000-\u0009\u000b-\u001f\u007f]/g : /[\u0000-\u001f\u007f]/g;
  return withoutMarkup.replace(controls, ' ').replace(/[ \t]{2,}/g, ' ').trim();
}

export function validateLogin(values: { email: string; password: string }): Errors {
  const errors: Errors = {};
  if (!values.email.trim()) errors.email = 'Email is required.';
  else if (!EMAIL.test(values.email.trim())) errors.email = 'Enter a valid email address.';
  if (!values.password) errors.password = 'Password is required.';
  return errors;
}

export function validateRegister(values: {
  email: string; password: string; confirmPassword: string; firstName: string; lastName: string; phoneNumber: string;
}): Errors {
  const errors = validateLogin(values);
  const password = values.password;
  if (password) {
    if (password.length < 10) errors.password = 'Password must be at least 10 characters.';
    else if (!/[a-z]/.test(password)) errors.password = 'Password must contain a lowercase letter.';
    else if (!/[A-Z]/.test(password)) errors.password = 'Password must contain an uppercase letter.';
    else if (!/[0-9]/.test(password)) errors.password = 'Password must contain a digit.';
  }
  if (values.confirmPassword !== password) errors.confirmPassword = 'Passwords do not match.';
  for (const [field, label] of [['firstName', 'First name'], ['lastName', 'Last name']] as const) {
    const value = sanitizeText(values[field]);
    if (!value) errors[field] = `${label} is required.`;
    else if (value.length > 100) errors[field] = `${label} must be 100 characters or fewer.`;
    else if (!NAME.test(value)) errors[field] = `${label} contains invalid characters.`;
  }
  const phone = values.phoneNumber.trim();
  if (phone && !PHONE.test(phone)) errors.phoneNumber = 'Use 7-20 digits; + ( ) - and spaces are allowed.';
  return errors;
}

export function validateFeedback(values: {
  storeId: number | null; overallRating: number; comments: string; visitDate: string; items: { rating: number; comment: string }[];
}): Errors {
  const errors: Errors = {};
  if (!values.storeId) errors.storeId = 'Choose a store.';
  if (values.overallRating < 1 || values.overallRating > 5) errors.overallRating = 'Select an overall rating from 1 to 5.';
  if (values.comments.length > 2000) errors.comments = 'Comments must be 2000 characters or fewer.';
  if (values.visitDate && new Date(values.visitDate) > new Date(Date.now() + 24 * 3600 * 1000)) {
    errors.visitDate = 'Visit date cannot be in the future.';
  }
  if (values.items.some((i) => i.comment.length > 1000)) errors.items = 'Category comments must be 1000 characters or fewer.';
  return errors;
}
