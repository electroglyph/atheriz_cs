// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest';

vi.mock('../src/utils/TextToANSI', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../src/utils/TextToANSI')>();
  return { ...actual, renderTextToAnsiLayer: vi.fn() };
});

import { renderTextToAnsiLayer } from '../src/utils/TextToANSI';
import { TextToolDialog } from '../src/ui/TextToolDialog';
import { CanvasState } from '../src/state/CanvasState';
import { UndoStack } from '../src/state/UndoStack';
import { AppState } from '../src/types';

const renderMock = renderTextToAnsiLayer as unknown as Mock;
const CELL = { width: 9.6, height: 19, font: '16px "Fira Code"', advance: 9.6 };

vi.stubGlobal('IntersectionObserver', class {
  observe() {}
  unobserve() {}
  disconnect() {}
});

function el(tag: string, id: string): HTMLElement {
  const e = document.createElement(tag);
  e.id = id;
  document.body.appendChild(e);
  return e;
}

function buildDialogDom(): Record<string, HTMLElement> {
  document.body.innerHTML = '';
  const ids: [string, string][] = [
    ['div', 'text-tool-modal'],
    ['textarea', 'text-tool-input'],
    ['select', 'text-tool-font'],
    ['select', 'text-tool-style'],
    ['input', 'text-tool-max-width'],
    ['span', 'text-tool-max-width-val'],
    ['input', 'text-tool-stretch'],
    ['span', 'text-tool-stretch-val'],
    ['canvas', 'text-tool-preview'],
    ['button', 'btn-text-cancel'],
    ['button', 'btn-text-confirm'],
    ['button', 'text-tool-google-fonts-btn'],
    ['select', 'text-tool-align'],
    ['div', 'text-chafa-options-container'],
    ['div', 'google-font-picker-modal'],
    ['input', 'gfp-search'],
    ['div', 'gfp-list'],
    ['div', 'gfp-tabs'],
    ['button', 'gfp-cancel'],
    ['button', 'gfp-ok'],
    ['div', 'gfp-sentinel'],
  ];
  const out: Record<string, HTMLElement> = {};
  for (const [tag, id] of ids) out[id] = el(tag, id);
  return out;
}

function makeAppState(): AppState {
  return {
    activeToolId: 'text',
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

beforeEach(() => {
  vi.clearAllMocks();
});

describe('text confirm applies to the live canvas, never a replaced one', () => {
  it('new map during conversion: old map stays clean, new map gets the text', async () => {
    const dom = buildDialogDom();
    const oldState = new CanvasState(60, 20);
    oldState.setCell(5, 5, { char: 'O', fg: [255, 255, 255], bg: [0, 0, 0] });
    const undo = new UndoStack();
    undo.setCurrentState(oldState);

    // The dialog never stores a canvas: it reads the owner's live binding
    // through the getter, so no sync call exists to forget.
    let live: CanvasState = oldState;
    let confirmed: CanvasState | null = null;
    const dialog = new TextToolDialog(
      makeAppState(),
      () => live,
      (s) => { confirmed = s; },
      () => CELL,
      undo,
    );

    (dom['text-tool-input'] as HTMLTextAreaElement).value = 'new text';
    (dom['text-tool-max-width'] as HTMLInputElement).value = '60';

    let resolveRender!: (v: unknown) => void;
    renderMock.mockImplementation(() => new Promise((res) => { resolveRender = res as (v: unknown) => void; }));
    (dom['btn-text-confirm'] as HTMLButtonElement).click();
    // Let the async handler suspend on the pending conversion.
    await new Promise((r) => setTimeout(r, 0));

    // New map lands while chafa is still converting.
    const fresh = new CanvasState(60, 20);
    undo.push(oldState);
    undo.setCurrentState(fresh);
    live = fresh;

    resolveRender({
      label: 'Text: new text',
      cells: [{ char: 'N', fg: [255, 255, 255], bg: [-1, -1, -1] }],
      cols: 1,
      rows: 1,
    });
    await vi.waitFor(() => expect(confirmed).not.toBeNull());

    // The live (fresh) map receives the text...
    expect(confirmed).toBe(fresh);
    expect(fresh.layers).toHaveLength(2);
    expect(fresh.getActiveLayer().cells[9][29].char).toBe('N');
    // ...and the discarded map is NOT resurrected: no text layer, marker intact.
    expect(oldState.layers).toHaveLength(1);
    expect(oldState.getActiveLayer().cells[5][5].char).toBe('O');
    // Undo steps back over the render on the fresh map.
    expect(undo.canUndo()).toBe(true);
    expect(undo.undo()!.layers).toHaveLength(1);
  });

  it('new map before render: walls from the discarded map never come back', async () => {
    const dom = buildDialogDom();
    const walls = new CanvasState(60, 20);
    walls.setCell(0, 0, { char: '#', fg: [255, 255, 255], bg: [0, 0, 0] });
    walls.setCell(59, 0, { char: '#', fg: [255, 255, 255], bg: [0, 0, 0] });
    const undo = new UndoStack();
    undo.setCurrentState(walls);

    let live: CanvasState = walls;
    let confirmed: CanvasState | null = null;
    const dialog = new TextToolDialog(
      makeAppState(),
      () => live,
      (s) => { confirmed = s; },
      () => CELL,
      undo,
    );

    // New: blank map replaces the walled one; the dialog gets NO sync call
    // (there is nothing to call anymore) and must still resolve the blank.
    live = new CanvasState(60, 20);
    undo.push(walls);
    undo.setCurrentState(live);

    (dom['text-tool-input'] as HTMLTextAreaElement).value = 'hi';
    (dom['text-tool-max-width'] as HTMLInputElement).value = '60';
    renderMock.mockResolvedValue({
      label: 'Text: hi',
      cells: [{ char: 'H', fg: [255, 255, 255], bg: [-1, -1, -1] }],
      cols: 1,
      rows: 1,
    });
    (dom['btn-text-confirm'] as HTMLButtonElement).click();
    await vi.waitFor(() => expect(confirmed).not.toBeNull());

    expect(confirmed).toBe(live);
    expect(live.getActiveLayer().cells[9][29].char).toBe('H');
    expect(live.getActiveLayer().cells[0][0].char).toBe('');
    expect(live.getActiveLayer().cells[0][59].char).toBe('');
    expect(walls.layers).toHaveLength(1);
  });
});
