import type { LinkStats } from './api';

interface Props {
  stats: LinkStats;
}

/** 30 UTC days as a table with proportional bars, and the top referrers (FR-006). */
export function StatsView({ stats }: Props) {
  const max = Math.max(1, ...stats.dailyClicks.map((day) => day.clicks));

  return (
    <div className="stats">
      <h3>Clicks per day, last 30 days (UTC)</h3>
      <table className="daily">
        <caption className="visually-hidden">Clicks per day for the last 30 days, oldest first</caption>
        <thead>
          <tr>
            <th scope="col">Date</th>
            <th scope="col">Clicks</th>
          </tr>
        </thead>
        <tbody>
          {stats.dailyClicks.map((day) => (
            <tr key={day.date}>
              <th scope="row">{day.date}</th>
              <td>
                <span className="bar" aria-hidden="true" style={{ width: `${(day.clicks / max) * 100}%` }} />
                <span className="count">{day.clicks}</span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <h3>Top referrers</h3>
      {stats.topReferrers.length === 0 ? (
        <p>No referrers yet.</p>
      ) : (
        <ol className="referrers">
          {stats.topReferrers.map((referrer) => (
            <li key={referrer.source}>
              <span className="long">{referrer.source}</span>: {referrer.clicks} {referrer.clicks === 1 ? 'click' : 'clicks'}
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
