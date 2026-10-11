// Lookup accepts a code or a whole short URL (plan R6): the code is the last path segment.

export function codeFromInput(input: string): string {
  const trimmed = input.trim();
  try {
    const segments = new URL(trimmed).pathname.split('/').filter(Boolean);
    return segments.at(-1) ?? '';
  } catch {
    return trimmed.replace(/^\/+|\/+$/g, '');
  }
}
