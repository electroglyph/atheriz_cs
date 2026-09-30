import { describe, expect, it } from 'vitest';
import { BrushTool } from '../src/tools/BrushTool';
import { FillTool } from '../src/tools/FillTool';
import { MoveTool } from '../src/tools/MoveTool';
import { RectangleTool } from '../src/tools/RectangleTool';
import { CanvasState } from '../src/state/CanvasState';
import { UndoStack } from '../src/state/UndoStack';
import { GridRenderer } from '../src/canvas/GridRenderer';
import { ToolContext } from '../src/tools/Tool';
import { AppState } from '../src/types';

function makeAppState(overrides: Partial<AppState> = {}): AppState {
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
        ...overrides,
    };
}

function makeRenderer(selected: Set<string> = new Set()) {
    let sel = selected;
    return {
        getSelectedCells: () => sel,
        setSelection: (s: Set<string>) => { sel = s; },
        clearSelection: () => { sel = new Set(); },
        setPreview: () => {},
        clearPreview: () => {},
        getRoomCells: () => new Set<string>(),
        setRoomCells: () => {},
    } as unknown as GridRenderer;
}

function makeCtx(state: CanvasState, overrides: Partial<AppState> = {}, selected?: Set<string>): ToolContext {
    return {
        state,
        undoStack: new UndoStack(),
        renderer: makeRenderer(selected),
        appState: makeAppState(overrides),
        modifiers: { shiftKey: false, altKey: false, ctrlKey: false },
    };
}

describe('tool viewport growth past the violet line', () => {
    it('brush drag past the right edge grows storage and paints the whole stroke', () => {
        const state = new CanvasState(5, 5);
        const ctx = makeCtx(state);
        const tool = new BrushTool();
        tool.onMouseDown(ctx, { x: 0, y: 0 });
        tool.onDrag(ctx, { x: 0, y: 0 }, { x: 7, y: 0 });
        expect(state.width).toBeGreaterThan(5);
        // The whole stroke painted, including past the old edge.
        for (let x = 0; x <= 7; x++) expect(state.getCell(x, 0)!.char).toBe('x');
        // The violet rect expanded to cover the stroke.
        const b = state.serverBounds;
        expect(b.col + b.w).toBeGreaterThanOrEqual(8);
        expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
        // Two entries: the in-bounds dot, then the growth. Undo stays
        // consistent (first undo drops growth + drag paint, keeps the dot).
        expect(ctx.undoStack.depth).toBe(2);
        tool.onMouseUp(ctx, { x: 7, y: 0 });
    });

    it('brush starting out of bounds keeps one undo entry for growth plus paint', () => {
        const state = new CanvasState(5, 5);
        const ctx = makeCtx(state);
        const tool = new BrushTool();
        tool.onMouseDown(ctx, { x: 7, y: 0 });
        // Drag back inside the already-expanded grid: no further growth.
        tool.onDrag(ctx, { x: 7, y: 0 }, { x: 5, y: 0 });
        for (let x = 5; x <= 7; x++) expect(state.getCell(x, 0)!.char).toBe('x');
        expect(ctx.undoStack.depth).toBe(1);
        tool.onMouseUp(ctx, { x: 5, y: 0 });
    });

    it('brush click past the left edge inserts storage and preserves content', () => {
        const state = new CanvasState(5, 5);
        state.setCell(4, 4, { char: 'Q', fg: [255, 255, 255], bg: [0, 0, 0] });
        const ctx = makeCtx(state);
        new BrushTool().onMouseDown(ctx, { x: -2, y: 1 });
        expect(state.width).toBeGreaterThan(5);
        // Click landed at the remapped origin column; old content shifted right intact.
        expect(state.getCell(0, 1)!.char).toBe('x');
        expect(state.getCell(4 + 2, 4)!.char).toBe('Q');
        // The violet rect still covers the old content.
        const b = state.serverBounds;
        expect(b.col).toBeLessThanOrEqual(2);
        expect(b.col + b.w).toBeGreaterThanOrEqual(2 + 5);
        expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
    });

    it('move past the edge grows instead of dropping the cells', () => {
        const state = new CanvasState(5, 5);
        state.setCell(3, 2, { char: 'M', fg: [255, 255, 255], bg: [0, 0, 0] });
        const ctx = makeCtx(state, {}, new Set(['3,2']));
        const tool = new MoveTool();
        tool.onMouseDown(ctx, { x: 3, y: 2 });
        tool.onMouseUp(ctx, { x: 8, y: 2 });
        expect(state.width).toBeGreaterThan(5);
        expect(state.getCell(8, 2)!.char).toBe('M');
        expect(state.getCell(3, 2)!.char).toBe('');
        expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
        expect(ctx.undoStack.depth).toBe(1);
    });

    it('fill seed outside the violet line expands the grid then floods within', () => {
        // Transparent layers: opaque black counts as ink for the flood.
        const state = new CanvasState(8, 8, false);
        state.setServerBounds({ col: 2, row: 2, w: 4, h: 4 });
        const ctx = makeCtx(state);
        new FillTool().onMouseDown(ctx, { x: 0, y: 0 });
        // Bounds grew to include the seed; the seed cell itself filled.
        expect(state.serverBounds.col).toBeLessThanOrEqual(0);
        expect(state.serverBounds.row).toBeLessThanOrEqual(0);
        expect(state.getCell(0, 0)!.char).toBe('x');
        expect(ctx.undoStack.depth).toBe(1);
    });

    it('rectangle across the left edge lands in post-growth coords', () => {
        const state = new CanvasState(5, 5);
        const ctx = makeCtx(state);
        const tool = new RectangleTool();
        tool.onMouseDown(ctx, { x: 3, y: 3 });
        tool.onDrag(ctx, { x: 3, y: 3 }, { x: -2, y: 4 });
        tool.onMouseUp(ctx, { x: -2, y: 4 });
        expect(state.width).toBeGreaterThan(5);
        // Outline corners: remapped left edge and the original right edge.
        expect(state.getCell(0, 3)!.char).not.toBe('');
        expect(state.getCell(3 + 2, 3)!.char).not.toBe('');
        expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
        expect(ctx.undoStack.depth).toBe(1);
    });
});

