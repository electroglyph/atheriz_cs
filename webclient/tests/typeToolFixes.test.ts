// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest';
import { TypeTool } from '../src/tools/TypeTool';
import { CanvasState } from '../src/state/CanvasState';
import { UndoStack } from '../src/state/UndoStack';
import { GridRenderer } from '../src/canvas/GridRenderer';
import { ToolContext } from '../src/tools/Tool';
import { AppState } from '../src/types';

function makeAppState(overrides: Partial<AppState> = {}): AppState {
  return {
    activeToolId: 'type',
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

function makeRenderer() {
  return {
    setPreview: () => {},
    clearPreview: () => {},
  } as unknown as GridRenderer;
}

/** Deferred modal stub so tests can swap ctx.state before confirm. */
function stubModal(tool: TypeTool, text: string | null): void {
  (tool as unknown as { modal: { open: () => Promise<string | null> } }).modal = {
    open: () => Promise.resolve(text),
  };
}

function stubModalDeferred(tool: TypeTool): (text: string | null) => void {
  let resolve!: (v: string | null) => void;
  (tool as unknown as { modal: { open: () => Promise<string | null> } }).modal = {
    open: () => new Promise<string | null>((r) => { resolve = r; }),
  };
  return (text) => resolve(text);
}

function makeCtx(state: CanvasState): ToolContext {
  const undoStack = new UndoStack();
  undoStack.setCurrentState(state);
  return {
    state,
    undoStack,
    renderer: makeRenderer(),
    appState: makeAppState(),
    modifiers: { shiftKey: false, altKey: false, ctrlKey: false },
  };
}

function buildModalDom(): void {
  document.body.innerHTML = '';
  for (const [tag, id] of [['div', 'type-tool-modal'], ['input', 'type-tool-input'], ['button', 'type-tool-ok'], ['button', 'type-tool-cancel']] as const) {
    const e = document.createElement(tag);
    e.id = id;
    document.body.appendChild(e);
  }
}

afterEach(() => {
  document.body.innerHTML = '';
  vi.restoreAllMocks();
});

describe('W11 TypeTool confirm guards', () => {
  it('fully off-canvas typing pushes no undo entry and paints nothing', async () => {
    buildModalDom();
    const state = new CanvasState(4, 4);
    const ctx = makeCtx(state);
    const depthBefore = ctx.undoStack.depth;
    const tool = new TypeTool();
    stubModal(tool, 'hi');
    tool.onMouseDown(ctx, { x: 10, y: 0 });
    await Promise.resolve();
    await Promise.resolve();
    expect(ctx.undoStack.depth).toBe(depthBefore);
    expect(state.getCompositeCell(0, 0)?.char ?? '').toBe('');
  });

  it('clips negative columns and out-of-range rows instead of overflowCells', async () => {
    buildModalDom();
    const state = new CanvasState(4, 4);
    const ctx = makeCtx(state);
    const tool = new TypeTool();
    stubModal(tool, 'ab');
    tool.onMouseDown(ctx, { x: 0, y: 99 });
    await Promise.resolve();
    await Promise.resolve();
    const layer = state.getActiveLayer();
    expect(layer.overflowCells?.size ?? 0).toBe(0);
  });

  it('paints onto the live canvas when state is swapped before confirm', async () => {
    buildModalDom();
    const oldState = new CanvasState(4, 4);
    const ctx = makeCtx(oldState);
    const tool = new TypeTool();
    const confirm = stubModalDeferred(tool);
    tool.onMouseDown(ctx, { x: 0, y: 0 });
    const liveState = new CanvasState(4, 4);
    ctx.state = liveState;
    ctx.undoStack.setCurrentState(liveState);
    confirm('Z');
    await Promise.resolve();
    await Promise.resolve();
    expect(liveState.getCell(0, 0)?.char).toBe('Z');
    expect(oldState.getCell(0, 0)?.char ?? '').not.toBe('Z');
  });

  it('keeps surrogate pairs intact instead of splitting UTF-16 halves', async () => {
    buildModalDom();
    const state = new CanvasState(4, 4);
    const ctx = makeCtx(state);
    const tool = new TypeTool();
    stubModal(tool, '😀');
    tool.onMouseDown(ctx, { x: 0, y: 0 });
    await Promise.resolve();
    await Promise.resolve();
    expect(state.getCell(0, 0)?.char).toBe('😀');
    expect(state.getCell(1, 0)?.char ?? '').toBe('');
  });
});
