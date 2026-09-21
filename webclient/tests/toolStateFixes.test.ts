// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest';
import { ToolManager } from '../src/tools/ToolManager';
import { BrushTool } from '../src/tools/BrushTool';
import { EraserTool } from '../src/tools/EraserTool';
import { LineTool } from '../src/tools/LineTool';
import { RectangleTool } from '../src/tools/RectangleTool';
import { OvalTool } from '../src/tools/OvalTool';
import { SelectionTool } from '../src/tools/SelectionTool';
import { MoveTool } from '../src/tools/MoveTool';
import { RotateTool } from '../src/tools/RotateTool';
import { EyedropperTool } from '../src/tools/EyedropperTool';
import { CanvasState } from '../src/state/CanvasState';
import { UndoStack } from '../src/state/UndoStack';
import { GridRenderer } from '../src/canvas/GridRenderer';
import { CanvasController } from '../src/canvas/CanvasController';
import { beginNewCanvas } from '../src/canvas/newCanvas';
import { AnsiExporter, LAYER_BOUNDARY_MARKER } from '../src/export/AnsiExporter';
import { buildCompositeAnsiPreview } from '../src/export/AnsiPreview';
import { LIGHT_BOX } from '../src/utils/characters';
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

afterEach(() => {
  document.body.innerHTML = '';
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('T1 ToolManager warns once per unknown tool id', () => {
  it('logs console.warn exactly once for a repeated unknown id', () => {
    const state = new CanvasState(4, 4);
    const ctx = makeCtx(state, { activeToolId: 'mystery-tool' });
    const tm = new ToolManager(ctx);
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    tm.onMouseDown({ x: 0, y: 0 });
    tm.onMouseDown({ x: 0, y: 0 });
    tm.onDrag({ x: 0, y: 0 }, { x: 1, y: 1 });
    expect(warn).toHaveBeenCalledTimes(1);
    expect(warn.mock.calls[0][0]).toContain('mystery-tool');
  });

  it('does not warn for registered tools', () => {
    const state = new CanvasState(4, 4);
    const ctx = makeCtx(state, { activeToolId: 'brush' });
    const tm = new ToolManager(ctx);
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    tm.onMouseDown({ x: 0, y: 0 });
    expect(warn).not.toHaveBeenCalled();
    expect(state.getCell(0, 0)!.char).toBe('x');
  });
});

describe('T2 tools clip output to canvas bounds', () => {
  it('brush mousedown out of bounds writes no overflow and no undo', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state);
    new BrushTool().onMouseDown(ctx, { x: -1, y: -1 });
    expect(ctx.undoStack.canUndo()).toBe(false);
    expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
  });

  it('brush drag past the edge paints to the edge without overflow', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state);
    const tool = new BrushTool();
    tool.onMouseDown(ctx, { x: 0, y: 0 });
    tool.onDrag(ctx, { x: 0, y: 0 }, { x: 50, y: 0 });
    for (let x = 0; x < 5; x++) expect(state.getCell(x, 0)!.char).toBe('x');
    expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
  });

  it('eraser out of bounds writes no overflow and no undo', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state);
    const tool = new EraserTool();
    tool.onMouseDown(ctx, { x: -1, y: 0 });
    tool.onDrag(ctx, { x: 0, y: 0 }, { x: 99, y: 0 });
    expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
  });

  it('line commit clips out-of-bounds points instead of overflowCells', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state, { lineDiagonal: false });
    const tool = new LineTool();
    tool.onMouseDown(ctx, { x: 3, y: 3 });
    tool.onDrag(ctx, { x: 3, y: 3 }, { x: 50, y: 3 });
    tool.onMouseUp(ctx, { x: 50, y: 3 });
    // In-bounds part of the stroke landed; nothing escaped to overflow.
    expect(state.getCell(4, 3)!.char).toBe(LIGHT_BOX.h);
    expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
    expect(ctx.undoStack.canUndo()).toBe(true);
    tool.destroy(ctx);
  });

  it('line ignores out-of-bounds neighbors when choosing glyphs', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state, { lineDiagonal: false });
    const tool = new LineTool() as unknown as {
      buildCells(ctx: unknown, pts: { x: number; y: number }[]): { col: number; row: number; cell: { char: string } }[];
    };
    // (2,5) is off-canvas: without the in-bounds-only neighbor rule (2,4)
    // would see a southern neighbor and draw 'v'; it must draw 'h'.
    // (buildCells itself stays unclipped for preview/junction use; the
    // commit path clips before applyBatch.)
    const cells = tool.buildCells(ctx as never, [{ x: 2, y: 4 }, { x: 2, y: 5 }]);
    const inBounds = cells.find((c) => c.col === 2 && c.row === 4);
    expect(inBounds).toBeDefined();
    expect(inBounds!.cell.char).toBe(LIGHT_BOX.h);
  });

  it('rectangle commit clips to bounds', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state);
    const tool = new RectangleTool();
    tool.onMouseDown(ctx, { x: 3, y: 3 });
    tool.onDrag(ctx, { x: 3, y: 3 }, { x: 10, y: 10 });
    tool.onMouseUp(ctx, { x: 10, y: 10 });
    expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
    // The in-bounds corner of the rect perimeter landed on the canvas.
    expect(state.getCell(4, 3)!.char).not.toBe('');
  });

  it('oval commit clips to bounds', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state);
    const tool = new OvalTool();
    tool.onMouseDown(ctx, { x: 0, y: 0 });
    tool.onDrag(ctx, { x: 0, y: 0 }, { x: 10, y: 10 });
    tool.onMouseUp(ctx, { x: 10, y: 10 });
    expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
  });
});

