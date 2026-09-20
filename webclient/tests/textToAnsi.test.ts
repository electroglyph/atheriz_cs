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

describe('calculateGrid fits text inside the current map', () => {
  it('scales short text up to span the full allowed width, preserving aspect', () => {
    // ~"hello" at 96px Fira Code on a 60x20 map: fills all 60 cols, rows
    // follow the font aspect (60*90*(9.6/19)/305 ~ 9), not the map height.
    const grid = calculateGrid(305, 90, 60, 60, 20, FIRACODE_CELL);
    expect(grid).toEqual({ cols: 60, rows: 9 });
  });

  it('shrinks wide text to the max-width cap preserving aspect', () => {
    const grid = calculateGrid(2000, 500, 60, 60, 20, FIRACODE_CELL);
    expect(grid).toEqual({ cols: 60, rows: 8 });
  });

  it('shrinks tall text to the map height preserving aspect', () => {
    const grid = calculateGrid(100, 800, 60, 60, 20, FIRACODE_CELL);
    expect(grid).toEqual({ cols: 4, rows: 18 });
  });

  it('never exceeds the map bounds even when the cap is larger', () => {
    const grid = calculateGrid(2000, 500, 60, 30, 20, FIRACODE_CELL);
    expect(grid.cols).toBeLessThanOrEqual(30);
    expect(grid.rows).toBeLessThanOrEqual(18);
  });

  it('fits on a tiny map', () => {
    const grid = calculateGrid(305, 90, 60, 5, 3, FIRACODE_CELL);
    expect(grid.cols).toBeLessThanOrEqual(5);
    expect(grid.rows).toBeLessThanOrEqual(1);
  });
});

describe('buildTextBatch centers cells and skips blanks', () => {
  it('centers the grid on the map', () => {
    const batch = buildTextBatch([textCell('A')], 1, 1, 60, 20);
    expect(batch).toHaveLength(1);
    expect(batch[0].col).toBe(29);
    expect(batch[0].row).toBe(9);
    expect(batch[0].cell.char).toBe('A');
  });

  it('skips blank cells so text never paints empty fills over the map', () => {
    const cells: Cell[] = [
      textCell('A'),
      { char: '', fg: [204, 204, 204], bg: [-1, -1, -1] },
      { char: '', fg: [204, 204, 204], bg: [0, 0, 0] },
      textCell('B'),
    ];
    const batch = buildTextBatch(cells, 2, 2, 60, 20);
    expect(batch.map(b => b.cell.char)).toEqual(['A', 'B']);
    expect(batch[0]).toMatchObject({ col: 29, row: 9 });
    expect(batch[1]).toMatchObject({ col: 30, row: 10 });
  });

  it('keeps blank-char cells that carry a visible background', () => {
    const cells: Cell[] = [{ char: '', fg: [204, 204, 204], bg: [255, 0, 0] }];
    const batch = buildTextBatch(cells, 1, 1, 60, 20);
    expect(batch).toHaveLength(1);
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
});
