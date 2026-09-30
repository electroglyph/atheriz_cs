// @ts-nocheck
// Pins for the single-surface canvas: the element covers the whole
// viewport with storage at a fixed inset, so every visible grid square
// is drawable and no second grid renderer has to match the canvas.
import { describe, it, expect } from 'vitest';
import { CanvasState } from '../src/state/CanvasState';
import {
    GridRenderer,
    planCanvasView,
    planContentScroll,
    STATE_ORIGIN_MARGIN,
} from '../src/canvas/GridRenderer';

const METRICS = { width: 8, height: 16, font: '16px monospace', advance: 8 };

function makeProbedCanvas() {
    const record = { rects: [], fills: [], lines: [], transforms: [] };
    let pen = null;
    const ctx = {
        setTransform: (...a) => { record.transforms.push(a); },
        fillRect: (...a) => { record.fills.push(a); },
        strokeRect: (...a) => { record.rects.push(a); },
        clearRect: () => {},
        fillText: () => {},
        fill: () => {},
        stroke: () => {},
        beginPath: () => {},
        moveTo: (x, y) => { pen = [x, y]; },
        lineTo: (x, y) => { if (pen) record.lines.push([...pen, x, y]); pen = [x, y]; },
        save: () => {},
        restore: () => {},
        set font(v) {},
        set fillStyle(v) {},
        set strokeStyle(v) {},
        set lineWidth(v) {},
        set globalAlpha(v) {},
        set textBaseline(v) {},
        set textAlign(v) {},
    };
    const canvas = { width: 0, height: 0, style: {}, getContext: () => ctx };
    return { record, canvas };
}

function makeRenderer(state, dpr = 1) {
    const { record, canvas } = makeProbedCanvas();
    const renderer = new GridRenderer(canvas, state, METRICS, dpr);
    return { renderer, record, canvas };
}

describe('planCanvasView', () => {
    it('covers storage at the inset plus a margin', () => {
        const need = planCanvasView({
            stateW: 24, stateH: 24, offsetCol: 128, offsetRow: 128,
            viewCols: 10, viewRows: 10,
            scrollX: 0, scrollY: 0, clientW: 0, clientH: 0,
            cellW: 8, cellH: 16,
        });
        expect(need.viewCols).toBe(128 + 24 + 32);
        expect(need.viewRows).toBe(128 + 24 + 32);
    });

    it('covers the scrolled viewport plus overscan', () => {
        // 8px cells, scrolled to x=1000 with a 400px viewport:
        // (1000 + 400 + 256) / 8 = 207 cells.
        const need = planCanvasView({
            stateW: 24, stateH: 24, offsetCol: 128, offsetRow: 128,
            viewCols: 184, viewRows: 184,
            scrollX: 1000, scrollY: 0, clientW: 400, clientH: 100,
            cellW: 8, cellH: 16,
        });
        expect(need.viewCols).toBe(207);
        expect(need.viewRows).toBe(184);
    });

    it('never shrinks the view', () => {
        const need = planCanvasView({
            stateW: 1, stateH: 1, offsetCol: 0, offsetRow: 0,
            viewCols: 500, viewRows: 500,
            scrollX: 0, scrollY: 0, clientW: 10, clientH: 10,
            cellW: 8, cellH: 16,
        });
        expect(need.viewCols).toBe(500);
        expect(need.viewRows).toBe(500);
    });

    it('ignores unusable cell sizes instead of collapsing', () => {
        const need = planCanvasView({
            stateW: 24, stateH: 24, offsetCol: 0, offsetRow: 0,
            viewCols: 10, viewRows: 10,
            scrollX: 0, scrollY: 0, clientW: 400, clientH: 400,
            cellW: NaN, cellH: 0,
        });
        expect(need.viewCols).toBe(24 + 32);
        expect(need.viewRows).toBe(24 + 32);
    });
});