describe('T3 line single-point preview matches commit', () => {
  it('buildCells and preview both draw horizontal for an isolated point', () => {
    const state = new CanvasState(5, 5);
    const previews: { col: number; row: number; cell: { char: string } }[] = [];
    const ctx = makeCtx(state, { lineDiagonal: false });
    (ctx.renderer as unknown as { setPreview: (c: typeof previews) => void }).setPreview = (c) => {
      previews.length = 0;
      previews.push(...c);
    };
    const tool = new LineTool();
    tool.onMouseDown(ctx, { x: 1, y: 1 });
    expect(previews).toHaveLength(1);
    // Choice pinned here: 'h' matches RectangleTool single-point behavior.
    expect(previews[0].cell.char).toBe(LIGHT_BOX.h);
    const cells = (tool as unknown as {
      buildCells(ctx: unknown, pts: { x: number; y: number }[]): { cell: { char: string } }[];
    }).buildCells(ctx as never, [{ x: 1, y: 1 }]);
    expect(cells).toHaveLength(1);
    expect(cells[0].cell.char).toBe(LIGHT_BOX.h);
    tool.destroy(ctx);
  });
});

describe('T4 line drag-release commits and ESC undoes the gesture', () => {
  it('mouseup commits the pending drag segment', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state, { lineDiagonal: false });
    ctx.undoStack.setCurrentState(state);
    const tool = new LineTool();
    tool.onMouseDown(ctx, { x: 0, y: 0 });
    tool.onDrag(ctx, { x: 0, y: 0 }, { x: 2, y: 0 });
    tool.onMouseUp(ctx, { x: 2, y: 0 });
    expect(state.getCell(1, 0)!.char).toBe(LIGHT_BOX.h);
    expect(ctx.undoStack.canUndo()).toBe(true);
    tool.destroy(ctx);
  });

  it('click-release without drag paints nothing', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state, { lineDiagonal: false });
    const tool = new LineTool();
    tool.onMouseDown(ctx, { x: 1, y: 1 });
    tool.onMouseUp(ctx, { x: 1, y: 1 });
    expect(ctx.undoStack.canUndo()).toBe(false);
    expect(state.getCell(1, 1)!.char).toBe('');
    tool.destroy(ctx);
  });

  it('ESC after a committed segment undoes the gesture once', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state, { lineDiagonal: false });
    ctx.undoStack.setCurrentState(state);
    const tool = new LineTool();
    tool.onMouseDown(ctx, { x: 0, y: 0 });
    tool.onMouseDown(ctx, { x: 2, y: 0 });
    expect(ctx.undoStack.canUndo()).toBe(true);
    expect(tool.onKeyDown(ctx, 'Escape')).toBe(true);
    expect(ctx.undoStack.canUndo()).toBe(false);
    expect(ctx.undoStack.canRedo()).toBe(true);
    tool.destroy(ctx);
  });

  it('all instances share one toast node and destroy() removes it', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state);
    const a = new LineTool();
    const b = new LineTool();
    a.onMouseDown(ctx, { x: 0, y: 0 });
    b.onMouseDown(ctx, { x: 1, y: 1 });
    expect(document.querySelectorAll('.line-tool-toast')).toHaveLength(1);
    a.destroy(ctx);
    // b still owns it.
    expect(document.querySelectorAll('.line-tool-toast')).toHaveLength(1);
    b.destroy(ctx);
    expect(document.querySelectorAll('.line-tool-toast')).toHaveLength(0);
  });
});

