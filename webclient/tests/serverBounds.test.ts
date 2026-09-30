import { describe, it, expect } from 'vitest';
import { CanvasState } from '../src/state/CanvasState';
import { UndoStack } from '../src/state/UndoStack';
import { ToolContext } from '../src/tools/Tool';
import { AppState, ViewportGrowth } from '../src/types';
import { GridRenderer } from '../src/canvas/GridRenderer';
import { ensureToolCapacity, originDeltaForGrowth, planCapacityFor } from '../src/canvas/ensureCapacity';

function makeAppState(): AppState {
    return {
        activeToolId: 'brush',
        rectMode: 'light',
        ovalMode: 'light',
        lineMode: 'light',
        gradientTarget: 'foreground',
        typeStyle: 'regular',
        selectedChar: 'x',
        fgColor: [255, 255, 255],
        bgColor: [0, 0, 0],
        fontFamily: 'monospace',
        gradientStops: [],
        selectMode: 'single',
        rotateMode: 'cw90',
        fillMode: 'brush',
        lineDiagonal: false,
        eyedropperTarget: 'fg-fg',
    };
}

function makeCtx(state: CanvasState, onShift?: (g: ViewportGrowth) => void): { ctx: ToolContext; undoStack: UndoStack } {
    const undoStack = new UndoStack();
    undoStack.setCurrentState(state);
    const renderer = { setPreview: () => {}, clearPreview: () => {} } as unknown as GridRenderer;
    const ctx: ToolContext = {
        state,
        undoStack,
        renderer,
        appState: makeAppState(),
        modifiers: { shiftKey: false, altKey: false, ctrlKey: false },
    };
    if (onShift) ctx.onViewportShifted = onShift;
    return { ctx, undoStack };
}

describe('CanvasState serverBounds', () => {
    it('defaults to the full canvas', () => {
        const state = new CanvasState(24, 10);
        expect(state.serverBounds).toEqual({ col: 0, row: 0, w: 24, h: 10 });
    });

    it('clone deep-copies the bounds', () => {
        const state = new CanvasState(10, 10);
        state.setServerBounds({ col: 2, row: 3, w: 4, h: 5 });
        const copy = state.clone();
        expect(copy.serverBounds).toEqual({ col: 2, row: 3, w: 4, h: 5 });
        copy.serverBounds.col = 9;
        expect(state.serverBounds.col).toBe(2);
    });

    it('setServerBounds sanitizes origin, size, and storage fit', () => {
        const state = new CanvasState(10, 8);
        state.setServerBounds({ col: -3.7, row: 2.2, w: 4.9, h: 100 });
        // Floored origin clamped at 0; size floored then contained in storage.
        expect(state.serverBounds).toEqual({ col: 0, row: 2, w: 4, h: 6 });
        state.setServerBounds({ col: 8, row: 7, w: 10, h: 10 });
        expect(state.serverBounds).toEqual({ col: 8, row: 7, w: 2, h: 1 });
    });

    it('setServerBounds throws on non-finite input', () => {
        const state = new CanvasState(10, 10);
        expect(() => state.setServerBounds({ col: NaN, row: 0, w: 4, h: 4 })).toThrow(RangeError);
        expect(() => state.setServerBounds({ col: 0, row: 0, w: Infinity, h: 4 })).toThrow(RangeError);
    });

    it('resize growth leaves bounds alone; shrink clamps bounds into storage', () => {
        const state = new CanvasState(10, 10);
        state.setServerBounds({ col: 2, row: 2, w: 4, h: 4 });
        state.resize(20, 20);
        expect(state.serverBounds).toEqual({ col: 2, row: 2, w: 4, h: 4 });
        state.resize(3, 3);
        expect(state.serverBounds).toEqual({ col: 2, row: 2, w: 1, h: 1 });
    });

    it('ensureBoundsFor expands and reports, and is a no-op inside', () => {
        const state = new CanvasState(10, 10);
        state.setServerBounds({ col: 2, row: 2, w: 2, h: 2 });
        expect(state.ensureBoundsFor(3, 3)).toBe(false);
        expect(state.ensureBoundsFor(6, 1)).toBe(true);
        expect(state.serverBounds).toEqual({ col: 2, row: 1, w: 5, h: 3 });
        // Clamped to storage, never beyond it.
        expect(state.ensureBoundsFor(50, 50)).toBe(true);
        expect(state.serverBounds).toEqual({ col: 2, row: 1, w: 8, h: 9 });
    });
});

