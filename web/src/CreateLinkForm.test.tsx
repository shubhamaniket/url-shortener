import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { CreateLinkForm } from './CreateLinkForm';

const created = {
  code: 'Abc123x',
  shortUrl: 'http://localhost:5058/Abc123x',
  originalUrl: 'https://example.com/some/long/path',
  createdAtUtc: '2026-10-11T07:00:00Z',
  expiresAtUtc: null,
  clickCount: 0,
};

function respond(status: number, body: unknown, headers: Record<string, string> = {}) {
  return vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(body), { status, headers }));
}

function sentBody(fetchMock: ReturnType<typeof respond>) {
  return JSON.parse(String(fetchMock.mock.calls[0][1]?.body));
}

describe('CreateLinkForm', () => {
  it('shows the short URL and copies it', async () => {
    const user = userEvent.setup();
    respond(201, created);
    render(<CreateLinkForm />);

    await user.type(screen.getByLabelText('Long URL'), 'https://example.com/some/long/path');
    await user.click(screen.getByRole('button', { name: 'Shorten' }));
    await user.click(await screen.findByRole('button', { name: 'Copy' }));

    expect(screen.getByRole('link', { name: created.shortUrl })).toHaveProperty('href', created.shortUrl);
    expect(await navigator.clipboard.readText()).toBe(created.shortUrl);
    expect(screen.getByText('Copied')).toBeTruthy();
    expect(screen.getByText(/Never expires/)).toBeTruthy();
  });

  it('sends the alias and the expiry with the local time zone', async () => {
    const user = userEvent.setup();
    const fetchMock = respond(201, { ...created, code: 'promo', expiresAtUtc: '2026-12-31T18:29:00Z' });
    render(<CreateLinkForm />);

    await user.type(screen.getByLabelText('Long URL'), 'https://example.com/');
    await user.type(screen.getByLabelText(/Custom alias/), '  promo ');
    fireEvent.change(screen.getByLabelText(/Expires/), { target: { value: '2026-12-31T23:59' } });
    await user.click(screen.getByRole('button', { name: 'Shorten' }));
    await screen.findByText(/Your short link/);

    const body = sentBody(fetchMock);
    expect(body.customAlias).toBe('promo');
    expect(body.expiresAt).toMatch(/^2026-12-31T23:59:00[+-]\d{2}:\d{2}$/);
    expect(screen.getByText(/Expires /)).toBeTruthy();
  });

  it('shows field errors next to their fields, keeps the input and focuses the first one', async () => {
    const user = userEvent.setup();
    respond(400, { title: 'One or more validation errors occurred.', errors: { url: ['The URL must be an absolute http or https URL.'], customAlias: ["The alias 'api' is reserved."] } });
    render(<CreateLinkForm />);

    await user.type(screen.getByLabelText('Long URL'), 'javascript:alert(1)');
    await user.type(screen.getByLabelText(/Custom alias/), 'api');
    await user.click(screen.getByRole('button', { name: 'Shorten' }));

    const urlInput = screen.getByLabelText('Long URL');
    expect(await screen.findByText('The URL must be an absolute http or https URL.')).toBeTruthy();
    expect(screen.getByText("The alias 'api' is reserved.")).toBeTruthy();
    expect(urlInput).toHaveProperty('value', 'javascript:alert(1)');
    expect(urlInput.getAttribute('aria-invalid')).toBe('true');
    expect(urlInput.getAttribute('aria-describedby')).toBe('url-error');
    expect(document.activeElement).toBe(urlInput);
  });

  it('explains a taken alias', async () => {
    const user = userEvent.setup();
    respond(409, { title: 'Alias already in use', detail: "The alias 'promo' is already taken." });
    render(<CreateLinkForm />);

    await user.type(screen.getByLabelText('Long URL'), 'https://example.com/');
    await user.click(screen.getByRole('button', { name: 'Shorten' }));

    expect((await screen.findByRole('alert')).textContent).toBe("The alias 'promo' is already taken.");
  });

  it('disables the button while the request is in progress', async () => {
    const user = userEvent.setup();
    vi.spyOn(globalThis, 'fetch').mockReturnValue(new Promise(() => {}));
    render(<CreateLinkForm />);

    await user.type(screen.getByLabelText('Long URL'), 'https://example.com/');
    await user.click(screen.getByRole('button', { name: 'Shorten' }));

    expect(screen.getByRole('button', { name: 'Shortening…' })).toHaveProperty('disabled', true);
  });

  it('offers manual copy when the clipboard is unavailable', async () => {
    const user = userEvent.setup();
    respond(201, created);
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));
    render(<CreateLinkForm />);

    await user.type(screen.getByLabelText('Long URL'), 'https://example.com/');
    await user.click(screen.getByRole('button', { name: 'Shorten' }));
    await user.click(await screen.findByRole('button', { name: 'Copy' }));

    expect(screen.getByText(/Select the link and copy it manually/)).toBeTruthy();
  });

  it('lets the user view the new link\'s details', async () => {
    const user = userEvent.setup();
    respond(201, created);
    const onViewDetails = vi.fn();
    render(<CreateLinkForm onViewDetails={onViewDetails} />);

    await user.type(screen.getByLabelText('Long URL'), 'https://example.com/');
    await user.click(screen.getByRole('button', { name: 'Shorten' }));
    await user.click(await screen.findByRole('button', { name: 'View details' }));

    expect(onViewDetails).toHaveBeenCalledWith('Abc123x');
  });
});
