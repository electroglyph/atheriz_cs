import { toCssFontFamily } from "./cssFont";

export interface CellMetrics {
  width: number;
  height: number;
  font: string;
  /** unrounded glyph advance; the editor cell grid is derived from this */
  advance: number;
}

export interface TextMetricsInput {
  width?: number;
  fontBoundingBoxAscent?: number;
  fontBoundingBoxDescent?: number;
  actualBoundingBoxAscent?: number;
  actualBoundingBoxDescent?: number;
}

const GRID_LEADING = 2;

function finiteOr(value: number | undefined, fallback: number): number {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

export function deriveCellMetrics(fontSize: number, tm: TextMetricsInput): { width: number; height: number } {
  // NB: `??` is not enough here — a broken measureText can report NaN,
  // which passes straight through `??` and poisons the grid. Require
  // finite numbers explicitly.
  const width = Math.max(1, Math.ceil(finiteOr(tm.width, fontSize * 0.6)));

  const ascent = tm.fontBoundingBoxAscent ?? tm.actualBoundingBoxAscent;
  const descent = tm.fontBoundingBoxDescent ?? tm.actualBoundingBoxDescent;
  const height = ascent != null && descent != null &&
      Number.isFinite(ascent) && Number.isFinite(descent)
    ? Math.ceil(ascent + descent + GRID_LEADING)
    : Math.ceil(fontSize * 1.2);

  return { width, height };
}

export function measureCellMetrics(
  fontFamily: string,
  fontSize: number,
): CellMetrics {
  const font = `${fontSize}px ${toCssFontFamily(fontFamily)}`;
  const fallback: CellMetrics = {
    width: Math.max(1, Math.ceil(fontSize * 0.6)),
    height: Math.max(1, Math.ceil(fontSize * 1.2)),
    font,
    advance: fontSize * 0.6,
  };
  const canvas = document.createElement("canvas");
  const ctx = canvas.getContext("2d");
  if (!ctx) return fallback;
  ctx.font = font;

  const tm = ctx.measureText("M") as TextMetricsInput;
  const { width, height } = deriveCellMetrics(fontSize, tm);
  const advance = finiteOr(tm.width, fontSize * 0.6);

  return { width, height, font, advance };
}