describe('CanvasState.ensureViewportFor', () => {
    it('is a no-op for inside points', () => {
        const state = new CanvasState(10, 10);
        state.setServerBounds({ col: 1, row: 1, w: 2, h: 2 });
        const g = state.ensureViewportFor(5, 5);
        expect(g).toMatchObject({ addedLeft: 0, addedTop: 0, addedRight: 0, addedBottom: 0, capped: false });
        expect(state.width).toBe(10);
        expect(state.serverBounds).toEqual({ col: 1, row: 1, w: 2, h: 2 });
    });

    it('appends storage on the right and bottom, keeping cells at their indices', () => {
        const state = new CanvasState(4, 4);
        state.setCell(3, 3, { char: 'x', fg: [1, 2, 3], bg: [0, 0, 0] });
        const g = state.ensureViewportFor(6, 7);
        expect(g.addedRight).toBeGreaterThanOrEqual(3);
        expect(g.addedBottom).toBeGreaterThanOrEqual(4);
        expect(g.capped).toBe(false);
        expect(state.width).toBe(4 + g.addedRight);
        expect(state.height).toBe(4 + g.addedBottom);
        expect(state.getCell(3, 3)?.char).toBe('x');
        expect(g.col).toBe(6);
        expect(g.row).toBe(7);
        // Bounds untouched by storage growth.
        expect(state.serverBounds).toEqual({ col: 0, row: 0, w: 4, h: 4 });
    });

    it('inserts storage at the left/top, shifting content and bounds', () => {
        const state = new CanvasState(4, 4);
        state.setServerBounds({ col: 1, row: 1, w: 2, h: 2 });
        state.setCell(0, 0, { char: 'o', fg: [1, 2, 3], bg: [0, 0, 0] });
        const g = state.ensureViewportFor(-2, -3);
        expect(g.addedLeft).toBe(2);
        expect(g.addedTop).toBe(3);
        expect(g.capped).toBe(false);
        expect(state.width).toBe(6);
        expect(state.height).toBe(7);
        // Content shifted right/down by the insert amounts.
        expect(state.getCell(2, 3)?.char).toBe('o');
        expect(g.col).toBe(0);
        expect(g.row).toBe(0);
        expect(state.serverBounds).toEqual({ col: 3, row: 4, w: 2, h: 2 });
    });

    it('caps growth at MAX_DIMENSION and reports capped points', () => {
        const state = new CanvasState(2048, 2048);
        const g = state.ensureViewportFor(3000, -5);
        expect(state.width).toBe(2048);
        expect(state.height).toBe(2048);
        expect(g.capped).toBe(true);
    });
});

describe('originDeltaForGrowth', () => {
    it('encodes the section-7 rebase rules', () => {
        // Right append: nothing.
        expect(originDeltaForGrowth({ addedLeft: 0, addedTop: 0, addedRight: 5, addedBottom: 0 }))
            .toEqual({ dx: 0, dy: 0 });
        // Bottom append: originY -= k.
        expect(originDeltaForGrowth({ addedLeft: 0, addedTop: 0, addedRight: 0, addedBottom: 5 }))
            .toEqual({ dx: 0, dy: -5 });
        // Left insert: originX -= k.
        expect(originDeltaForGrowth({ addedLeft: 3, addedTop: 0, addedRight: 0, addedBottom: 0 }))
            .toEqual({ dx: -3, dy: 0 });
        // Top insert: self-stable, no rebase.
        expect(originDeltaForGrowth({ addedLeft: 0, addedTop: 4, addedRight: 0, addedBottom: 0 }))
            .toEqual({ dx: 0, dy: 0 });
    });
});

