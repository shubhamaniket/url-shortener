// Typed calls to the public API (contract v0.3.0) and one place that turns any failure into a
// plain-language message (plan R4). Nothing here ever shows a raw response body.

export interface LinkResponse {
  code: string;
  shortUrl: string;
  originalUrl: string;
  createdAtUtc: string;
  expiresAtUtc: string | null;
  clickCount: number;
}

export interface LinkStats {
  code: string;
  totalClicks: number;
  dailyClicks: { date: string; clicks: number }[];
  topReferrers: { source: string; clicks: number }[];
}

export interface CreateLinkInput {
  url: string;
  customAlias?: string;
  expiresAt?: string;
}

export interface ApiError {
  /** One sentence for the user. */
  message: string;
  /** Messages per request field (`url`, `customAlias`, `expiresAt`) for 400 responses. */
  fieldErrors: Record<string, string[]>;
  status?: number;
}

export type Result<T> = { ok: true; value: T } | { ok: false; error: ApiError };

const baseUrl: string = import.meta.env.VITE_API_BASE_URL ?? '';

export const unavailableMessage = 'The service is unavailable. Check your connection and try again.';

export function createLink(input: CreateLinkInput): Promise<Result<LinkResponse>> {
  return request<LinkResponse>('/api/links', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  });
}

export function getLink(code: string): Promise<Result<LinkResponse>> {
  return request<LinkResponse>(`/api/links/${encodeURIComponent(code)}`);
}

export function getStats(code: string): Promise<Result<LinkStats>> {
  return request<LinkStats>(`/api/links/${encodeURIComponent(code)}/stats`);
}

async function request<T>(path: string, init?: RequestInit): Promise<Result<T>> {
  let response: Response;
  try {
    response = await fetch(`${baseUrl}${path}`, init);
  } catch {
    return { ok: false, error: { message: unavailableMessage, fieldErrors: {} } };
  }

  if (response.ok) {
    return { ok: true, value: (await response.json()) as T };
  }

  return { ok: false, error: await toApiError(response) };
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

export async function toApiError(response: Response): Promise<ApiError> {
  const status = response.status;

  if (status === 429) {
    const seconds = Number.parseInt(response.headers.get('Retry-After') ?? '', 10);
    const wait = Number.isFinite(seconds) && seconds > 0 ? `${seconds} second${seconds === 1 ? '' : 's'}` : 'a moment';
    return { status, fieldErrors: {}, message: `Too many requests. Try again in ${wait}.` };
  }

  let problem: ProblemDetails = {};
  try {
    problem = (await response.json()) as ProblemDetails;
  } catch {
    // Not JSON (e.g. a proxy error page): fall through to a generic message.
  }

  if (status === 400 && problem.errors && Object.keys(problem.errors).length > 0) {
    return { status, fieldErrors: problem.errors, message: 'Please fix the highlighted fields.' };
  }

  if (status === 404) {
    return { status, fieldErrors: {}, message: 'No link with this code.' };
  }

  if (status >= 500) {
    return { status, fieldErrors: {}, message: 'Something went wrong on the server. Please try again.' };
  }

  return { status, fieldErrors: {}, message: problem.detail ?? problem.title ?? `The request failed (${status}).` };
}
