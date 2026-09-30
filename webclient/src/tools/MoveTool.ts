import { Tool, ToolContext } from './Tool';
import { Point, Cell } from '../types';
import { cellEquals } from '../utils/colors';
import { parseCellKey } from '../utils/cellKeys';
import { ensureToolCapacity } from '../canvas/ensureCapacity';

export class MoveTool implements Tool {
    private anchor: Point | null = null;
    private movingCells: { col: number, row: number, originCell: Cell }[] = [];

    onMouseDown(ctx: ToolContext, cell: Point) {
        this.anchor = cell;
        
        const selected = ctx.renderer.getSelectedCells();
        const activeLayer = ctx.state.getActiveLayer();

        this.movingCells = [];

        if (selected && selected.size > 0) {
            // Move every selected cell on the active layer — including empty
            // ones — so room coords (which sit on glyph-less interior cells)
            // are reported to onCellsMoved and their outlines move with them
            for (const key of selected) {
                const parsed = parseCellKey(key);
                if (!parsed) continue;
                const col = parsed.col;
                const row = parsed.row;
                const cell = ctx.state.getCell(col, row);
                const originCell = cell
                    ? {
                        char: cell.char,
                        fg: [...cell.fg] as [number, number, number],
                        bg: [...cell.bg] as [number, number, number],
                        bold: cell.bold,
                        italic: cell.italic,
                        underline: cell.underline
                    }
                    : { char: '', fg: [204, 204, 204] as [number, number, number], bg: [-1, -1, -1] as [number, number, number] };
                this.movingCells.push({ col, row, originCell });
            }
        } else {
            // Move everything on the active layer that is non-empty
            for (let row = 0; row < ctx.state.height; row++) {
                for (let col = 0; col < ctx.state.width; col++) {
                    const c = activeLayer.cells[row][col];
                    const isBlackBg = c.bg[0] === 0 && c.bg[1] === 0 && c.bg[2] === 0;
                    const isEmpty = (!c.char || c.char.trim() === '') && (c.bg[0] === -1 || isBlackBg);
                    if (!isEmpty) {
                        const originCell = { 
                            char: c.char, fg: [...c.fg] as [number, number, number], bg: [...c.bg] as [number, number, number],
                            bold: c.bold, italic: c.italic, underline: c.underline
                        };
                        this.movingCells.push({ col, row, originCell });
                    }
                }
            }
            if (activeLayer.overflowCells) {
                 for (const [key, c] of activeLayer.overflowCells.entries()) {
                      const parsed = parseCellKey(key);
                      if (!parsed) continue;
                      const col = parsed.col;
                      const row = parsed.row;
                      const originCell = { 
                         char: c.char, fg: [...c.fg] as [number, number, number], bg: [...c.bg] as [number, number, number],
                         bold: c.bold, italic: c.italic, underline: c.underline
                     };
                     this.movingCells.push({ col, row, originCell });
                 }
            }
        }
    }

    onDrag(ctx: ToolContext, _from: Point, to: Point) {
        if (!this.anchor) return;
        const dx = to.x - this.anchor.x;
        const dy = to.y - this.anchor.y;

        const previewMap = new Map<string, { col: number, row: number, cell: Cell }>();

        // 1. Hide the original positions by placing transparent replacement cells
        for (const mc of this.movingCells) {
            previewMap.set(`${mc.col},${mc.row}`, {
                col: mc.col,
                row: mc.row,
                cell: { char: '', fg: [204, 204, 204], bg: [-1, -1, -1] }
            });
        }

        // 2. Draw the cells at the new positions
        for (const mc of this.movingCells) {
            const newCol = mc.col + dx;
            const newRow = mc.row + dy;
            
            previewMap.set(`${newCol},${newRow}`, {
                col: newCol,
                row: newRow,
                cell: mc.originCell
            });
        }

        ctx.renderer.setPreview(Array.from(previewMap.values()));
    }

