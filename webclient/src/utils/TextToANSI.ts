import { CanvasState } from "../state/CanvasState";
import { UndoStack } from "../state/UndoStack";
import { Cell } from "../types";
import { convertImageToAnsi } from "./imageLoader";
import { parseAnsiToCells } from "./ansiParser";
import { ChafaConfig } from "./chafaDefaults";
import { CellMetrics } from "./fontMetrics";

/**
 * Grid size for a text crop that spans the allowed width inside the current
 * map. Scales the crop up or down to fill min(maxWidth, map width) —
 * preserving aspect — then shrinks to fit when that would exceed the map
 * height bound (map height - 2). The map itself is never resized.
 */
export function calculateGrid(
  cropW: number,
  cropH: number,
  maxWidthGlyphs: number,
  canvasWidth: number,
  canvasHeight: number,
  cellMetrics: CellMetrics,
): { cols: number; rows: number } {
  const cellW = Math.max(1, cellMetrics.width);
  const cellH = Math.max(1, cellMetrics.height);
  // fontRatio = cellWidth / cellHeight. rows = (cols * cropH * fontRatio) / cropW
  const fontRatio = cellW / cellH;
  const maxCols = Math.max(1, Math.min(maxWidthGlyphs, canvasWidth));
  const maxRows = Math.max(1, canvasHeight - 2);

  let cols = maxCols;
  let rows = Math.max(1, Math.round((cols * cropH * fontRatio) / cropW));

  if (rows > maxRows) {
    rows = maxRows;
    cols = Math.max(1, Math.round((maxRows * cropW) / (cropH * fontRatio)));
  }

  return { cols: Math.min(cols, maxCols), rows: Math.min(Math.max(1, rows), maxRows) };
}

export function previewFontString(cellFont: string, px = 96): string {
  return `${px}px ${cellFont.replace(/^\d+(\.\d+)?px\s+/, '')}`;
}

/**
 * Unapplied text-conversion result: converted cells in row-major order over
 * a cols x rows grid, plus the layer label. Placement onto map coordinates
 * happens in buildTextBatch against the LIVE map size, so a canvas replaced
 * mid-conversion (New/resize/load/undo) still gets centered output.
 */
export interface TextRenderResult {
  label: string;
  cells: Cell[];
  cols: number;
  rows: number;
}

/**
 * Pipeline to convert drawn text on a temporary Canvas into quantized ANSI art:
 * 1. Derives an exact bounding box isolating the text content.
 * 2. Crops the source canvas to eliminate arbitrary whitespace.
 * 3. Dynamically calculates an ANSI grid spelling the crop across the full
 *    allowed map width at the font aspect ratio (shrinking to fit the map
 *    height when needed), never resizing the map itself.
 * 4. Passes standard PNG data to Chafa for WASM-based color quantization.
 * 5. Returns the converted cells WITHOUT touching any CanvasState, so the
 *    caller can apply them to the live canvas even if it was replaced while
 *    the async conversion was in flight.
 */
