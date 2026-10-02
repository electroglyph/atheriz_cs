This is just a little unicode drawing app which will become the basis for my map designer in [AtheriZ](https://github.com/electroglyph/atheriz)

diagonal line drawing is a bit janky, but i can't be arsed to fix it right now

probably still a few other bugs to work out

`npm run build` to build, and `npm run dev` to run it locally.

The build also includes the TypeScript AtheriZ webclient at `dist/webclient/`.

To stage the compiled assets into a game web directory, build the
webclient (`npm run build`) and run the engine server's `deploy game`
verb:

`dotnet src/Atheriz.Server/bin/Release/net10.0/Atheriz.Server.dll deploy game --web-root /path/to/game/web`

Pass `--no-clean` to keep files the stage would otherwise remove.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for release notes.

## Google Fonts

The Text tool bundles 50 featured fonts (top 10 per Google category) under
`public/gfonts/`, so they work offline and appear directly in the font
dropdown. The full catalogue (~1,950 families, listed in
`src/data/googleFonts.ts`) is browsed via the `G Fonts` button and streams
from `fonts.googleapis.com` on demand — that path needs network access.

`node scripts/fetch_google_fonts.cjs` refreshes the full catalogue
(`src/data/googleFonts.ts`). The 50 bundled families are frozen in
`src/data/featuredGoogleFonts.ts` — edit by hand only, then
`node scripts/cache_google_fonts.cjs --full --featured` downloads any
missing featured files and pruning to the featured set keeps
`public/gfonts/` small.

note: this is almost entirely AI generated code, though i've attempted to do it sanely