describe('world-coord stability across growth', () => {
    // world x = col + originX, y = H-1-row + originY.
    const worldOf = (col: number, row: number, h: number, ox: number, oy: number) =>
        ({ x: col + ox, y: h - 1 - row + oy });

    it('keeps every existing cell stable in all four directions', () => {
        for (const point of [{ col: 9, row: 2 }, { col: 2, row: 9 }, { col: -2, row: 2 }, { col: 2, row: -2 }]) {
            const state = new CanvasState(8, 8);
            state.setCell(3, 3, { char: 'r', fg: [1, 2, 3], bg: [0, 0, 0] });
            let ox = 100;
            let oy = -50;
            const before = worldOf(3, 3, state.height, ox, oy);
            const g = state.ensureViewportFor(point.col, point.row);
            const d = originDeltaForGrowth(g);
            ox += d.dx;
            oy += d.dy;
            const afterCol = 3 + g.addedLeft;
            const afterRow = 3 + g.addedTop;
            expect(worldOf(afterCol, afterRow, state.height, ox, oy)).toEqual(before);
            expect(state.getCell(afterCol, afterRow)?.char).toBe('r');
        }
    });
});

describe('ensureToolCapacity', () => {
    it('does nothing (no undo, no hook) when all points are covered', () => {
        const state = new CanvasState(10, 10);
        state.setServerBounds({ col: 0, row: 0, w: 10, h: 10 });
        let shifts = 0;
        const { ctx, undoStack } = makeCtx(state, () => { shifts++; });
        const res = ensureToolCapacity(ctx, [{ col: 1, row: 1 }, { col: 9, row: 9 }]);
        expect(res.pushed).toBe(false);
        expect(res.dropped).toEqual(new Set());
        expect(res.points).toEqual([{ col: 1, row: 1 }, { col: 9, row: 9 }]);
        expect(undoStack.canUndo()).toBe(false);
        expect(shifts).toBe(0);
    });

    it('pushes one undo entry when only bounds expand, without growing storage', () => {
        const state = new CanvasState(10, 10);
        state.setServerBounds({ col: 0, row: 0, w: 2, h: 2 });
        let shifts = 0;
        const { ctx, undoStack } = makeCtx(state, () => { shifts++; });
        const res = ensureToolCapacity(ctx, [{ col: 5, row: 5 }]);
        expect(res.pushed).toBe(true);
        expect(undoStack.depth).toBe(1);
        expect(state.width).toBe(10);
        expect(state.serverBounds).toEqual({ col: 0, row: 0, w: 6, h: 6 });
        // Storage did not grow, but the host still hears about the new
        // grid rect (zero growth): renderer, session, and origin mirror
        // from it, otherwise the violet outline and the save diff go stale.
        expect(shifts).toBe(1);
        // Undo reverts the bounds expansion.
        undoStack.setCurrentState(state);
        const restored = undoStack.undo()!;
        expect(restored.serverBounds).toEqual({ col: 0, row: 0, w: 2, h: 2 });
    });

    it('grows storage once for multi-point strokes, then publishes the grown grid', () => {
        const state = new CanvasState(4, 4);
        const shifts: ViewportGrowth[] = [];
        const { ctx, undoStack } = makeCtx(state, g => { shifts.push(g); });
        const res = ensureToolCapacity(ctx, [{ col: -2, row: 1 }, { col: 6, row: 2 }]);
        expect(res.pushed).toBe(true);
        expect(undoStack.depth).toBe(1);
        // Two calls: the growth record first (overlays shift from it),
        // then a zero-growth publish carrying the expanded bounds. The
        // publish is what keeps the violet outline and the save diff from
        // going stale one step behind every combined growth.
        expect(shifts.length).toBe(2);
        expect(state.width).toBeGreaterThanOrEqual(7);
        // Points remapped into post-growth storage.
        expect(res.points).toEqual([
            { col: -2 + shifts[0].addedLeft, row: 1 + shifts[0].addedTop },
            { col: 6 + shifts[0].addedLeft, row: 2 + shifts[0].addedTop },
        ]);
        expect(res.dropped).toEqual(new Set());
        expect(shifts[1]).toMatchObject({
            addedLeft: 0, addedTop: 0, addedRight: 0, addedBottom: 0, capped: false,
        });
        // Bounds followed the grown points.
        const b = state.serverBounds;
        for (const p of res.points) {
            expect(p.col).toBeGreaterThanOrEqual(b.col);
            expect(p.row).toBeGreaterThanOrEqual(b.row);
            expect(p.col).toBeLessThan(b.col + b.w);
            expect(p.row).toBeLessThan(b.row + b.h);
        }
    });

    it('works without a host hook (bare unit-test contexts)', () => {
        const state = new CanvasState(4, 4);
        const { ctx } = makeCtx(state);
        const res = ensureToolCapacity(ctx, [{ col: -1, row: -1 }]);
        expect(res.pushed).toBe(true);
        expect(state.width).toBe(5);
        expect(state.height).toBe(5);
        expect(res.points).toEqual([{ col: 0, row: 0 }]);
    });

    it('drops points past the 2048 cap', () => {
        const state = new CanvasState(2048, 8);
        const { ctx } = makeCtx(state);
        const res = ensureToolCapacity(ctx, [{ col: 5000, row: 2 }]);
        expect(res.dropped).toEqual(new Set(['5000,2']));
        expect(res.points).toEqual([]);
    });

    it('planCapacityFor covers the full point range in one plan', () => {
        const planned = planCapacityFor(4, 4, [{ col: -2, row: 1 }, { col: 6, row: 9 }]);
        expect(planned.growth.addedLeft).toBe(2);
        expect(planned.growth.addedTop).toBe(0);
        expect(planned.growth.addedRight).toBeGreaterThanOrEqual(3);
        expect(planned.growth.addedBottom).toBeGreaterThanOrEqual(6);
        expect(planned.dropped).toEqual([]);
        expect(planned.surviving).toEqual([
            { col: 0, row: 1 },
            { col: 8, row: 9 },
        ]);
    });
});

