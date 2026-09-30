import { Tool, ToolContext } from './Tool';
import { Point, Cell } from '../types';
import { getLinePoints } from '../utils/geometry';
import { cellEquals } from '../utils/colors';
import { ensureToolCapacity } from '../canvas/ensureCapacity';

export class EraserTool implements Tool {
    private erasedCells: Set<string> = new Set();
    private undoPushed = false;
    
    private getEraserCell(ctx: ToolContext): Cell {
        const isBackgroundLayer = ctx.state.activeLayerIndex === 0;
        return {
            char: '',
            fg: [204, 204, 204], // default fg
            bg: isBackgroundLayer ? [0, 0, 0] : [-1, -1, -1]
        };
    }

    onMouseDown(ctx: ToolContext, cell: Point): void {
        this.erasedCells.clear();
        this.undoPushed = false;

        // Grow the viewport past the violet line instead of clipping;
        // only 2048-capped points are still dropped. Paint in the
        // post-growth frame: left/top inserts shift every stored coord.
        const cap = ensureToolCapacity(ctx, [{ col: cell.x, row: cell.y }]);
        if (cap.pushed) this.undoPushed = true;
        const at = { x: cell.x + cap.shift.col, y: cell.y + cap.shift.row };
        if (cap.dropped.has(`${at.x},${at.y}`)) {
            this.erasedCells.add(`${at.x},${at.y}`);
            return;
        }

        const eraserCell = this.getEraserCell(ctx);
        const current = ctx.state.getCell(at.x, at.y);
        if (!current || !cellEquals(current, eraserCell)) {
            if (!this.undoPushed) {
                ctx.undoStack.push(ctx.state);
                this.undoPushed = true;
            }
            ctx.state.setCell(at.x, at.y, eraserCell);
        }
        this.erasedCells.add(`${at.x},${at.y}`);
    }

    onDrag(ctx: ToolContext, from: Point, to: Point): void {
        const points = getLinePoints(from, to);
        const cellData = this.getEraserCell(ctx);
        const updates: {col: number, row: number, cell: Cell}[] = [];

        // Grow once for the whole stroke so the line stays continuous, then
        // paint in the post-growth frame (see onMouseDown).
        const fresh = points.filter(p => !this.erasedCells.has(`${p.x},${p.y}`));
        const cap = ensureToolCapacity(ctx, fresh.map(p => ({ col: p.x, row: p.y })));
        if (cap.pushed) this.undoPushed = true;

        for (const p of points) {
            const sx = p.x + cap.shift.col;
            const sy = p.y + cap.shift.row;
            const key = `${sx},${sy}`;
            if (this.erasedCells.has(key)) continue;
            this.erasedCells.add(key);

            // Drop only 2048-capped points instead of clipping at the edge.
            if (cap.dropped.has(key)) continue;

            const current = ctx.state.getCell(sx, sy);
            if (!current || !cellEquals(current, cellData)) {
                updates.push({ col: sx, row: sy, cell: cellData });
            }
        }
        
        if (updates.length > 0) {
            if (!this.undoPushed) {
                ctx.undoStack.push(ctx.state);
                this.undoPushed = true;
            }
            ctx.state.applyBatch(updates);
        }
    }

    onMouseUp(_ctx: ToolContext, _cell: Point): void {
        this.erasedCells.clear();
    }

    onHover(ctx: ToolContext, cell: Point): void {
        // Preview cell will be drawn black
        ctx.renderer.setPreview([{
            col: cell.x,
            row: cell.y,
            cell: this.getEraserCell(ctx)
        }]);
    }

    onMouseLeave(ctx: ToolContext): void {
        ctx.renderer.clearPreview();
    }
}
