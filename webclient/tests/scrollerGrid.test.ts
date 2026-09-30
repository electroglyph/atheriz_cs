import { describe, it, expect } from 'vitest';
import { scrollerGridTile, SCROLLER_GRID_COLOR } from '../src/utils/scrollerGrid';

function decodeTile(tile: string): string {
    const prefix = 'url("data:image/svg+xml,';
    expect(tile.startsWith(prefix)).toBe(true);
    expect(tile.endsWith('")')).toBe(true);
    return decodeURIComponent(tile.slice(prefix.length, -2));
}

describe('scrollerGridTile', () => {
    it('shares the canvas grid color, never a gradient', () => {
        expect(SCROLLER_GRID_COLOR).toBe('#222');
        const svg = decodeTile(scrollerGridTile(10, 18));
        expect(svg).toContain(`stroke='${SCROLLER_GRID_COLOR}'`);
        expect(svg).not.toContain('linear-gradient');
        expect(svg).not.toContain('radial-gradient');
    });

    it('bakes the live cell size into the tile and strokes 1px hard edges', () => {
        const svg = decodeTile(scrollerGridTile(10, 18));
        expect(svg).toContain(`width='10'`);
        expect(svg).toContain(`height='18'`);
        expect(svg).toContain(`stroke-width='1'`);
        expect(svg).toContain(`shape-rendering='crispEdges'`);
    });

    it('draws the top and left edges so tiles join into a continuous grid', () => {
        const svg = decodeTile(scrollerGridTile(10, 18));
        // M10 0: start top-right, H0: top edge, V18: left edge down.
        expect(svg).toContain(`d='M10 0H0V18'`);
    });

    it('preserves fractional cell sizes instead of rounding to whole px', () => {
        const svg = decodeTile(scrollerGridTile(9.61, 19.2));
        expect(svg).toContain(`width='9.61'`);
        expect(svg).toContain(`height='19.2'`);
        expect(svg).toContain(`d='M9.61 0H0V19.2'`);
    });

    it('falls back to the CSS default tile for unusable metrics', () => {
        for (const [w, h] of [[NaN, 18], [10, Infinity], [0, 18], [-5, -5]] as Array<[number, number]>) {
            const svg = decodeTile(scrollerGridTile(w, h));
            const ew = Number.isFinite(w) && w > 0 ? w : 10;
            const eh = Number.isFinite(h) && h > 0 ? h : 18;
            expect(svg).toContain(`width='${ew}'`);
            expect(svg).toContain(`height='${eh}'`);
            expect(svg).toContain(`stroke='${SCROLLER_GRID_COLOR}'`);
        }
    });
});