describe('T5 selection gesture and clipboard fixes', () => {
  it('a mid-gesture mode switch cannot crash the mouseup', () => {
    const state = new CanvasState(5, 5);
    for (let y = 0; y <= 2; y++) {
      for (let x = 0; x <= 2; x++) {
        state.setCell(x, y, { char: 'x', fg: [255, 255, 255], bg: [0, 0, 0] });
      }
    }
    const ctx = makeCtx(state, { selectMode: 'rectangle' });
    const tool = new SelectionTool();
    tool.onMouseDown(ctx, { x: 0, y: 0 });
    // Toolbar switches to lasso before the release: the captured rectangle
    // mode still applies, so no lassoPath dereference can throw.
    ctx.appState.selectMode = 'lasso';
    expect(() => tool.onMouseUp(ctx, { x: 2, y: 2 })).not.toThrow();
    expect(ctx.renderer.getSelectedCells().size).toBe(9);
    tool.destroy();
  });

  it('delete on an already-empty selection pushes no undo', () => {
    const state = new CanvasState(3, 3);
    const ctx = makeCtx(state, { selectMode: 'single' });
    const tool = new SelectionTool();
    tool.onMouseDown(ctx, { x: 1, y: 1 });
    expect(tool.onKeyDown(ctx, 'Delete')).toBe(false);
    expect(ctx.undoStack.canUndo()).toBe(false);
    tool.destroy();
  });

  it('delete on the background layer preserves opaque black bg', () => {
    const state = new CanvasState(3, 3);
    state.setCell(1, 1, { char: 'A', fg: [255, 255, 255], bg: [10, 20, 30] });
    const ctx = makeCtx(state, { selectMode: 'single' });
    const tool = new SelectionTool();
    tool.onMouseDown(ctx, { x: 1, y: 1 });
    expect(tool.onKeyDown(ctx, 'Delete')).toBe(true);
    const cell = state.getCell(1, 1)!;
    expect(cell.char).toBe('');
    expect(cell.bg).toEqual([0, 0, 0]);
    tool.destroy();
  });

  it('delete on an overlay writes transparent bg', () => {
    const state = new CanvasState(3, 3);
    state.addLayer('fg');
    state.setCell(1, 1, { char: 'A', fg: [255, 255, 255], bg: [5, 5, 5] });
    const ctx = makeCtx(state, { selectMode: 'single' });
    const tool = new SelectionTool();
    tool.onMouseDown(ctx, { x: 1, y: 1 });
    expect(tool.onKeyDown(ctx, 'Delete')).toBe(true);
    expect(state.getCell(1, 1)!.bg).toEqual([-1, -1, -1]);
    tool.destroy();
  });

  it('paste reuses the Pasted layer and selects the pasted content', () => {
    const state = new CanvasState(5, 5);
    state.setCell(0, 0, { char: 'P', fg: [255, 255, 255], bg: [0, 0, 0] });
    const ctx = makeCtx(state, { selectMode: 'single' });
    const tool = new SelectionTool();
    tool.onMouseDown(ctx, { x: 0, y: 0 });
    expect(tool.onKeyDown(ctx, 'ctrl+c')).toBe(true);
    expect(tool.onKeyDown(ctx, 'ctrl+v')).toBe(true);
    expect(state.layers.length).toBe(2);
    expect(state.layers[1].name).toBe('Pasted');
    expect(tool.onKeyDown(ctx, 'ctrl+v')).toBe(true);
    // No layer spam: the second paste reuses the top Pasted layer.
    expect(state.layers.length).toBe(2);
    // Pasted content stays selected instead of cleared.
    expect(tool.hasSelection).toBe(true);
    expect(ctx.renderer.getSelectedCells().has('0,0')).toBe(true);
    tool.destroy();
  });
});