describe('rectangle starting outside the violet grid', () => {
    it('expands the violet rect to the outline and tells the host (zero growth)', () => {
        const state = new CanvasState(20, 20);
        state.setServerBounds({ col: 8, row: 8, w: 4, h: 4 });
        const shifts: { addedLeft: number; addedTop: number; addedRight: number; addedBottom: number; capped: boolean }[] = [];
        const ctx = makeCtx(state);
        ctx.onViewportShifted = (g) => { shifts.push(g); };
        const depthBefore = ctx.undoStack.depth;
        const tool = new RectangleTool();
        // Anchor and release both sit inside storage but outside violet.
        tool.onMouseDown(ctx, { x: 2, y: 2 });
        tool.onMouseUp(ctx, { x: 6, y: 6 });
        const b = state.serverBounds;
        expect(b.col).toBeLessThanOrEqual(2);
        expect(b.row).toBeLessThanOrEqual(2);
        expect(b.col + b.w).toBeGreaterThanOrEqual(7);
        expect(b.row + b.h).toBeGreaterThanOrEqual(7);
        // The outline actually painted (top-left corner of the rect).
        expect(state.getCompositeCell(2, 2)!.char).not.toBe('');
        // One undo entry covers bounds growth plus paint; the host learns
        // the new grid rect through a zero-growth shift (renderer, session,
        // and origin all mirror from it).
        expect(ctx.undoStack.depth).toBe(depthBefore + 1);
        expect(shifts).toHaveLength(1);
        expect(shifts[0]).toMatchObject({
            addedLeft: 0, addedTop: 0, addedRight: 0, addedBottom: 0, capped: false,
        });
    });
});
