/** Scroller fallback grid: must match GridRenderer's canvas strokes exactly. */

/** Grid line color, shared with the canvas grid strokes in GridRenderer. */
export const SCROLLER_GRID_COLOR = '#222';

/** Fallback tile size when metrics are unusable (matches the CSS defaults). */
const FALLBACK_W = 10;
const FALLBACK_H = 18;

function saneDim(value: number, fallback: number): number {
    return Number.isFinite(value) && value > 0 ? parseFloat(value.toFixed(2)) : fallback;
}

/**
 * Background tile for `#canvas-container`: a 1px top edge plus a 1px left
 * edge in the grid color, baked at the live cell size. SVG with
 * `crispEdges`, never a gradient: coincident-stop linear-gradients get
 * bitmap-cached and resampled by the browser (worse under fractional tile
 * sizes or OS display scaling), so the container lines render as a soft
 * fade that reads lighter than the canvas's solid strokes. Hard SVG edges
 * match the canvas by construction. Returns a `url("data:...")` value
 * ready for `background-image`.
 */
export function scrollerGridTile(cellW: number, cellH: number): string {
    const w = saneDim(cellW, FALLBACK_W);
    const h = saneDim(cellH, FALLBACK_H);
    const svg =
        `<svg xmlns='http://www.w3.org/2000/svg' width='${w}' height='${h}'>` +
        `<path d='M${w} 0H0V${h}' fill='none' stroke='${SCROLLER_GRID_COLOR}' ` +
        `stroke-width='1' shape-rendering='crispEdges'/></svg>`;
    return `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;
}
