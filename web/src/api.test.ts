import { describe, expect, it, vi } from 'vitest';

import { createLink, getStats, toApiError, unavailableMessage } from './api';

function problem(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/problem+json', ...headers },
  });
}

describe('toApiError', () => {
  it('keeps field errors from a 400', async () => {
    const error = await toApiError(problem(400, { title: 'One or more validation errors occurred.', errors: { url: ['The URL must be an absolute http or https URL.'] } }));

    expect(error.status).toBe(400);
    expect(error.fieldErrors).toEqual({ url: ['The URL must be an absolute http or https URL.'] });
    expect(error.message).toBe('Please fix the highlighted fields.');
  });

  it('uses the detail of a 409', async () => {
    const error = await toApiError(problem(409, { title: 'Alias already in use', detail: "The alias 'promo' is already taken." }));

    expect(error.message).toBe("The alias 'promo' is already taken.");
  });

  it('explains a 404 in plain words', async () => {
    expect((await toApiError(problem(404, { title: 'Not Found' }))).message).toBe('No link with this code.');
  });

  it('uses the detail of a 410', async () => {
    expect((await toApiError(problem(410, { title: 'Link expired', detail: 'This short link has expired.' }))).message).toBe('This short link has expired.');
  });

  it.each([
    ['42', 'Too many requests. Try again in 42 seconds.'],
    ['1', 'Too many requests. Try again in 1 second.'],
    ['', 'Too many requests. Try again in a moment.'],
  ])('states the wait for a 429 with Retry-After "%s"', async (retryAfter, expected) => {
    const error = await toApiError(problem(429, { title: 'Too many requests' }, retryAfter ? { 'Retry-After': retryAfter } : {}));

    expect(error.message).toBe(expected);
  });

  it('never shows a raw body for a server error or a non-JSON response', async () => {
    expect((await toApiError(new Response('<html>Bad gateway</html>', { status: 502 }))).message).toBe('Something went wrong on the server. Please try again.');
    expect((await toApiError(new Response('oops', { status: 418 }))).message).toBe('The request failed (418).');
  });
});

describe('requests', () => {
  it('posts the create body and returns the link', async () => {
    const link = { code: 'Abc123x', shortUrl: 'http://localhost:5058/Abc123x', originalUrl: 'https://example.com/', createdAtUtc: '2026-10-11T07:00:00Z', expiresAtUtc: null, clickCount: 0 };
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(link), { status: 201 }));

    const result = await createLink({ url: 'https://example.com/', customAlias: 'promo' });

    expect(result).toEqual({ ok: true, value: link });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/links');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ url: 'https://example.com/', customAlias: 'promo' });
  });

  it('encodes the code in the path', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{}', { status: 200 }));

    await getStats('a/b');

    expect(fetchMock.mock.calls[0][0]).toBe('/api/links/a%2Fb/stats');
  });

  it('reports an unreachable service', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'));

    const result = await createLink({ url: 'https://example.com/' });

    expect(result).toEqual({ ok: false, error: { message: unavailableMessage, fieldErrors: {} } });
  });
});
