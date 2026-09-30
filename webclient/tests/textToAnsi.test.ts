import { describe, it, expect } from 'vitest';
import { previewFontString, calculateGrid, buildTextBatch, applyTextRender } from '../src/utils/TextToANSI';
import { CanvasState } from '../src/state/CanvasState';
import { UndoStack } from '../src/state/UndoStack';
import { Cell } from '../src/types';

const FIRACODE_CELL = { width: 9.6, height: 19, font: '16px "Fira Code"', advance: 9.6 };

function textCell(char: string): Cell {
  return { char, fg: [255, 255, 255], bg: [-1, -1, -1] };
}

describe('previewFontString builds a valid ctx.font', () => {
  it('uses the preview size plus the family from a full font string', () => {
    expect(previewFontString('18px "Unifont"')).toBe('96px "Unifont"');
    expect(previewFontString('18px "Unifont"')).not.toMatch(/^96px "18px /);
  });

  it('passes a multi-family list through without mangling', () => {
    expect(previewFontString("18px 'Fira Code', 'FiraCode'")).toBe(
      "96px 'Fira Code', 'FiraCode'",
    );
  });

  it('leaves bare keywords intact', () => {
    expect(previewFontString('22px monospace')).toBe('96px monospace');
  });
});

describe('calculateGrid renders at an explicit character size', () => {
  it('uses the exact requested size when the crop fits, preserving aspect', () => {
    // ~"hello" at 96px Fira Code: 60 cols wide, rows follow the font
    // aspect (60*90*(9.6/19)/305 ~ 9), not the height target.
    const grid = calculateGrid(305, 90, 60, 24, FIRACODE_CELL);
    expect(grid).toEqual({ cols: 60, rows: 9 });
  });

  it('scales wide text down to the width target preserving aspect', () => {
    const grid = calculateGrid(2000, 500, 60, 24, FIRACODE_CELL);
    expect(grid).toEqual({ cols: 60, rows: 8 });
  });

  it('scales tall text down to the height target preserving aspect', () => {
    const grid = calculateGrid(100, 800, 60, 24, FIRACODE_CELL);
    expect(grid).toEqual({ cols: 6, rows: 24 });
  });

  it('never upscales: output stays within the requested size', () => {
    const grid = calculateGrid(10, 10, 80, 24, FIRACODE_CELL);
    expect(grid.cols).toBeLessThanOrEqual(80);
    expect(grid.rows).toBeLessThanOrEqual(24);
  });

  it('fits a tiny explicit size', () => {
    const grid = calculateGrid(10, 10, 2, 1, FIRACODE_CELL);
    expect(grid).toEqual({ cols: 2, rows: 1 });
  });

  it('sanitizes non-finite and oversize targets', () => {
    expect(calculateGrid(100, 100, NaN, 24, FIRACODE_CELL)).toEqual({ cols: 1, rows: 1 });
    const huge = calculateGrid(100, 100, 99999, 99999, FIRACODE_CELL);
    expect(huge.cols).toBeLessThanOrEqual(2048);
    expect(huge.rows).toBeLessThanOrEqual(2048);
  });
});

describe('buildTextBatch centers cells on the server grid and skips blanks', () => {
  const FULL = { col: 0, row: 0, w: 60, h: 20 };

  it('centers the grid on a full-canvas grid', () => {
    const batch = buildTextBatch([textCell('A')], 1, 1, FULL);
    expect(batch).toHaveLength(1);
    expect(batch[0].col).toBe(29);
    expect(batch[0].row).toBe(9);
    expect(batch[0].cell.char).toBe('A');
  });

  it('centers on an inset grid, not the viewport', () => {
    const batch = buildTextBatch([textCell('A')], 1, 1, { col: 10, row: 5, w: 20, h: 10 });
    expect(batch).toHaveLength(1);
    expect(batch[0].col).toBe(19);
    expect(batch[0].row).toBe(9);
  });

  it('skips blank cells so text never paints empty fills over the map', () => {
    const cells: Cell[] = [
      textCell('A'),
      { char: '', fg: [204, 204, 204], bg: [-1, -1, -1] },
      { char: '', fg: [204, 204, 204], bg: [0, 0, 0] },
      textCell('B'),
    ];
    const batch = buildTextBatch(cells, 2, 2, FULL);
    expect(batch.map(b => b.cell.char)).toEqual(['A', 'B']);
    expect(batch[0]).toMatchObject({ col: 29, row: 9 });
    expect(batch[1]).toMatchObject({ col: 30, row: 10 });
  });

  it('keeps blank-char cells that carry a visible background', () => {
    const cells: Cell[] = [{ char: '', fg: [204, 204, 204], bg: [255, 0, 0] }];
    const batch = buildTextBatch(cells, 1, 1, FULL);
    expect(batch).toHaveLength(1);
  });

  it('keeps placements past the grid edge for applyTextRender to grow into', () => {
    const batch = buildTextBatch([textCell('A')], 30, 1, { col: 0, row: 0, w: 10, h: 10 });
    expect(batch).toHaveLength(1);
    expect(batch[0].col).toBe(-10);
  });
});

describe('applyTextRender layers the batch onto the given state', () => {
  it('pushes undo, adds a layer, and applies the batch', () => {
    const state = new CanvasState(60, 20);
    const undo = new UndoStack();
    undo.setCurrentState(state);
    applyTextRender(state, undo, 'Text: hello', [{ col: 29, row: 9, cell: textCell('A') }]);
    expect(state.layers).toHaveLength(2);
    expect(state.layers[1].name).toBe('Text: hello');
    expect(state.getActiveLayer().cells[9][29].char).toBe('A');
    expect(undo.canUndo()).toBe(true);
    const restored = undo.undo()!;
    expect(restored.layers).toHaveLength(1);
  });

  it('is a no-op for null or empty results', () => {
    const state = new CanvasState(60, 20);
    const undo = new UndoStack();
    undo.setCurrentState(state);
    applyTextRender(state, undo, 'Text: hello', []);
    applyTextRender(state, null, 'Text: hello', []);
    expect(state.layers).toHaveLength(1);
    expect(undo.canUndo()).toBe(false);
  });

  it('grows the viewport and grid for out-of-grid placements in one undo step', () => {
    const state = new CanvasState(10, 10);
    state.setServerBounds({ col: 2, row: 2, w: 4, h: 4 });
    const undo = new UndoStack();
    undo.setCurrentState(state);
    applyTextRender(state, undo, 'Text: wide', [
      { col: 0, row: 0, cell: textCell('A') },
      { col: 20, row: 20, cell: textCell('B') },
    ]);
    // Both glyphs landed in place: the near one was already stored, the
    // far one inside the appended storage (right/bottom growth).
    expect(state.getCompositeCell(0, 0)?.char).toBe('A');
    expect(state.getCompositeCell(20, 20)?.char).toBe('B');
    // The grid expanded to cover both placements.
    expect(state.serverBounds).toEqual({ col: 0, row: 0, w: 21, h: 21 });
    expect(state.serverBounds.col + state.serverBounds.w).toBeLessThanOrEqual(state.width);
    expect(state.layers).toHaveLength(2);
    // Growth + bounds + paint share the single pre-mutation checkpoint:
    // one undo restores the single layer, a second finds nothing.
    expect(undo.undo()!.layers).toHaveLength(1);
    expect(undo.undo()).toBeNull();
  });

  it('reports growth through onViewportShifted so the host can rebase', () => {
    const state = new CanvasState(10, 10);
    const undo = new UndoStack();
    undo.setCurrentState(state);
    const growths: { addedLeft: number; addedTop: number; capped: boolean }[] = [];
    applyTextRender(
      state,
      undo,
      'Text: shift',
      [{ col: -5, row: 3, cell: textCell('A') }],
      (g) => { growths.push(g); },
    );
    expect(growths).toHaveLength(1);
    // Left inserts are exact: the glyph remaps to column 0.
    expect(growths[0].addedLeft).toBe(5);
    expect(growths[0].capped).toBe(false);
    expect(state.getCompositeCell(0, 3)?.char).toBe('A');
  });

  it('stays silent when everything already fits the viewport', () => {
    const state = new CanvasState(60, 20);
    const undo = new UndoStack();
    undo.setCurrentState(state);
    let calls = 0;
    applyTextRender(
      state,
      undo,
      'Text: snug',
      [{ col: 29, row: 9, cell: textCell('A') }],
      () => { calls += 1; },
    );
    expect(calls).toBe(0);
    expect(state.getCompositeCell(29, 9)?.char).toBe('A');
  });
});
