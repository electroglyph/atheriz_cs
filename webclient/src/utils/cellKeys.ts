/**
 * Helpers for `"col,row"` cell keys used by canvas tools and the renderer.
 *
 * Selection sets cross tool/renderer boundaries as plain strings, so a
 * malformed key must never reach a `layer.cells[row][col]` index: `parseInt`
 * without a radix yields `NaN` for garbage input, `NaN` passes naive bounds
 * checks (`NaN < 0` and `NaN >= width` are both false), and indexing with it
 * produces `undefined` whose member access throws.
 */

/** Parse a `"col,row"` key. Returns `null` for malformed keys (skip them). */
export function parseCellKey(key: string): { col: number; row: number } | null {
    const sep = key.indexOf(',');
    if (sep < 0) return null;
    const col = parseInt(key.slice(0, sep), 10);
    const row = parseInt(key.slice(sep + 1), 10);
    if (!Number.isInteger(col) || !Number.isInteger(row)) return null;
    return { col, row };
}