describe('T6 move/rotate noop guards and clipping', () => {
  it('whole-layer move on an empty layer pushes no undo', () => {
    const state = new CanvasState(5, 5);
    const ctx = makeCtx(state);
    const tool = new MoveTool();
    tool.onMouseDown(ctx, { x: 0, y: 0 });
    tool.onMouseUp(ctx, { x: 2, y: 0 });
    expect(ctx.undoStack.canUndo()).toBe(false);
  });

  it('move clips out-of-bounds destinations instead of overflowCells', () => {
    const state = new CanvasState(5, 5);
    state.addLayer('fg');
    state.setCell(4, 0, { char: 'A', fg: [255, 255, 255], bg: [0, 0, 0] });
    const selected = new Set(['4,0']);
    const ctx = makeCtx(state, {}, selected);
    const tool = new MoveTool();
    tool.onMouseDown(ctx, { x: 4, y: 0 });
    tool.onMouseUp(ctx, { x: 7, y: 0 });
    expect(state.getCell(4, 0)!.char).toBe('');
    expect(state.getActiveLayer().overflowCells?.size ?? 0).toBe(0);
  });

  it('move of only empty cells pushes no undo and reports no moves', () => {
    const state = new CanvasState(5, 5);
    state.addLayer('fg');
    const moved: unknown[] = [];
    const ctx = makeCtx(state, {}, new Set(['1,1']));
    ctx.onCellsMoved = (m) => moved.push(...m);
    const tool = new MoveTool();
    tool.onMouseDown(ctx, { x: 1, y: 1 });
    tool.onMouseUp(ctx, { x: 3, y: 1 });
    expect(ctx.undoStack.canUndo()).toBe(false);
    expect(moved).toHaveLength(0);
  });

  it('free rotate click on the center pushes no undo (atan2 guard + epsilon)', () => {
    const state = new CanvasState(6, 6);
    state.setCell(2, 2, { char: 'A', fg: [255, 255, 255], bg: [0, 0, 0] });
    const ctx = makeCtx(state, { rotateMode: 'free' }, new Set(['2,2']));
    const tool = new RotateTool();
    tool.onMouseDown(ctx, { x: 2, y: 2 });
    tool.onMouseUp(ctx, { x: 2, y: 2 });
    expect(ctx.undoStack.canUndo()).toBe(false);
    expect(state.getCell(2, 2)!.char).toBe('A');
  });

  it('discrete rotate of an all-empty selection pushes no undo', () => {
    const state = new CanvasState(4, 4);
    state.addLayer('fg');
    const ctx = makeCtx(state, { rotateMode: 'cw90' }, new Set(['0,0']));
    new RotateTool().applyTransform(ctx, 'cw90');
    expect(ctx.undoStack.canUndo()).toBe(false);
  });
});

describe('T7 eyedropper reads the active layer for transparency', () => {
  function pickAt(state: CanvasState, target: AppState['eyedropperTarget'], x: number, y: number) {
    const ctx = makeCtx(state, { eyedropperTarget: target });
    let detail: { isFg: boolean; color: number[] } | null = null;
    const listener = (e: Event) => {
      detail = (e as CustomEvent).detail;
    };
    window.addEventListener('colorPicked', listener);
    try {
      new EyedropperTool().onMouseDown(ctx, { x, y });
    } finally {
      window.removeEventListener('colorPicked', listener);
    }
    return detail;
  }

  it('picks black for a transparent active-layer cell over bg content', () => {
    const state = new CanvasState(3, 3);
    state.setCell(0, 0, { char: 'B', fg: [9, 9, 9], bg: [10, 20, 30] });
    state.addLayer('fg'); // active overlay cell (0,0) stays transparent
    // Composite bg is opaque here, so only an active-layer read detects it.
    expect(state.getCompositeCell(0, 0)!.bg).toEqual([10, 20, 30]);
    const detail = pickAt(state, 'bg-bg', 0, 0);
    expect(detail).not.toBeNull();
    expect(detail!.isFg).toBe(false);
    expect(detail!.color).toEqual([0, 0, 0]);
  });

  it('picks the overlay bg when the active layer is opaque', () => {
    const state = new CanvasState(3, 3);
    state.addLayer('fg');
    state.setCell(1, 1, { char: 'Z', fg: [255, 0, 0], bg: [1, 2, 3] });
    const detail = pickAt(state, 'bg-bg', 1, 1);
    expect(detail!.color).toEqual([1, 2, 3]);
  });

  it('still picks composite fg for fg targets', () => {
    const state = new CanvasState(3, 3);
    state.setCell(0, 0, { char: 'B', fg: [9, 9, 9], bg: [10, 20, 30] });
    state.addLayer('fg');
    const detail = pickAt(state, 'fg-fg', 0, 0);
    expect(detail!.isFg).toBe(true);
    expect(detail!.color).toEqual([9, 9, 9]);
  });
});

