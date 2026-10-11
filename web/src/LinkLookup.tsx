import { useEffect, useRef, useState, type FormEvent } from 'react';

import { getLink, getStats, type ApiError, type LinkResponse, type LinkStats } from './api';
import { codeFromInput } from './lookup';
import { StatsView } from './StatsView';
import { formatLocal } from './time';

export interface LookupRequest {
  code: string;
  /** Changes on every request, so asking for the same code again still runs a lookup. */
  id: number;
}

interface Props {
  /** A lookup requested from elsewhere on the page ("View details" after creating a link). */
  request?: LookupRequest;
}

interface Loaded {
  link: LinkResponse;
  stats: LinkStats | null;
  statsError: ApiError | null;
}

export function LinkLookup({ request }: Props) {
  const [input, setInput] = useState(request?.code ?? '');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);

  async function lookUp(raw: string) {
    const code = codeFromInput(raw);
    if (!code) {
      setLoaded(null);
      setError('Enter a code or a short URL.');
      return;
    }

    setPending(true);
    setError(null);
    const [link, stats] = await Promise.all([getLink(code), getStats(code)]);
    setPending(false);

    if (!link.ok) {
      setLoaded(null);
      setError(link.error.message);
      return;
    }

    setLoaded({ link: link.value, stats: stats.ok ? stats.value : null, statsError: stats.ok ? null : stats.error });
  }

  // "View details" from the create form: fill in the code, run the lookup, and move focus here.
  useEffect(() => {
    if (!request) {
      return;
    }

    let active = true;
    queueMicrotask(() => {
      if (active) {
        setInput(request.code);
        heading.current?.focus();
        void lookUp(request.code);
      }
    });
    return () => {
      active = false;
    };
  }, [request]);

  function submit(event: FormEvent) {
    event.preventDefault();
    void lookUp(input);
  }

  const expired = loaded?.link.expiresAtUtc ? new Date(loaded.link.expiresAtUtc) <= new Date() : false;

  return (
    <section aria-labelledby="lookup-heading">
      <h2 id="lookup-heading" ref={heading} tabIndex={-1}>Look up a link</h2>
      <form onSubmit={submit} noValidate>
        <div className="field">
          <label htmlFor="lookup">Code or short URL</label>
          <input id="lookup" value={input} onChange={(e) => setInput(e.target.value)} placeholder="aZ3kP9q or http://localhost:8080/aZ3kP9q" />
        </div>
        <button type="submit" disabled={pending}>{pending ? 'Looking up…' : 'Look up'}</button>
      </form>

      <div aria-live="polite">
        {error && <p className="error" role="alert">{error}</p>}
        {loaded && (
          <div className="result">
            <dl className="details">
              <dt>Short URL</dt>
              <dd><a href={loaded.link.shortUrl} target="_blank" rel="noopener noreferrer">{loaded.link.shortUrl}</a></dd>
              <dt>Goes to</dt>
              <dd className="long">{loaded.link.originalUrl}</dd>
              <dt>Created</dt>
              <dd>{formatLocal(loaded.link.createdAtUtc)}</dd>
              <dt>Expires</dt>
              <dd>
                {loaded.link.expiresAtUtc ? formatLocal(loaded.link.expiresAtUtc) : 'Never'}
                {expired && <span className="badge">Expired</span>}
              </dd>
              <dt>Total clicks</dt>
              <dd>{loaded.stats?.totalClicks ?? loaded.link.clickCount}</dd>
            </dl>
            {loaded.stats && <StatsView stats={loaded.stats} />}
            {loaded.statsError && <p className="error">Statistics are not available right now: {loaded.statsError.message}</p>}
          </div>
        )}
      </div>
    </section>
  );
}