describe('bounds-only expansion propagation', () => {
    it('ensureBoundsForRect notifies on change and stays silent when contained', () => {
        const state = new CanvasState(10, 10);
        state.setServerBounds({ col: 4, row: 4, w: 2, h: 2 });
        let fires = 0;
        state.onChange(() => { fires++; });
        expect(state.ensureBoundsForRect({ col: 1, row: 1, w: 2, h: 2 })).toBe(true);
        expect(state.serverBounds).toEqual({ col: 1, row: 1, w: 5, h: 5 });
        expect(fires).toBe(1);
        expect(state.ensureBoundsForRect({ col: 2, row: 2, w: 2, h: 2 })).toBe(false);
        expect(fires).toBe(1);
    });

    it('ensureToolCapacity reports bounds-only expansion to the host with zero growth', () => {
        const state = new CanvasState(10, 10);
        state.setServerBounds({ col: 4, row: 4, w: 2, h: 2 });
        const shifts: ViewportGrowth[] = [];
        const { ctx, undoStack } = makeCtx(state, (g) => { shifts.push(g); });
        const depthBefore = undoStack.depth;
        const cap = ensureToolCapacity(ctx, [{ col: 1, row: 1 }]);
        expect(cap.points).toEqual([{ col: 1, row: 1 }]);
        expect(cap.pushed).toBe(true);
        expect(state.serverBounds).toEqual({ col: 1, row: 1, w: 5, h: 5 });
        expect(undoStack.depth).toBe(depthBefore + 1);
        expect(shifts).toHaveLength(1);
        expect(shifts[0]).toMatchObject({
            addedLeft: 0, addedTop: 0, addedRight: 0, addedBottom: 0, capped: false,
        });
    });
});
