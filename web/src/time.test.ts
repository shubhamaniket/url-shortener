import { describe, expect, it } from 'vitest';

import { toZonedIso } from './time';

describe('toZonedIso', () => {
  it.each([
    ['2026-12-31T23:59', 330, '2026-12-31T23:59:00+05:30'],
    ['2026-12-31T23:59', -480, '2026-12-31T23:59:00-08:00'],
    ['2026-12-31T23:59', 0, '2026-12-31T23:59:00+00:00'],
    ['2026-12-31T23:59:30', 60, '2026-12-31T23:59:30+01:00'],
    ['2026-06-01T08:00', -210, '2026-06-01T08:00:00-03:30'],
  ])('%s at offset %i becomes %s', (local, offset, expected) => {
    expect(toZonedIso(local, offset)).toBe(expected);
  });

  it('uses the browser offset for that date when none is given', () => {
    const local = '2026-12-31T23:59';
    const minutes = -new Date(local).getTimezoneOffset();

    expect(toZonedIso(local)).toBe(toZonedIso(local, minutes));
  });
});