    onMouseUp(ctx: ToolContext, cell: Point) {
        if (!this.anchor) return;
        
        const dx = cell.x - this.anchor.x;
        const dy = cell.y - this.anchor.y;
        
        ctx.renderer.clearPreview();

        if (dx === 0 && dy === 0) {
            // No movement occurred
            this.anchor = null;
            this.movingCells = [];
            return;
        }

        // Noop guard: nothing collected (e.g. a whole-layer move on an empty
        // layer) pushes no undo entry and changes nothing.
        if (this.movingCells.length === 0) {
            this.anchor = null;
            return;
        }

        // Destinations past the violet line grow the viewport instead of
        // being clipped; only 2048-capped destinations are still dropped.
        // Out-of-bounds origins (overflow cells) still cannot be cleared.
        // Everything below runs in the post-growth frame: left/top inserts
        // shift every stored coord (see BrushTool.onMouseDown).
        const destinations = this.movingCells.map(mc => ({ col: mc.col + dx, row: mc.row + dy }));
        const cap = ensureToolCapacity(ctx, destinations);
        const sh = cap.shift;
        const inStorage = (c: number, r: number): boolean =>
            c >= 0 && c < ctx.state.width && r >= 0 && r < ctx.state.height;

        const clearUpdates = this.movingCells
            .map(mc => ({ col: mc.col + sh.col, row: mc.row + sh.row }))
            .filter(p => inStorage(p.col, p.row))
            .map(p => ({
                col: p.col, row: p.row, cell: { char: '', fg: [204, 204, 204] as [number, number, number], bg: [-1, -1, -1] as [number, number, number] }
            }));

        const placeUpdates = this.movingCells
            .map(mc => ({
                col: mc.col + dx + sh.col, row: mc.row + dy + sh.row, cell: mc.originCell
            }))
            .filter(u => !cap.dropped.has(`${u.col},${u.row}`));

        // Merge with placements winning over clears on overlap, then drop
        // writes that would not actually change the cell. A fully clipped
        // or content-identical move pushes no undo entry.
        const merged = new Map<string, { col: number; row: number; cell: Cell }>();
        for (const u of [...clearUpdates, ...placeUpdates]) {
            merged.set(`${u.col},${u.row}`, { col: u.col, row: u.row, cell: u.cell });
        }
        const batch = [...merged.values()].filter(u => {
            const current = ctx.state.getCell(u.col, u.row);
            return !current || !cellEquals(current, u.cell);
        });
        if (batch.length === 0) {
            this.anchor = null;
            this.movingCells = [];
            return;
        }

        // Push state for undo (skipped when growth already pushed one).
        if (!cap.pushed) ctx.undoStack.push(ctx.state);

        // Report before painting: the host snapshots the live canvas here
        // for deny reverts, and nothing below reads painted cells (list,
        // overlay, and queue updates are coord-only), so pre-paint order
        // is safe and keeps the snapshot pre-move.
        if (ctx.onCellsMoved) {
            ctx.onCellsMoved(
                this.movingCells.map(mc => ({
                    fromCol: mc.col + sh.col,
                    fromRow: mc.row + sh.row,
                    toCol: mc.col + dx + sh.col,
                    toRow: mc.row + dy + sh.row
                }))
            );
        }

        ctx.state.applyBatch(batch);

        // 3. Move the selection outline if any
        let selected = ctx.renderer.getSelectedCells();
        if (selected && selected.size > 0) {
            const newSel = new Set<string>();
            for (const key of selected) {
                const parsed = parseCellKey(key);
                if (!parsed) continue;
                newSel.add(`${parsed.col + dx},${parsed.row + dy}`);
            }
            ctx.renderer.setSelection(newSel);
            ctx.selectionSync?.setSelection(newSel);
        }

        this.anchor = null;
        this.movingCells = [];
    }

    onHover(_ctx: ToolContext, _cell: Point) {}
    onMouseLeave(_ctx: ToolContext) {}
}
