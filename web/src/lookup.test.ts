import { describe, expect, it } from 'vitest';

import { codeFromInput } from './lookup';

describe('codeFromInput', () => {
  it.each([
    ['Abc123x', 'Abc123x'],
    ['  team-offsite  ', 'team-offsite'],
    ['http://localhost:5058/Abc123x', 'Abc123x'],
    ['https://sho.rt/team-offsite/', 'team-offsite'],
    ['/Abc123x/', 'Abc123x'],
    ['', ''],
  ])('"%s" → "%s"', (input, expected) => {
    expect(codeFromInput(input)).toBe(expected);
  });
});
