import { describe, expect, it } from 'vitest';
import { sanitizeText, validateFeedback, validateLogin, validateRegister } from './validation';

const validRegistration = {
  email: 'a@b.com', password: 'Passw0rdOK1', confirmPassword: 'Passw0rdOK1',
  firstName: 'Asha', lastName: "O'Brien", phoneNumber: '+91 98765 43210',
};

describe('sanitizeText', () => {
  it('strips tags, control characters and extra spaces', () => {
    expect(sanitizeText('<b>Hi</b>   there\u0007')).toBe('Hi there');
  });
  it('keeps newlines only when allowed', () => {
    expect(sanitizeText('a\nb', true)).toBe('a\nb');
    expect(sanitizeText('a\nb')).toBe('a b');
  });
});

describe('validateLogin', () => {
  it('accepts valid credentials', () => expect(validateLogin({ email: 'a@b.com', password: 'x' })).toEqual({}));
  it('requires both fields and a valid email', () => {
    expect(validateLogin({ email: '', password: '' })).toEqual({ email: 'Email is required.', password: 'Password is required.' });
    expect(validateLogin({ email: 'nope', password: 'x' }).email).toMatch(/valid/);
  });
});

describe('validateRegister', () => {
  it('accepts a valid registration', () => expect(validateRegister(validRegistration)).toEqual({}));
  it.each([
    ['short1A', 'at least 10'],
    ['alllowercase123', 'uppercase'],
    ['ALLUPPERCASE123', 'lowercase'],
    ['NoDigitsHereAtAll', 'digit'],
  ])('rejects weak password %s', (password, message) => {
    expect(validateRegister({ ...validRegistration, password, confirmPassword: password }).password).toContain(message);
  });
  it('rejects mismatched confirmation, bad names and bad phone', () => {
    const errors = validateRegister({ ...validRegistration, confirmPassword: 'x', firstName: '123', lastName: '', phoneNumber: 'abc' });
    expect(Object.keys(errors).sort()).toEqual(['confirmPassword', 'firstName', 'lastName', 'phoneNumber']);
  });
});

describe('validateFeedback', () => {
  const valid = { storeId: 1, overallRating: 4, comments: 'ok', visitDate: '', items: [] };
  it('accepts valid feedback', () => expect(validateFeedback(valid)).toEqual({}));
  it('requires store and rating', () => {
    expect(validateFeedback({ ...valid, storeId: null })).toHaveProperty('storeId');
    expect(validateFeedback({ ...valid, overallRating: 0 })).toHaveProperty('overallRating');
  });
  it('limits lengths and rejects future dates', () => {
    expect(validateFeedback({ ...valid, comments: 'x'.repeat(2001) })).toHaveProperty('comments');
    expect(validateFeedback({ ...valid, visitDate: '2999-01-01' })).toHaveProperty('visitDate');
    expect(validateFeedback({ ...valid, items: [{ rating: 3, comment: 'x'.repeat(1001) }] })).toHaveProperty('items');
  });
});
