// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../src/utils/imageLoader', () => ({
  convertImageToAnsi: vi.fn().mockResolvedValue('ansi'),
}));

vi.mock('../src/utils/ansiParser', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../src/utils/ansiParser')>();
  return {
    ...actual,
    parseAnsiToCells: vi.fn(async (_ansi: string, cols: number, rows: number) =>
      Array.from({ length: cols * rows }, () => ({
        char: 'X',
        fg: [255, 255, 255] as [number, number, number],
        bg: [-1, -1, -1] as [number, number, number],
      })),
    ),
  };
});

import { renderTextToAnsiLayer } from '../src/utils/TextToANSI';
import { CanvasState } from '../src/state/CanvasState';

const CELL = { width: 9.6, height: 19, font: '16px "Fira Code"', advance: 9.6 };

function textSourceCanvas(): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = 400;
  canvas.height = 200;
  const ctx = canvas.getContext('2d')!;
  ctx.fillStyle = '#000000';
  ctx.fillRect(0, 0, 400, 200);
  ctx.fillStyle = '#ffffff';
  ctx.fillRect(50, 30, 300, 90);
  return canvas;
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('renderTextToAnsiLayer returns cells without mutating state', () => {
  it('fits the grid inside the map and leaves the passed state untouched', async () => {
    const state = new CanvasState(60, 20);
    const layersBefore = state.layers.length;
    const result = await renderTextToAnsiLayer(
      'hello',
      60,
      { width: state.width, height: state.height },
      { height: 25 } as never,
      textSourceCanvas(),
      CELL,
    );
    expect(result).not.toBeNull();
    expect(result!.cols).toBeLessThanOrEqual(60);
    expect(result!.rows).toBeLessThanOrEqual(18);
    expect(result!.label).toBe('Text: hello');
    expect(result!.cells.length).toBe(result!.cols * result!.rows);
    // The old contract added a layer + painted cells as a side effect;
    // the new contract must not touch the state at all.
    expect(state.layers).toHaveLength(layersBefore);
    expect(state.getCompositeCell(30, 10)?.char ?? '').toBe('');
  });

  it('returns null when the source canvas has no content', async () => {
    const canvas = document.createElement('canvas');
    canvas.width = 100;
    canvas.height = 100;
    const ctx = canvas.getContext('2d')!;
    ctx.fillStyle = '#000000';
    ctx.fillRect(0, 0, 100, 100);
    const result = await renderTextToAnsiLayer('hello', 60, { width: 60, height: 20 }, { height: 25 } as never, canvas, CELL);
    expect(result).toBeNull();
  });
});
