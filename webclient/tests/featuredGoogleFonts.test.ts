// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { readFileSync, existsSync } from 'fs';
import { join } from 'path';

const root = join(__dirname, '..');

function slugify(family: string): string {
  return family.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

describe('featured Google Fonts offline set', () => {
  it('holds 10 fonts from each of the 5 Google categories', async () => {
    const { FEATURED_GOOGLE_FONTS } = await import('../src/data/featuredGoogleFonts');
    const { GOOGLE_FONT_CATEGORIES } = await import('../src/data/googleFonts');
    expect(FEATURED_GOOGLE_FONTS).toHaveLength(50);
    const perCategory = new Map<string, number>();
    for (const f of FEATURED_GOOGLE_FONTS) {
      perCategory.set(f.category, (perCategory.get(f.category) ?? 0) + 1);
    }
    expect([...perCategory.keys()].sort()).toEqual([...GOOGLE_FONT_CATEGORIES].sort());
    for (const [category, count] of perCategory) {
      expect(count, category).toBe(10);
    }
  });

  it('excludes the multi-MB CJK families, which stream instead', async () => {
    const { FEATURED_GOOGLE_FONTS } = await import('../src/data/featuredGoogleFonts');
    const families = FEATURED_GOOGLE_FONTS.map(f => f.family);
    expect(families).not.toContain('Noto Sans JP');
    expect(families).not.toContain('Noto Serif JP');
  });

  it('is frozen: membership matches the pinned webclient 1.1.0 list exactly', async () => {
    const { FEATURED_GOOGLE_FONTS } = await import('../src/data/featuredGoogleFonts');
    const frozen: { family: string; category: string }[] = [
      { family: 'Black Ops One', category: 'Display' },
      { family: 'Lobster Two', category: 'Display' },
      { family: 'Changa One', category: 'Display' },
      { family: 'Alfa Slab One', category: 'Display' },
      { family: 'Lilita One', category: 'Display' },
      { family: 'Bungee', category: 'Display' },
      { family: 'Gravitas One', category: 'Display' },
      { family: 'Lobster', category: 'Display' },
      { family: 'Comfortaa', category: 'Display' },
      { family: 'Abril Fatface', category: 'Display' },
      { family: 'Dancing Script', category: 'Handwriting' },
      { family: 'Caveat', category: 'Handwriting' },
      { family: 'Pacifico', category: 'Handwriting' },
      { family: 'Shadows Into Light', category: 'Handwriting' },
      { family: 'Great Vibes', category: 'Handwriting' },
      { family: 'Zeyada', category: 'Handwriting' },
      { family: 'Indie Flower', category: 'Handwriting' },
      { family: 'Permanent Marker', category: 'Handwriting' },
      { family: 'Satisfy', category: 'Handwriting' },
      { family: 'Yellowtail', category: 'Handwriting' },
      { family: 'Roboto Mono', category: 'Monospace' },
      { family: 'JetBrains Mono', category: 'Monospace' },
      { family: 'Inconsolata', category: 'Monospace' },
      { family: 'Source Code Pro', category: 'Monospace' },
      { family: 'IBM Plex Mono', category: 'Monospace' },
      { family: 'Space Mono', category: 'Monospace' },
      { family: 'DM Mono', category: 'Monospace' },
      { family: 'Geist Mono', category: 'Monospace' },
      { family: 'Courier Prime', category: 'Monospace' },
      { family: 'Fira Code', category: 'Monospace' },
      { family: 'Roboto', category: 'Sans Serif' },
      { family: 'Open Sans', category: 'Sans Serif' },
      { family: 'Google Sans', category: 'Sans Serif' },
      { family: 'Inter', category: 'Sans Serif' },
      { family: 'Montserrat', category: 'Sans Serif' },
      { family: 'Poppins', category: 'Sans Serif' },
      { family: 'Lato', category: 'Sans Serif' },
      { family: 'Arimo', category: 'Sans Serif' },
      { family: 'Roboto Condensed', category: 'Sans Serif' },
      { family: 'Oswald', category: 'Sans Serif' },
      { family: 'Playfair Display', category: 'Serif' },
      { family: 'Roboto Slab', category: 'Serif' },
      { family: 'Merriweather', category: 'Serif' },
      { family: 'Lora', category: 'Serif' },
      { family: 'Noto Serif', category: 'Serif' },
      { family: 'Libre Baskerville', category: 'Serif' },
      { family: 'Cormorant Garamond', category: 'Serif' },
      { family: 'PT Serif', category: 'Serif' },
      { family: 'EB Garamond', category: 'Serif' },
      { family: 'Instrument Serif', category: 'Serif' },
    ];
    expect(FEATURED_GOOGLE_FONTS).toEqual(frozen);
  });

  it('manifest lists exactly the featured slugs with cached CSS on disk', async () => {
    const { FEATURED_GOOGLE_FONTS } = await import('../src/data/featuredGoogleFonts');
    const manifest = JSON.parse(readFileSync(join(root, 'public/gfonts/manifest.json'), 'utf8'));
    const manifestSlugs = manifest.fonts.map((f: { slug: string }) => f.slug);
    expect(manifestSlugs).toEqual(FEATURED_GOOGLE_FONTS.map(f => slugify(f.family)));
    for (const f of FEATURED_GOOGLE_FONTS) {
      const slug = slugify(f.family);
      expect(existsSync(join(root, `public/gfonts/css/preview-${slug}.css`)), `preview ${slug}`).toBe(true);
      expect(existsSync(join(root, `public/gfonts/css/full-${slug}.css`)), `full ${slug}`).toBe(true);
    }
    expect(existsSync(join(root, 'public/gfonts/LICENSES.TXT'))).toBe(true);
  });
});

describe('Google font loader streams non-bundled families from the CDN', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', async () => ({ ok: false, json: async () => ({ fonts: [] }) }));
    Object.defineProperty(document, 'fonts', {
      value: { load: async () => [], ready: Promise.resolve() },
      configurable: true,
    });
  });

  afterEach(() => {
    document.head.querySelectorAll('link').forEach(el => el.remove());
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('loadFontPreview injects a googleapis stylesheet with display=swap', async () => {
    const { loadFontPreview } = await import('../src/utils/googleFontLoader');
    loadFontPreview('Zeyada Preview Test Family XYZ');
    await vi.waitFor(() => {
      const links = Array.from(document.head.querySelectorAll('link')).map(l => l.href);
      expect(links.some(h => h.includes('fonts.googleapis.com') && h.includes('display=swap'))).toBe(true);
    });
  });

  it('loadFontFull injects a googleapis full stylesheet and resolves', async () => {
    const { loadFontFull } = await import('../src/utils/googleFontLoader');
    await loadFontFull('Zeyada Full Test Family XYZ');
    const links = Array.from(document.head.querySelectorAll('link')).map(l => l.href);
    expect(links.some(h => h.includes('fonts.googleapis.com') && h.includes('wght@400;700'))).toBe(true);
  });
});