describe('T8 exporter guards and newline convention', () => {
  it('export of a layer-less canvas returns a reset instead of throwing', () => {
    const state = new CanvasState(2, 2);
    state.layers = [];
    expect(() => AnsiExporter.export(state)).not.toThrow();
    expect(AnsiExporter.export(state)).toBe('\x1b[0m');
  });

  it('composite preview of a layer-less canvas returns a reset', () => {
    const state = new CanvasState(2, 2);
    state.layers = [];
    expect(buildCompositeAnsiPreview(state)).toBe('\x1b[0m');
  });

  it('an empty overlay emits no boundary marker', () => {
    const state = new CanvasState(3, 1);
    state.addLayer('empty-overlay');
    const out = AnsiExporter.export(state);
    expect(out).not.toContain(LAYER_BOUNDARY_MARKER);
  });

  it('both outputs end with a bare reset and no trailing newline', () => {
    const state = new CanvasState(2, 1);
    state.setCell(0, 0, { char: 'X', fg: [255, 0, 0], bg: [0, 0, 0] });
    const exported = AnsiExporter.export(state);
    expect(exported.endsWith('\x1b[0m')).toBe(true);
    expect(exported.endsWith('\n')).toBe(false);
    const preview = buildCompositeAnsiPreview(state);
    expect(preview.endsWith('\x1b[0m')).toBe(true);
    expect(preview.endsWith('\n')).toBe(false);
  });
});

describe('T9 UndoStack range validation and reset', () => {
  function makeStack(): { stack: UndoStack; state: CanvasState } {
    const state = new CanvasState(2, 2);
    const stack = new UndoStack();
    stack.setCurrentState(state);
    return { stack, state };
  }

  it('undoTo out-of-range returns null and changes nothing', () => {
    const { stack } = makeStack();
    expect(stack.undoTo(-1)).toBeNull();
    expect(stack.undoTo(1)).toBeNull();
    expect(stack.depth).toBe(0);
  });

  it('undoTo beyond depth returns null and changes nothing', () => {
    const { stack, state } = makeStack();
    stack.push(state);
    expect(stack.depth).toBe(1);
    expect(stack.undoTo(99)).toBeNull();
    expect(stack.depth).toBe(1);
    expect(stack.canUndo()).toBe(true);
  });

  it('undoTo(depth) is a no-op returning the current state', () => {
    const { stack, state } = makeStack();
    stack.push(state);
    expect(stack.undoTo(1)).not.toBeNull();
    expect(stack.depth).toBe(1);
  });

  it('reset clears currentState so undo/redo return null', () => {
    const { stack, state } = makeStack();
    stack.push(state);
    stack.reset();
    expect(stack.canUndo()).toBe(false);
    expect(stack.canRedo()).toBe(false);
    expect(stack.undo()).toBeNull();
    expect(stack.redo()).toBeNull();
  });

  it('evicts past the 128-entry cap', () => {
    const { stack, state } = makeStack();
    for (let i = 0; i < 130; i++) stack.push(state);
    expect(stack.depth).toBe(128);
  });

  it('push clears the redo stack', () => {
    const { stack, state } = makeStack();
    stack.push(state);
    state.setCell(0, 0, { char: 'A', fg: [255, 255, 255], bg: [0, 0, 0] });
    stack.setCurrentState(state);
    expect(stack.undo()).not.toBeNull();
    expect(stack.canRedo()).toBe(true);
    stack.push(state);
    expect(stack.canRedo()).toBe(false);
  });
});

