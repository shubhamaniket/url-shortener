import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import axe from 'axe-core';
import { describe, expect, it, vi } from 'vitest';

import { App } from './App';
import { LinkLookup } from './LinkLookup';

const link = {
  code: 'Abc123x',
  shortUrl: 'http://localhost:5058/Abc123x',
  originalUrl: 'https://example.com/some/long/path',
  createdAtUtc: '2026-10-01T07:00:00Z',
  expiresAtUtc: null as string | null,
  clickCount: 5,
};

function days(today: number) {
  return Array.from({ length: 30 }, (_, i) => ({ date: `2026-10-${String(i + 1).padStart(2, '0')}`, clicks: i === 29 ? today : 0 }));
}

const stats = {
  code: 'Abc123x',
  totalClicks: 5,
  dailyClicks: days(5),
  topReferrers: [{ source: 'news.example', clicks: 3 }, { source: '(direct)', clicks: 2 }],
};

/** Routes fetch calls by path; anything else is a 404. */
function api(routes: Record<string, () => Response>) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const path = String(input);
    return (routes[path] ?? (() => new Response(JSON.stringify({ title: 'Not Found' }), { status: 404 })))();
  });
}

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) => () =>
  new Response(JSON.stringify(body), { status, headers });

async function lookUp(value: string) {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText('Code or short URL'), value);
  await user.click(screen.getByRole('button', { name: 'Look up' }));
}

describe('LinkLookup', () => {
  it('shows details and statistics', async () => {
    api({ '/api/links/Abc123x': json(link), '/api/links/Abc123x/stats': json(stats) });
    render(<LinkLookup />);

    await lookUp('Abc123x');

    expect(await screen.findByText('https://example.com/some/long/path')).toBeTruthy();
    expect(screen.getByText('Never')).toBeTruthy();
    const table = screen.getByRole('table');
    expect(within(table).getAllByRole('row')).toHaveLength(31);
    expect(within(table).getByRole('rowheader', { name: '2026-10-30' }).parentElement?.textContent).toContain('5');
    const referrers = screen.getAllByRole('listitem').map((item) => item.textContent);
    expect(referrers).toEqual(['news.example: 3 clicks', '(direct): 2 clicks']);
  });

  it('accepts a pasted short URL', async () => {
    const fetchMock = api({ '/api/links/Abc123x': json(link), '/api/links/Abc123x/stats': json(stats) });
    render(<LinkLookup />);

    await lookUp('  http://localhost:5058/Abc123x/ ');

    await screen.findByText('https://example.com/some/long/path');
    expect(fetchMock.mock.calls.map((call) => String(call[0]))).toEqual(['/api/links/Abc123x', '/api/links/Abc123x/stats']);
  });

  it('marks an expired link and still shows its statistics', async () => {
    api({ '/api/links/Abc123x': json({ ...link, expiresAtUtc: '2020-01-01T00:00:00Z' }), '/api/links/Abc123x/stats': json({ ...stats, topReferrers: [] }) });
    render(<LinkLookup />);

    await lookUp('Abc123x');

    expect(await screen.findByText('Expired')).toBeTruthy();
    expect(screen.getByText('No referrers yet.')).toBeTruthy();
  });

  it('explains an unknown code', async () => {
    api({});
    render(<LinkLookup />);

    await lookUp('zzzzzzz');

    expect((await screen.findByRole('alert')).textContent).toBe('No link with this code.');
  });

  it('states the wait when rate limited', async () => {
    api({
      '/api/links/Abc123x': json({ title: 'Too many requests' }, 429, { 'Retry-After': '17' }),
      '/api/links/Abc123x/stats': json({ title: 'Too many requests' }, 429, { 'Retry-After': '17' }),
    });
    render(<LinkLookup />);

    await lookUp('Abc123x');

    expect((await screen.findByRole('alert')).textContent).toBe('Too many requests. Try again in 17 seconds.');
  });

  it('reports an unreachable service', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'));
    render(<LinkLookup />);

    await lookUp('Abc123x');

    expect((await screen.findByRole('alert')).textContent).toMatch(/service is unavailable/);
  });

  it('asks for a code when the field is empty', async () => {
    const fetchMock = api({});
    render(<LinkLookup />);

    await userEvent.setup().click(screen.getByRole('button', { name: 'Look up' }));

    expect((await screen.findByRole('alert')).textContent).toBe('Enter a code or a short URL.');
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('App', () => {
  it('shows a new link\'s details from the create form, and the page has no accessibility violations', async () => {
    const user = userEvent.setup();
    api({
      '/api/links': json(link, 201),
      '/api/links/Abc123x': json(link),
      '/api/links/Abc123x/stats': json(stats),
    });
    const { container } = render(<App />);

    await user.type(screen.getByLabelText('Long URL'), 'https://example.com/some/long/path');
    await user.click(screen.getByRole('button', { name: 'Shorten' }));
    await user.click(await screen.findByRole('button', { name: 'View details' }));

    expect(await screen.findByRole('table')).toBeTruthy();
    expect(screen.getByLabelText('Code or short URL')).toHaveProperty('value', 'Abc123x');
    expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Look up a link' }));

    const results = await axe.run(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations.map((violation) => `${violation.id}: ${violation.help}`)).toEqual([]);
  });
});
