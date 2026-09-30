import { Tool, ToolContext } from './Tool';
import { Point, Cell, Color } from '../types';
import { sampleGradient, lerpColor } from '../utils/colors';
import { parseCellKey } from '../utils/cellKeys';
import { ensureToolCapacity } from '../canvas/ensureCapacity';

function luminance(c: Color): number {
    return (c[0] * 0.299 + c[1] * 0.587 + c[2] * 0.114) / 255;
}

export class FillTool implements Tool {
    private anchor: Point | null = null;
    private currentTarget: Point | null = null;
    private fillCells: Set<string> = new Set();
    /** True when the gradient gesture already pushed undo for growth. */
    private gradientGrew = false;

    private isEmptyCell(cell: Cell): boolean {
        const hasChar = cell.char && cell.char.trim() !== '';
        // Only the transparent marker counts as "no background": an opaque
        // black bg is ink (matches GradientTool/CanvasState.setCell).
        const hasBg = cell.bg[0] !== -1;
        return !hasChar && !hasBg;
    }

    private floodFill(ctx: ToolContext, start: Point): Set<string> {
        const layer = ctx.state.getActiveLayer();
        // Flood stays inside the violet server-grid rect, not the full
        // viewport: with default full-canvas bounds this matches the old
        // behavior exactly. The seed is expanded into the rect first (see
        // onMouseDown), so clicking outside the line grows the grid.
        const b = ctx.state.serverBounds;
        const minX = b.col;
        const minY = b.row;
        const maxX = b.col + b.w;
        const maxY = b.row + b.h;
        const visited = new Set<string>();
        const queue: Point[] = [start];
        let qIdx = 0;
        let reachedBorder = false;

        while (qIdx < queue.length) {
            const p = queue[qIdx++]!;
            const k = `${p.x},${p.y}`;

            if (visited.has(k)) continue;
            if (p.x < minX || p.x >= maxX || p.y < minY || p.y >= maxY) {
                reachedBorder = true;
                continue;
            }

            const cell = layer.cells[p.y][p.x];
            if (!this.isEmptyCell(cell)) continue;

            visited.add(k);

            queue.push({ x: p.x - 1, y: p.y });
            queue.push({ x: p.x + 1, y: p.y });
            queue.push({ x: p.x, y: p.y - 1 });
            queue.push({ x: p.x, y: p.y + 1 });
        }

        if (reachedBorder) {
            return this.getOutsideEmptyCells(ctx);
        }

        return visited;
    }

    private getOutsideEmptyCells(ctx: ToolContext): Set<string> {
        const layer = ctx.state.getActiveLayer();
        const b = ctx.state.serverBounds;
        const minX = b.col;
        const minY = b.row;
        const maxX = b.col + b.w;
        const maxY = b.row + b.h;
        const outside = new Set<string>();
        const visited = new Set<string>();
        const queue: Point[] = [];
        let qIdx = 0;

        for (let x = minX; x < maxX; x++) {
            queue.push({ x, y: minY });
            queue.push({ x, y: maxY - 1 });
        }
        for (let y = minY + 1; y < maxY - 1; y++) {
            queue.push({ x: minX, y });
            queue.push({ x: maxX - 1, y });
        }

        while (qIdx < queue.length) {
            const p = queue[qIdx++]!;
            const k = `${p.x},${p.y}`;

            if (visited.has(k)) continue;
            if (p.x < minX || p.x >= maxX || p.y < minY || p.y >= maxY) continue;

            visited.add(k);

            const cell = layer.cells[p.y][p.x];
            if (!this.isEmptyCell(cell)) continue;

            outside.add(k);

            queue.push({ x: p.x - 1, y: p.y });
            queue.push({ x: p.x + 1, y: p.y });
            queue.push({ x: p.x, y: p.y - 1 });
            queue.push({ x: p.x, y: p.y + 1 });
        }

        return outside;
    }

    private computeFillCells(ctx: ToolContext, start: Point): Set<string> {
        const selected = ctx.renderer.getSelectedCells();
        if (selected.size > 0) return selected;
        return this.floodFill(ctx, start);
    }

    onMouseDown(ctx: ToolContext, cell: Point): void {
        this.anchor = cell;
        this.currentTarget = cell;
        this.gradientGrew = false;

        // A seed outside the violet line grows the grid to include it
        // first, then the flood runs inside the (possibly expanded) rect.
        // A selected region outside the line grows it the same way.
        const selected = ctx.renderer.getSelectedCells();
        const wanted = [{ col: cell.x, row: cell.y }];
        for (const k of selected) {
            const parsed = parseCellKey(k);
            if (parsed) wanted.push({ col: parsed.col, row: parsed.row });
        }
        const cap = ensureToolCapacity(ctx, wanted);
        // Flood in the post-growth frame (see BrushTool.onMouseDown).
        const seed = { x: cell.x + cap.shift.col, y: cell.y + cap.shift.row };

        if (ctx.appState.fillMode === 'gradient') {
            this.fillCells = this.computeFillCells(ctx, seed);
            this.gradientGrew = cap.pushed;
            this.renderPreview(ctx);
        } else {
            const targets = this.computeFillCells(ctx, seed);
            const updates = this.applyFill(ctx, targets);
            if (updates.length > 0) {
                if (!cap.pushed) ctx.undoStack.push(ctx.state);
                ctx.state.applyBatch(updates);
            }
            this.anchor = null;
            this.fillCells = new Set();
        }
    }