describe('T10 CanvasController input handling', () => {
  const metrics = { width: 10, height: 10, font: '10px monospace', advance: 10 };

  function makeDom() {
    const canvas = document.createElement('canvas');
    const bounds = { left: 0, top: 0, right: 100, bottom: 100, width: 100, height: 100 };
    vi.spyOn(canvas, 'getBoundingClientRect').mockReturnValue(bounds as unknown as DOMRect);
    return canvas;
  }

  function makeTmContext(state: CanvasState): ToolContext {
    const renderer = { setPreview: () => {}, clearPreview: () => {} } as unknown as GridRenderer;
    return { state, undoStack: new UndoStack(), renderer, appState: makeAppState(), modifiers: { shiftKey: false, altKey: false, ctrlKey: false } };
  }

  it('refreshes modifiers on mousedown', () => {
    const canvas = makeDom();
    const state = new CanvasState(10, 10);
    const ctx = makeTmContext(state);
    const tm = new ToolManager(ctx);
    const controller = new CanvasController(canvas, metrics, tm);
    canvas.dispatchEvent(new MouseEvent('mousedown', { button: 0, clientX: 55, clientY: 55, shiftKey: true }));
    expect(ctx.modifiers.shiftKey).toBe(true);
    controller.destroy();
  });

  it('ends the drag on a non-left mouseup', () => {
    const canvas = makeDom();
    const state = new CanvasState(10, 10);
    const tm = new ToolManager(makeTmContext(state));
    const controller = new CanvasController(canvas, metrics, tm);
    const mouseUp = vi.spyOn(tm, 'onMouseUp');
    canvas.dispatchEvent(new MouseEvent('mousedown', { button: 0, clientX: 55, clientY: 55 }));
    window.dispatchEvent(new MouseEvent('mouseup', { button: 2, clientX: 55, clientY: 55 }));
    expect(mouseUp).toHaveBeenCalledTimes(1);
    controller.destroy();
  });

  it('ignores Enter but forwards Delete', () => {
    const canvas = makeDom();
    const state = new CanvasState(10, 10);
    const tm = new ToolManager(makeTmContext(state));
    const controller = new CanvasController(canvas, metrics, tm);
    const keyDown = vi.spyOn(tm, 'onKeyDown');
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(keyDown).not.toHaveBeenCalled();
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete' }));
    expect(keyDown).toHaveBeenCalledWith('Delete');
    controller.destroy();
  });

  it('forwards ctrl+x and ctrl+shift variants to tools', () => {
    const canvas = makeDom();
    const state = new CanvasState(10, 10);
    const tm = new ToolManager(makeTmContext(state));
    const controller = new CanvasController(canvas, metrics, tm);
    const keyDown = vi.spyOn(tm, 'onKeyDown');
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'x', ctrlKey: true }));
    expect(keyDown).toHaveBeenCalledWith('ctrl+x');
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'C', ctrlKey: true, shiftKey: true }));
    expect(keyDown).toHaveBeenCalledWith('ctrl+c');
    controller.destroy();
  });
});

describe('T11 beginNewCanvas validation and preview reset', () => {
  function makeDeps() {
    const canvas = document.createElement('canvas');
    const previous = new CanvasState(8, 8);
    const renderer = new GridRenderer(canvas, previous, {
      width: 8,
      height: 16,
      font: '8px monospace',
      advance: 8,
    });
    const selection = new SelectionTool();
    const undoStack = new UndoStack();
    undoStack.setCurrentState(previous);
    const tools = { state: previous };
    const layers = { updateState: vi.fn() };
    return { deps: { undoStack, renderer, selection, layers, tools }, previous };
  }

  it.each([0, -1, 1.5, NaN, 3000])('throws RangeError for invalid size %s', (bad) => {
    const { deps, previous } = makeDeps();
    expect(() => beginNewCanvas(deps as never, bad as number, 8)).toThrow(RangeError);
    expect(() => beginNewCanvas(deps as never, 8, bad as number)).toThrow(RangeError);
    // Nothing mutated on validation failure.
    expect(deps.tools.state).toBe(previous);
    expect(deps.undoStack.canUndo()).toBe(false);
  });

  it('clears the renderer preview on reset', () => {
    const { deps } = makeDeps();
    deps.renderer.setPreview([{ col: 0, row: 0, cell: { char: 'x', fg: [255, 255, 255], bg: [0, 0, 0] } }]);
    const clearPreview = vi.spyOn(deps.renderer, 'clearPreview');
    beginNewCanvas(deps as never, 10, 6);
    expect(clearPreview).toHaveBeenCalled();
  });
});
