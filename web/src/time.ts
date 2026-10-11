// The API requires an expiry with an explicit zone (feature 002). A datetime-local input gives
// local wall-clock time without one, so the browser's offset for that date is appended (plan R3).

/**
 * Turns a datetime-local value ("2026-12-31T23:59" or with seconds) into
 * "2026-12-31T23:59:00+05:30". `offsetMinutes` is east of UTC; by default the browser's offset for
 * that local date (daylight-saving aware).
 */
export function toZonedIso(localValue: string, offsetMinutes?: number): string {
  const withSeconds = localValue.length === 16 ? `${localValue}:00` : localValue;
  const offset = offsetMinutes ?? -new Date(localValue).getTimezoneOffset();
  const sign = offset < 0 ? '-' : '+';
  const absolute = Math.abs(offset);
  const hours = String(Math.floor(absolute / 60)).padStart(2, '0');
  const minutes = String(absolute % 60).padStart(2, '0');
  return `${withSeconds}${sign}${hours}:${minutes}`;
}

/** A UTC timestamp from the API, shown in the user's local time. */
export function formatLocal(isoUtc: string): string {
  return new Date(isoUtc).toLocaleString();
}
