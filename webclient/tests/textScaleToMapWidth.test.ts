// @vitest-environment jsdom
// End-to-end scaling proof: real TTF rasterized through node-canvas,
// converted by the REAL chafa wasm and parsed by the REAL ansi parser.
// No mocks below this line — if renderTextToAnsiLayer stops spanning the
// map width, this file fails.
import { describe, it, expect, beforeAll } from 'vitest';
import { registerFont } from 'canvas';
import { renderTextToAnsiLayer } from '../src/utils/TextToANSI';
import { DEFAULT_CHAFA_OPTIONS } from '../src/utils/chafaDefaults';

const FONT_FAMILY = 'E2EScaleMono';
// Display-cell aspect of the map grid (Fira Code-ish); only the ratio
// matters to calculateGrid.
const CELL = { width: 9.6, height: 19, font: `16px "${FONT_FAMILY}"`, advance: 9.6 };

beforeAll(() => {
  registerFont('fonts/Fira_Custom.ttf', { family: FONT_FAMILY });
});

function helloCanvas(): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = 640;
  canvas.height = 200;
  const ctx = canvas.getContext('2d')!;
  ctx.fillStyle = '#000000';
  ctx.fillRect(0, 0, 640, 200);
  ctx.fillStyle = '#ffffff';
  ctx.font = `96px "${FONT_FAMILY}"`;
  ctx.fillText('hello', 20, 140);
  return canvas;
}

function nonBlankFraction(cells: { char: string }[]): number {
  const marked = cells.filter(c => c.char.trim() !== '').length;
  return marked / cells.length;
}

describe('rendered text spans the map width (real font + real chafa)', () => {
  it('fills all 60 cols on a 60x20 map with real converted content', async () => {
    const result = await renderTextToAnsiLayer(
      'hello',
      60,
      { width: 60, height: 20 },
      DEFAULT_CHAFA_OPTIONS,
      helloCanvas(),
      CELL,
    );
    expect(result).not.toBeNull();
    // Spans horizontally all the way across...
    expect(result!.cols).toBe(60);
    // ...while fitting vertically...
    expect(result!.rows).toBeGreaterThanOrEqual(1);
    expect(result!.rows).toBeLessThanOrEqual(18);
    // ...and the cells are real converted text, not an empty grid.
    expect(result!.cells).toHaveLength(result!.cols * result!.rows);
    expect(nonBlankFraction(result!.cells)).toBeGreaterThan(0.1);
  }, 60000);

  it('fills all 120 cols on a larger 120x40 map', async () => {
    const result = await renderTextToAnsiLayer(
      'hello',
      120,
      { width: 120, height: 40 },
      DEFAULT_CHAFA_OPTIONS,
      helloCanvas(),
      CELL,
    );
    expect(result).not.toBeNull();
    expect(result!.cols).toBe(120);
    expect(result!.rows).toBeLessThanOrEqual(38);
    expect(result!.cells).toHaveLength(result!.cols * result!.rows);
    expect(nonBlankFraction(result!.cells)).toBeGreaterThan(0.1);
  }, 60000);
});
