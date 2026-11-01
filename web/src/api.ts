import type {
  AuthResponse, Category, MyFeedback, Paged, RecentFeedback, Store, StoreSummary, SubmitFeedbackInput, User,
} from './types';

/** Standardised error body (RFC 7807) returned by every service. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code?: string,
    readonly fieldErrors: Record<string, string[]> = {},
    readonly traceId?: string,
  ) {
    super(message);
  }
}

let tokenProvider: () => string | null = () => null;
let onUnauthorized: () => void = () => {};

export function configureApi(options: { getToken: () => string | null; onUnauthorized: () => void }) {
  tokenProvider = options.getToken;
  onUnauthorized = options.onUnauthorized;
}

async function request<T>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const token = tokenProvider();
  const response = await fetch(path, {
    method: init.method ?? 'GET',
    headers: {
      ...(init.body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: init.body !== undefined ? JSON.stringify(init.body) : undefined,
  }).catch(() => {
    throw new ApiError('Cannot reach the server. Please check your connection and try again.', 0);
  });

  if (response.ok) {
    return (response.status === 204 ? undefined : await response.json()) as T;
  }

  const problem = await response.json().catch(() => ({}) as Record<string, unknown>);
  if (response.status === 401 && token) {
    onUnauthorized();
  }
  const fallback = response.status === 403 ? 'You do not have access to this resource.' : 'Something went wrong.';
  throw new ApiError(
    (problem.detail as string) || (problem.title as string) || fallback,
    response.status,
    problem.code as string | undefined,
    (problem.errors as Record<string, string[]>) ?? {},
    problem.traceId as string | undefined,
  );
}

export const api = {
  register: (body: { email: string; password: string; firstName: string; lastName: string; phoneNumber?: string; role: string }) =>
    request<User>('/api/auth/register', { method: 'POST', body }),
  login: (body: { email: string; password: string }) => request<AuthResponse>('/api/auth/login', { method: 'POST', body }),
  stores: () => request<Store[]>('/api/stores'),
  categories: () => request<Category[]>('/api/categories'),
  submitFeedback: (body: SubmitFeedbackInput) =>
    request<{ submissionId: number; submittedAtUtc: string }>('/api/feedback', { method: 'POST', body }),
  myFeedback: (page: number, pageSize = 10) => request<Paged<MyFeedback>>(`/api/feedback/mine?page=${page}&pageSize=${pageSize}`),
  managedStores: () => request<{ storeId: number; storeCode: string; storeName: string; city?: string }[]>('/api/analytics/stores'),
  summary: (storeId: number, days: number) => request<StoreSummary>(`/api/analytics/stores/${storeId}/summary?days=${days}`),
  recent: (storeId: number, count = 8) => request<RecentFeedback[]>(`/api/analytics/stores/${storeId}/feedback/recent?count=${count}`),
};
