export type Role = 'Customer' | 'StoreManager';

export interface User {
  userId: number;
  email: string;
  firstName: string;
  lastName: string;
  role: Role;
}

export interface AuthResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: User;
}

export interface Store { storeId: number; storeCode: string; storeName: string; city?: string | null }
export interface Category { categoryId: number; categoryName: string; description?: string | null }

export interface FeedbackItemInput { categoryId: number; rating: number; comment?: string }
export interface SubmitFeedbackInput {
  storeId: number;
  overallRating: number;
  comments?: string;
  visitDate?: string;
  isAnonymous: boolean;
  items: FeedbackItemInput[];
}

export interface MyFeedback {
  submissionId: number;
  storeName: string;
  overallRating: number;
  comments?: string | null;
  visitDate?: string | null;
  submittedAtUtc: string;
  items: { categoryName: string; rating: number; comment?: string | null }[];
}

export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number }

export interface DailyPoint { date: string; submissionCount: number; avgRating: number | null }
export interface StoreSummary {
  storeId: number;
  storeName: string;
  windowDays: number;
  totalFeedbackAllTime: number;
  feedbackInWindow: number;
  avgSatisfactionInWindow: number | null;
  avgSatisfactionPreviousWindow: number | null;
  trendDelta: number | null;
  trendDirection: 'improving' | 'declining' | 'stable' | 'insufficient_data';
  ratingDistribution: { rating: number; count: number }[];
  dailyTrend: DailyPoint[];
  topCategories: { categoryId: number; categoryName: string; ratingCount: number; avgRating: number }[];
}

export interface RecentFeedback {
  submissionId: number;
  overallRating: number;
  comments?: string | null;
  submittedAtUtc: string;
  isAnonymous: boolean;
  categories: string[];
}

export interface StoreOption { storeId: number; storeCode: string; storeName: string; city?: string | null }

export interface MonthlyReport {
  storeId: number;
  storeName: string;
  year: number;
  month: number;
  generatedAtUtc: string;
  totalFeedback: number;
  avgRating: number | null;
  previousMonthFeedback: number;
  previousMonthAvgRating: number | null;
  delta: number | null;
  trendDirection: StoreSummary['trendDirection'];
  ratingDistribution: { rating: number; count: number }[];
  dailyTrend: DailyPoint[];
  categories: { categoryId: number; categoryName: string; ratingCount: number; avgRating: number }[];
}
