// FROZEN offline set, pinned for webclient 1.1.0 (2026-09-20).
// This is the set bundled under public/gfonts for offline use.
// Do NOT regenerate from Google's live popularity ranking: the ranking
// reshuffles constantly, which would churn the bundled files and the
// manifest for no user-visible benefit. Change this list only by deliberate
// hand edit, then re-run `node scripts/cache_google_fonts.cjs --full
// --featured` for the new files and prune `public/gfonts/` back to the
// listed families plus a manifest rewrite.

export interface FeaturedGoogleFont {
  family: string;
  category: string;
}

export const FEATURED_GOOGLE_FONTS: FeaturedGoogleFont[] = [
  // Display (10)
  { family: "Black Ops One", category: "Display" },
  { family: "Lobster Two", category: "Display" },
  { family: "Changa One", category: "Display" },
  { family: "Alfa Slab One", category: "Display" },
  { family: "Lilita One", category: "Display" },
  { family: "Bungee", category: "Display" },
  { family: "Gravitas One", category: "Display" },
  { family: "Lobster", category: "Display" },
  { family: "Comfortaa", category: "Display" },
  { family: "Abril Fatface", category: "Display" },
  // Handwriting (10)
  { family: "Dancing Script", category: "Handwriting" },
  { family: "Caveat", category: "Handwriting" },
  { family: "Pacifico", category: "Handwriting" },
  { family: "Shadows Into Light", category: "Handwriting" },
  { family: "Great Vibes", category: "Handwriting" },
  { family: "Zeyada", category: "Handwriting" },
  { family: "Indie Flower", category: "Handwriting" },
  { family: "Permanent Marker", category: "Handwriting" },
  { family: "Satisfy", category: "Handwriting" },
  { family: "Yellowtail", category: "Handwriting" },
  // Monospace (10)
  { family: "Roboto Mono", category: "Monospace" },
  { family: "JetBrains Mono", category: "Monospace" },
  { family: "Inconsolata", category: "Monospace" },
  { family: "Source Code Pro", category: "Monospace" },
  { family: "IBM Plex Mono", category: "Monospace" },
  { family: "Space Mono", category: "Monospace" },
  { family: "DM Mono", category: "Monospace" },
  { family: "Geist Mono", category: "Monospace" },
  { family: "Courier Prime", category: "Monospace" },
  { family: "Fira Code", category: "Monospace" },
  // Sans Serif (10)
  { family: "Roboto", category: "Sans Serif" },
  { family: "Open Sans", category: "Sans Serif" },
  { family: "Google Sans", category: "Sans Serif" },
  { family: "Inter", category: "Sans Serif" },
  { family: "Montserrat", category: "Sans Serif" },
  { family: "Poppins", category: "Sans Serif" },
  { family: "Lato", category: "Sans Serif" },
  { family: "Arimo", category: "Sans Serif" },
  { family: "Roboto Condensed", category: "Sans Serif" },
  { family: "Oswald", category: "Sans Serif" },
  // Serif (10)
  { family: "Playfair Display", category: "Serif" },
  { family: "Roboto Slab", category: "Serif" },
  { family: "Merriweather", category: "Serif" },
  { family: "Lora", category: "Serif" },
  { family: "Noto Serif", category: "Serif" },
  { family: "Libre Baskerville", category: "Serif" },
  { family: "Cormorant Garamond", category: "Serif" },
  { family: "PT Serif", category: "Serif" },
  { family: "EB Garamond", category: "Serif" },
  { family: "Instrument Serif", category: "Serif" },
];