    onDrag(ctx: ToolContext, _from: Point, to: Point): void {
        if (ctx.appState.fillMode !== 'gradient' || !this.anchor) return;
        this.currentTarget = to;
        this.renderPreview(ctx);
    }

    onMouseUp(ctx: ToolContext, cell: Point): void {
        if (ctx.appState.fillMode !== 'gradient' || !this.anchor) return;
        this.currentTarget = cell;

        ctx.renderer.clearPreview();

        const updates = this.applyGradientFill(ctx, this.anchor, this.currentTarget);
        if (updates.length > 0) {
            if (!this.gradientGrew) ctx.undoStack.push(ctx.state);
            ctx.state.applyBatch(updates);
        }

        this.anchor = null;
        this.currentTarget = null;
        this.fillCells = new Set();
    }

    onHover(_ctx: ToolContext, _cell: Point): void {}

    onMouseLeave(ctx: ToolContext): void {
        if (!this.anchor) {
            ctx.renderer.clearPreview();
        }
    }

    private renderPreview(ctx: ToolContext) {
        if (!this.anchor || !this.currentTarget || this.fillCells.size === 0) return;
        const updates = this.applyGradientFill(ctx, this.anchor, this.currentTarget);
        ctx.renderer.setPreview(updates);
    }

    private applyFill(ctx: ToolContext, targets: Set<string>): { col: number, row: number, cell: Cell }[] {
        const mode = ctx.appState.fillMode;
        const layer = ctx.state.getActiveLayer();
        const updates: { col: number, row: number, cell: Cell }[] = [];

        for (const k of targets) {
            const parsed = parseCellKey(k);
            if (!parsed) continue;
            const col = parsed.col;
            const row = parsed.row;
            if (col < 0 || col >= ctx.state.width || row < 0 || row >= ctx.state.height) continue;

            const existing = layer.cells[row][col];
            const newCell: Cell = {
                char: existing.char,
                fg: [...existing.fg] as Color,
                bg: [...existing.bg] as Color,
            };

            if (mode === 'brush') {
                newCell.char = ctx.appState.selectedChar;
                newCell.fg = [...ctx.appState.fgColor] as Color;
                newCell.bg = [...ctx.appState.bgColor] as Color;
            } else if (mode === 'foreground') {
                newCell.fg = [...ctx.appState.fgColor] as Color;
            } else if (mode === 'background') {
                newCell.bg = [...ctx.appState.bgColor] as Color;
            }

            updates.push({ col, row, cell: newCell });
        }

        return updates;
    }

    private applyGradientFill(ctx: ToolContext, start: Point, end: Point): { col: number, row: number, cell: Cell }[] {
        const layer = ctx.state.getActiveLayer();
        const updates: { col: number, row: number, cell: Cell }[] = [];

        const vx = end.x - start.x;
        const vy = end.y - start.y;
        const lenSq = vx * vx + vy * vy;

        const target = ctx.appState.gradientTarget;
        // `|| default` misses an explicitly empty array (truthy), so check length.
        const stops: Color[] = ctx.appState.gradientStops.length > 0
            ? ctx.appState.gradientStops
            : [[0, 0, 0], [255, 255, 255]];

        for (const k of this.fillCells) {
            const parsed = parseCellKey(k);
            if (!parsed) continue;
            const col = parsed.col;
            const row = parsed.row;
            if (col < 0 || col >= ctx.state.width || row < 0 || row >= ctx.state.height) continue;

            const existing = layer.cells[row][col];

            let t = 0;
            if (lenSq !== 0) {
                const wx = col - start.x;
                const wy = row - start.y;
                t = (wx * vx + wy * vy) / lenSq;
                t = Math.max(0, Math.min(1, t));
            }

            const gColor = sampleGradient(stops, t);
            const newCell: Cell = {
                char: existing.char,
                fg: [...existing.fg] as Color,
                bg: [...existing.bg] as Color,
            };

            const hasChar = existing.char && existing.char.trim() !== '';
            const hasBg = existing.bg[0] !== -1;
            const fgLum = luminance(existing.fg);
            const bgLum = hasBg ? luminance(existing.bg) : -1;
            const inkIsBg = hasBg && (!hasChar || bgLum > fgLum);
            const inkLum = inkIsBg ? bgLum : fgLum;

            if (target === 'both') {
                newCell.fg = gColor;
                newCell.bg = gColor;
            } else if (target === 'foreground') {
                if (inkIsBg) {
                    newCell.bg = gColor;
                } else {
                    newCell.fg = gColor;
                }
            } else if (target === 'background') {
                if (!inkIsBg) {
                    newCell.bg = gColor;
                }
            }

            if (target === 'luminance' || target === 'inverse-luminance') {
                const factor = target === 'luminance' ? inkLum * inkLum : (1 - inkLum) * (1 - inkLum);
                if (inkIsBg) {
                    newCell.bg = lerpColor(existing.bg, gColor, factor);
                } else {
                    newCell.fg = lerpColor(existing.fg, gColor, factor);
                }
            }

            updates.push({ col, row, cell: newCell });
        }

        return updates;
    }
}
