const loadedPreviews = new Set<string>();
const loadedFull = new Set<string>();

import { toCssFontFamily } from './cssFont';

const CACHE_BASE = import.meta.env.BASE_URL + 'gfonts/css';
const CDN_BASE = 'https://fonts.googleapis.com/css2';

/** Network timeout for the font manifest fetch. */
export const FONT_MANIFEST_TIMEOUT_MS = 10000;

let cacheManifest: Set<string> | null = null;
let manifestPromise: Promise<Set<string>> | null = null;

function slugify(family: string): string {
    return family.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

export function fontNameToCSS(family: string): string {
    return toCssFontFamily(family);
}

function loadManifest(): Promise<Set<string>> {
    if (cacheManifest !== null) return Promise.resolve(cacheManifest);
    if (manifestPromise) return manifestPromise;
    manifestPromise = (async () => {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), FONT_MANIFEST_TIMEOUT_MS);
        try {
            const res = await fetch(import.meta.env.BASE_URL + 'gfonts/manifest.json', { signal: controller.signal });
            if (!res.ok) throw new Error(`manifest HTTP ${res.status}`);
            const data = await res.json();
            cacheManifest = new Set(data.fonts.map((f: { slug: string }) => f.slug));
        } catch (err) {
            console.warn('googleFontLoader: font manifest unavailable, using CDN fallback', err);
            cacheManifest = new Set();
        } finally {
            clearTimeout(timer);
        }
        return cacheManifest!;
    })();
    return manifestPromise;
}

export function preloadManifest(): void {
    void loadManifest();
}

function hasCache(slug: string): boolean {
    return cacheManifest !== null && cacheManifest.has(slug);
}

function injectStylesheet(href: string, fallbackHref?: string): void {
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    if (fallbackHref) {
        link.onerror = () => {
            link.href = fallbackHref;
        };
    }
    document.head.appendChild(link);
}

function cdnPreviewUrl(family: string): string {
    return `${CDN_BASE}?family=${encodeURIComponent(family)}&text=${encodeURIComponent(family)}&display=swap`;
}

function cdnFullUrl(family: string): string {
    return `${CDN_BASE}?family=${encodeURIComponent(family)}:wght@400;700&display=swap`;
}

export function loadFontPreview(family: string): void {
    if (loadedPreviews.has(family)) return;
    loadedPreviews.add(family);

    const slug = slugify(family);

    if (hasCache(slug)) {
        injectStylesheet(`${CACHE_BASE}/preview-${slug}.css`, cdnPreviewUrl(family));
    } else {
        loadManifest().then(manifest => {
            if (manifest.has(slug)) {
                injectStylesheet(`${CACHE_BASE}/preview-${slug}.css`, cdnPreviewUrl(family));
            } else {
                injectStylesheet(cdnPreviewUrl(family));
            }
        });
    }
}

/**
 * Loads the full (400 + 700) webfont for `family`. Returns true when the
 * font loaded and `document.fonts` settled, false when loading failed (the
 * caller should fall back to already-available fonts). Already-loaded
 * families short-circuit to true.
 */
export async function loadFontFull(family: string): Promise<boolean> {
    if (loadedFull.has(family)) return true;
    loadedFull.add(family);

    const slug = slugify(family);

    if (hasCache(slug)) {
        injectStylesheet(`${CACHE_BASE}/full-${slug}.css`, cdnFullUrl(family));
    } else {
        const manifest = await loadManifest();
        if (manifest.has(slug)) {
            injectStylesheet(`${CACHE_BASE}/full-${slug}.css`, cdnFullUrl(family));
        } else {
            injectStylesheet(cdnFullUrl(family));
        }
    }

    try {
        await document.fonts.load(`400px ${fontNameToCSS(family)}`);
        await document.fonts.load(`bold 40px ${fontNameToCSS(family)}`);
    } catch (err) {
        console.warn(`googleFontLoader: failed to load font "${family}"`, err);
        return false;
    }
    try {
        await Promise.race([
            document.fonts.ready,
            new Promise((_, reject) => setTimeout(
                () => reject(new Error('document.fonts.ready timeout')),
                FONT_MANIFEST_TIMEOUT_MS,
            )),
        ]);
    } catch (err) {
        console.warn(`googleFontLoader: fonts.ready timed out for "${family}"`, err);
    }
    return true;
}
