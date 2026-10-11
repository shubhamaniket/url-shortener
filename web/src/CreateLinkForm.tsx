import { useEffect, useRef, useState, type FormEvent } from 'react';

import { createLink, type ApiError, type LinkResponse } from './api';
import { formatLocal, toZonedIso } from './time';

type Field = 'url' | 'customAlias' | 'expiresAt';
const fieldOrder: Field[] = ['url', 'customAlias', 'expiresAt'];

interface Props {
  /** Shows the new link's details and statistics (US2 scenario 6). */
  onViewDetails?: (code: string) => void;
}

export function CreateLinkForm({ onViewDetails }: Props) {
  const [url, setUrl] = useState('');
  const [alias, setAlias] = useState('');
  const [expiry, setExpiry] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [created, setCreated] = useState<LinkResponse | null>(null);
  const [copyStatus, setCopyStatus] = useState('');
  const urlInput = useRef<HTMLInputElement>(null);
  const aliasInput = useRef<HTMLInputElement>(null);
  const expiryInput = useRef<HTMLInputElement>(null);

  // Move focus to the first field the service rejected, so keyboard and screen-reader users land on it.
  useEffect(() => {
    const first = fieldOrder.find((field) => error?.fieldErrors[field]?.length);
    const input = { url: urlInput, customAlias: aliasInput, expiresAt: expiryInput }[first ?? 'url'];
    if (first) {
      input.current?.focus();
    }
  }, [error]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setError(null);
    setCopyStatus('');

    const result = await createLink({
      url: url.trim(),
      customAlias: alias.trim() || undefined,
      expiresAt: expiry ? toZonedIso(expiry) : undefined,
    });

    setPending(false);
    if (result.ok) {
      setCreated(result.value);
      setUrl('');
      setAlias('');
      setExpiry('');
    } else {
      setCreated(null);
      setError(result.error);
    }
  }

  async function copy(shortUrl: string) {
    try {
      if (!navigator.clipboard) {
        throw new Error('Clipboard not available');
      }
      await navigator.clipboard.writeText(shortUrl);
      setCopyStatus('Copied');
    } catch {
      setCopyStatus('Copy is not available here. Select the link and copy it manually.');
    }
  }

  const fieldError = (field: Field) => error?.fieldErrors[field]?.join(' ');
  const describedBy = (field: Field, hint?: string) =>
    [hint, fieldError(field) ? `${field}-error` : undefined].filter(Boolean).join(' ') || undefined;

  return (
    <section aria-labelledby="create-heading">
      <h2 id="create-heading">Shorten a link</h2>
      <form onSubmit={submit} noValidate>
        <div className="field">
          <label htmlFor="url">Long URL</label>
          <input id="url" ref={urlInput} type="url" required value={url} onChange={(e) => setUrl(e.target.value)}
            placeholder="https://example.com/some/long/path" aria-invalid={Boolean(fieldError('url'))} aria-describedby={describedBy('url')} />
          {fieldError('url') && <p id="url-error" className="field-error">{fieldError('url')}</p>}
        </div>

        <div className="field">
          <label htmlFor="customAlias">Custom alias <span className="optional">(optional)</span></label>
          <input id="customAlias" ref={aliasInput} value={alias} onChange={(e) => setAlias(e.target.value)}
            placeholder="team-offsite" aria-invalid={Boolean(fieldError('customAlias'))} aria-describedby={describedBy('customAlias', 'customAlias-hint')} />
          <p id="customAlias-hint" className="hint">3–30 letters, digits, - or _.</p>
          {fieldError('customAlias') && <p id="customAlias-error" className="field-error">{fieldError('customAlias')}</p>}
        </div>

        <div className="field">
          <label htmlFor="expiresAt">Expires <span className="optional">(optional, your local time)</span></label>
          <input id="expiresAt" ref={expiryInput} type="datetime-local" value={expiry} onChange={(e) => setExpiry(e.target.value)}
            aria-invalid={Boolean(fieldError('expiresAt'))} aria-describedby={describedBy('expiresAt', 'expiresAt-hint')} />
          <p id="expiresAt-hint" className="hint">Leave empty for a link that never expires.</p>
          {fieldError('expiresAt') && <p id="expiresAt-error" className="field-error">{fieldError('expiresAt')}</p>}
        </div>

        <button type="submit" disabled={pending}>{pending ? 'Shortening…' : 'Shorten'}</button>
      </form>

      <div aria-live="polite">
        {error && !Object.keys(error.fieldErrors).length && <p className="error" role="alert">{error.message}</p>}
        {error && Object.keys(error.fieldErrors).length > 0 && <p className="error">{error.message}</p>}
        {created && (
          <div className="result">
            <p>
              Your short link:{' '}
              <a href={created.shortUrl} target="_blank" rel="noopener noreferrer">{created.shortUrl}</a>
            </p>
            <p className="actions">
              <button type="button" onClick={() => copy(created.shortUrl)}>Copy</button>
              {onViewDetails && <button type="button" className="secondary" onClick={() => onViewDetails(created.code)}>View details</button>}
              {copyStatus && <span className="status">{copyStatus}</span>}
            </p>
            <p className="meta">
              Goes to <span className="long">{created.originalUrl}</span>. {created.expiresAtUtc ? `Expires ${formatLocal(created.expiresAtUtc)}.` : 'Never expires.'}
            </p>
          </div>
        )}
      </div>
    </section>
  );
}