export async function renderTextToAnsiLayer(
  text: string,
  maxWidthGlyphs: number,
  mapSize: { width: number; height: number },
  chafaConfig: ChafaConfig,
  sourceCanvas: HTMLCanvasElement,
  cellMetrics: CellMetrics,
): Promise<TextRenderResult | null> {
  const fontRatio = cellMetrics.width / cellMetrics.height;

  const ctx = sourceCanvas.getContext("2d");
  if (!ctx) throw new Error("TextToANSI: source canvas 2d context unavailable");
  const w = sourceCanvas.width;
  const h = sourceCanvas.height;

   ctx.font = previewFontString(cellMetrics.font);
   const textMetrics = ctx.measureText("M");
   const metricsExt = textMetrics as unknown as { actualBoundingBoxAscent?: number; actualBoundingBoxDescent?: number };
   const rawAscent = metricsExt.actualBoundingBoxAscent;
   const rawDescent = metricsExt.actualBoundingBoxDescent;
   const ascent = typeof rawAscent === "number" && Number.isFinite(rawAscent) ? rawAscent : 80;
   const descent = typeof rawDescent === "number" && Number.isFinite(rawDescent) ? rawDescent : 20;

  const pixels = ctx.getImageData(0, 0, w, h).data;

  let minX = w,
    minY = h,
    maxX = 0,
    maxY = 0;
  let hasContent = false;

  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const idx = (y * w + x) * 4;
      if (pixels[idx] > 10 || pixels[idx + 1] > 10 || pixels[idx + 2] > 10) {
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
        hasContent = true;
      }
    }
  }

  if (!hasContent) {
    console.warn(
      "[TextToANSI] SCAN FAILED: No colored pixels found in preview.",
    );
    return null;
  }

  const pad = 10;
  const padTop = Math.max(pad, Math.ceil(ascent * 0.15));
  const padBottom = Math.max(pad, Math.ceil(descent * 0.15));
  minX = Math.max(0, minX - pad);
  minY = Math.max(0, minY - padTop);
  maxX = Math.min(w - 1, maxX + pad);
  maxY = Math.min(h - 1, maxY + padBottom);

  const cropW = maxX - minX + 1;
  const cropH = maxY - minY + 1;

  const cropCanvas = document.createElement("canvas");
  cropCanvas.width = cropW;
  cropCanvas.height = cropH;
  const cropCtx = cropCanvas.getContext("2d");
  if (!cropCtx) throw new Error("TextToANSI: crop canvas 2d context unavailable");
  cropCtx.fillStyle = "#000000";
  cropCtx.fillRect(0, 0, cropW, cropH);
  cropCtx.drawImage(
    sourceCanvas,
    minX,
    minY,
    cropW,
    cropH,
    0,
    0,
    cropW,
    cropH,
  );

  const buffer = await new Promise<ArrayBuffer | null>((resolve) => {
    cropCanvas.toBlob((blob) => {
      if (!blob) resolve(null);
      else
        blob
          .arrayBuffer()
          .then(resolve)
          .catch(() => resolve(null));
    }, "image/png");
  });

  if (!buffer) return null;

  const grid = calculateGrid(cropW, cropH, maxWidthGlyphs, mapSize.width, mapSize.height, cellMetrics);
  const totalCols = grid.cols;
  const totalRows = grid.rows;

  const ansi = await convertImageToAnsi(
    buffer,
    totalCols,
    totalRows,
    { ...chafaConfig, fontRatio },
  );

  const rawCells = await parseAnsiToCells(ansi, totalCols, totalRows);

  return {
    label: `Text: ${text.substring(0, 10)}`,
    cells: rawCells,
    cols: totalCols,
    rows: totalRows,
  };
}

/**
 * Centers converted cells on the map, skipping blank (transparent/black
 * background, empty char) cells so text never paints over the map with
 * empty fills. Coordinates outside the map are left for applyBatch to
 * clamp into overflow.
 */
export function buildTextBatch(
  rawCells: Cell[],
  totalCols: number,
  totalRows: number,
  mapWidth: number,
  mapHeight: number,
): { col: number; row: number; cell: Cell }[] {
  const batch: { col: number; row: number; cell: Cell }[] = [];
  const startX = Math.floor(mapWidth / 2 - totalCols / 2);
  const startY = Math.floor(mapHeight / 2 - totalRows / 2);

  for (let i = 0; i < rawCells.length; i++) {
    const localCol = i % totalCols;
    const localRow = Math.floor(i / totalCols);
    const cell = rawCells[i];

    if (!cell.char) {
      if (
        cell.bg[0] === -1 ||
        (cell.bg[0] === 0 && cell.bg[1] === 0 && cell.bg[2] === 0)
      ) {
        continue;
      }
    }

    batch.push({
      col: startX + localCol,
      row: startY + localRow,
      cell,
    });
  }

  return batch;
}

/**
 * Applies a converted result to the given (live) state: undo checkpoint,
 * then a new layer with the batch. No-op for null/empty results.
 */
export function applyTextRender(
  state: CanvasState,
  undoStack: UndoStack | null,
  label: string,
  batch: { col: number; row: number; cell: Cell }[],
): void {
  if (batch.length === 0) return;
  if (undoStack) undoStack.push(state);
  state.addLayer(label, false);
  state.applyBatch(batch);
}
