const CSS_FONT_FALLBACK = 'monospace';

/**
 * Normalizes an app font-family value into a safe CSS font-family fragment.
 * Never throws: null/undefined/empty input yields the fallback. Dangerous
 * CSS constructs (`;`, `!`, `url(`, parens) are stripped so a hostile family
 * name cannot break out of a `font` shorthand or `font-family` declaration.
 * Legitimate quoted names and comma-separated fallback lists pass through.
 */
export function toCssFontFamily(value: string | null | undefined): string {
  if (typeof value !== 'string') return CSS_FONT_FALLBACK;
  let trimmed = value.trim();
  if (!trimmed) return CSS_FONT_FALLBACK;
  trimmed = trimmed
    .replace(/;/g, '')
    .replace(/!/g, '')
    .replace(/url\s*\(/gi, '')
    .replace(/[()]/g, '')
    .trim();
  if (!trimmed) return CSS_FONT_FALLBACK;
  if (trimmed.includes(',')) return trimmed;
  if (trimmed.startsWith("'") || trimmed.startsWith('"')) return trimmed;
  if (/\s/.test(trimmed)) return `"${trimmed}"`;
  return trimmed;
}

/**
 * Sanitizes a single font family name to plain identifier characters
 * (`[A-Za-z0-9 _-]`). Quotes, semicolons, CSS functions and anything else
 * outside the allowlist are stripped. Returns the fallback when nothing
 * usable remains. Use this before interpolating a name into a `font`
 * shorthand or `font-family` style string.
 */
export function sanitizeFontFamily(value: unknown, fallback = CSS_FONT_FALLBACK): string {
  if (typeof value !== 'string') return fallback;
  const cleaned = value
    .replace(/[^A-Za-z0-9 _-]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
  return cleaned || fallback;
}

/**
 * Sanitizes a possibly comma-separated font-family list segment by segment
 * (each via {@link sanitizeFontFamily}), re-quoting names that contain
 * spaces so the result stays valid CSS. Drops empty segments; returns the
 * fallback when nothing usable remains.
 */
export function sanitizeFontList(value: unknown, fallback = CSS_FONT_FALLBACK): string {
  if (typeof value !== 'string') return fallback;
  const parts = value
    .split(',')
    .map((seg) => sanitizeFontFamily(seg, ''))
    .filter((seg) => seg.length > 0)
    .map((seg) => (seg.includes(' ') ? `"${seg}"` : seg));
  return parts.length > 0 ? parts.join(', ') : fallback;
}