describe('planContentScroll', () => {
    it('puts the content top-left in view with a margin', () => {
        // 8px cols, 16px rows, content rect at state (5,5), origin 128:
        // left = 133*8 - 3*8, top = 133*16 - 3*16.
        const pos = planContentScroll({
            offsetCol: 128, offsetRow: 128,
            rectCol: 5, rectRow: 5,
            cellW: 8, cellH: 16,
        });
        expect(pos.scrollLeft).toBe(133 * 8 - 24);
        expect(pos.scrollTop).toBe(133 * 16 - 48);
    });

    it('clamps at zero when content starts at the origin', () => {
        const pos = planContentScroll({
            offsetCol: 0, offsetRow: 0,
            rectCol: 0, rectRow: 0,
            cellW: 8, cellH: 16,
        });
        expect(pos).toEqual({ scrollLeft: 0, scrollTop: 0 });
    });

    it('falls back to 1px cells for garbage metrics instead of NaN scroll', () => {
        const pos = planContentScroll({
            offsetCol: 128, offsetRow: 128,
            rectCol: 0, rectRow: 0,
            cellW: NaN, cellH: 0,
        });
        expect(pos).toEqual({ scrollLeft: 125, scrollTop: 125 });
        expect(Number.isNaN(pos.scrollLeft)).toBe(false);
    });

    it('renderer reports the origin the host set', () => {
        const state = new CanvasState(4, 4, false);
        const { renderer } = makeRenderer(state);
        expect(renderer.getViewOrigin()).toEqual({ offsetCol: 0, offsetRow: 0 });
        renderer.setViewOrigin(128, 128);
        expect(renderer.getViewOrigin()).toEqual({ offsetCol: 128, offsetRow: 128 });
    });
});

describe('GridRenderer drawable view', () => {
    it('defaults to a storage-sized view at origin zero', () => {
        const state = new CanvasState(10, 8, false);
        const { canvas } = makeRenderer(state);
        expect(canvas.width).toBe(80);
        expect(canvas.height).toBe(128);
    });

    it('draws the violet outline at the state inset', () => {
        const state = new CanvasState(10, 8, false);
        state.setServerBounds({ col: 1, row: 2, w: 4, h: 3 });
        const { renderer, record } = makeRenderer(state);
        renderer.setViewOrigin(5, 5);
        // setViewOrigin re-renders into the same record.
        expect(record.rects).toContainEqual([(5 + 1) * 8, (5 + 2) * 16, 4 * 8, 3 * 16]);
    });

    it('spans grid lines across the whole view, not just storage', () => {
        const state = new CanvasState(3, 2, false);
        const { renderer, record, canvas } = makeRenderer(state);
        renderer.setViewOrigin(STATE_ORIGIN_MARGIN, STATE_ORIGIN_MARGIN);
        // Scroll far right: (5000 + 400 + 256) / 8 = 707 columns.
        const grown = renderer.ensureViewForViewport(5000, 0, 400, 100);
        expect(grown).toBe(true);
        // View covers inset + storage + margin: bigger than 3x2 storage.
        expect(canvas.width).toBeGreaterThan(3 * 8);
        // Verticals run the full view height (viewRows * 16).
        const viewH = canvas.height;
        expect(record.lines).toContainEqual([8.5, 0, 8.5, viewH]);
    });

    it('draws room fills at the state inset', () => {
        const state = new CanvasState(10, 8, false);
        const { renderer, record } = makeRenderer(state);
        renderer.setViewOrigin(2, 3);
        renderer.setRoomCells(new Set(['1,1']));
        expect(record.fills).toContainEqual([(2 + 1) * 8, (3 + 1) * 16, 8, 16]);
    });

    it('draws the room union boundary at the state inset', () => {
        const state = new CanvasState(10, 8, false);
        const { renderer, record } = makeRenderer(state);
        renderer.setViewOrigin(2, 3);
        // Single room cell (0,0): top edge runs (0,0)->(1,0) in cell units.
        renderer.setRoomCells(new Set(['0,0']));
        expect(record.lines).toContainEqual([2 * 8, 3 * 16, 3 * 8, 3 * 16]);
    });

    it('updateState keeps the view but covers bigger storage', () => {
        const first = new CanvasState(4, 4, false);
        const { renderer, canvas } = makeRenderer(first);
        const before = canvas.width;
        renderer.updateState(new CanvasState(60, 60, false));
        // Grown to cover 60-wide storage plus margin — never shrunk back.
        expect(canvas.width).toBeGreaterThan(before);
        expect(canvas.width).toBe((60 + 32) * 8);
        renderer.updateState(new CanvasState(4, 4, false));
        expect(canvas.width).toBe((60 + 32) * 8);
    });

    it('ensureViewForViewport reports false when already covered', () => {
        const state = new CanvasState(4, 4, false);
        const { renderer } = makeRenderer(state);
        expect(renderer.ensureViewForViewport(0, 0, 10, 10)).toBe(true);
        expect(renderer.ensureViewForViewport(0, 0, 10, 10)).toBe(false);
    });
});
