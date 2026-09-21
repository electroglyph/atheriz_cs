import { Tool, ToolContext } from './Tool';
import { Point, Cell, RotateMode } from '../types';
import { cellEquals } from '../utils/colors';

import { transformCharacter } from '../utils/transformMappings';
import { measureCellMetrics } from '../utils/fontMetrics';
import { parseCellKey } from '../utils/cellKeys';

function getCellAspect(fontFamily: string): number {
    try {
        const m = measureCellMetrics(fontFamily, 16);
        return m.width / m.height;
    } catch {
        return 0.5;
    }
}

export class RotateTool implements Tool {
    private anchor: Point | null = null;
    private movingCells: { col: number, row: number, originCell: Cell }[] = [];
    private startAngle: number = 0;
    private currentTheta: number = 0;

    // Center used for rotation
    private cx: number = 0;
    private cy: number = 0;

    // atan2(0,0) is implementation-defined noise: a click exactly on the
    // rotation center means "no rotation", so fall back to the given angle.
    private static angleOf(dx: number, dy: number, fallback: number): number {
        return (dx === 0 && dy === 0) ? fallback : Math.atan2(dy, dx);
    }

    private static inBounds(ctx: ToolContext, c: number, r: number): boolean {
        return c >= 0 && c < ctx.state.width && r >= 0 && r < ctx.state.height;
    }

    // Merge clears + placements (placements win on overlap) and drop writes
    // that would not change the cell, so content-identical transforms push
    // no undo entry. Out-of-bounds destinations are clipped (dropped).
    private static buildBatch(
        ctx: ToolContext,
        clearUpdates: { col: number; row: number; cell: Cell }[],
        placeUpdates: { col: number; row: number; cell: Cell }[]
    ): { col: number; row: number; cell: Cell }[] {
        const merged = new Map<string, { col: number; row: number; cell: Cell }>();
        for (const u of [...clearUpdates, ...placeUpdates]) {
            if (RotateTool.inBounds(ctx, u.col, u.row)) {
                merged.set(`${u.col},${u.row}`, u);
            }
        }
        return [...merged.values()].filter(u => {
            const current = ctx.state.getCell(u.col, u.row);
            return !current || !cellEquals(current, u.cell);
        });
    }

    public applyTransform(ctx: ToolContext, mode: Exclude<RotateMode, 'free'>) {
        const selected = ctx.renderer.getSelectedCells();
        const activeLayer = ctx.state.getActiveLayer();
        
        let targetCells: { col: number, row: number, originCell: Cell }[] = [];

        if (selected && selected.size > 0) {
            for (const key of selected) {
                const parsed = parseCellKey(key);
                if (!parsed) continue;
                const col = parsed.col;
                const row = parsed.row;
                const cell = ctx.state.getCell(col, row);
                // Skip empty cells (same emptiness rule as CanvasState.setCell:
                // no char and a transparent bg), so rotating a selection that
                // is already blank pushes no undo entry.
                if (cell && ((cell.char && cell.char.trim() !== '') || cell.bg[0] !== -1)) {
                    targetCells.push({ col, row, originCell: cell });
                }
            }
        } else {
            for (let row = 0; row < ctx.state.height; row++) {
                for (let col = 0; col < ctx.state.width; col++) {
                    const c = activeLayer.cells[row][col];
                    const isBlackBg = c.bg[0] === 0 && c.bg[1] === 0 && c.bg[2] === 0;
                    const isEmpty = (!c.char || c.char.trim() === '') && (c.bg[0] === -1 || isBlackBg);
                    if (!isEmpty) {
                        targetCells.push({ col, row, originCell: c });
                    }
                }
            }
            if (activeLayer.overflowCells) {
                for (const [key, c] of activeLayer.overflowCells.entries()) {
                    const parsed = parseCellKey(key);
                    if (!parsed) continue;
                    targetCells.push({ col: parsed.col, row: parsed.row, originCell: c });
                }
            }
        }

        if (targetCells.length === 0) return;

        // Compute Bounding Box
        let minCol = Infinity, maxCol = -Infinity;
        let minRow = Infinity, maxRow = -Infinity;
        for (const tc of targetCells) {
            if (tc.col < minCol) minCol = tc.col;
            if (tc.col > maxCol) maxCol = tc.col;
            if (tc.row < minRow) minRow = tc.row;
            if (tc.row > maxRow) maxRow = tc.row;
        }

        const width = maxCol - minCol + 1;
        const height = maxRow - minRow + 1;

        const clearUpdates = targetCells.map(tc => ({
            col: tc.col, row: tc.row, 
            cell: { char: '', fg: [204, 204, 204] as [number, number, number], bg: [-1, -1, -1] as [number, number, number] }
        }));

        const newTargetCells: { col: number, row: number, originCell: Cell }[] = [];
        const mappedSelection = new Set<string>();

        // 2. Transform positions and characters (in-place, anchored to bbox top-left)
        for (const tc of targetCells) {
            const i = tc.col - minCol;
            const j = tc.row - minRow;
            let ni = i, nj = j;
            let newChar = tc.originCell.char;

            switch (mode) {
                case 'cw90':
                    ni = height - 1 - j;
                    nj = i;
                    break;
                case 'ccw90':
                    ni = j;
                    nj = width - 1 - i;
                    break;
                case '180':
                    ni = width - 1 - i;
                    nj = height - 1 - j;
                    break;
                case 'flip-h':
                    ni = width - 1 - i;
                    break;
                case 'flip-v':
                    nj = height - 1 - j;
                    break;
            }
            newChar = transformCharacter(newChar, mode);

            const finalCol = minCol + ni;
            const finalRow = minRow + nj;

            newTargetCells.push({
                col: finalCol,
                row: finalRow,
                originCell: { ...tc.originCell, char: newChar }
            });
            if (selected && selected.size > 0 && RotateTool.inBounds(ctx, finalCol, finalRow)) {
                mappedSelection.add(`${finalCol},${finalRow}`);
            }
        }

        const placeUpdates = newTargetCells.map(ntc => ({
            col: ntc.col, row: ntc.row, cell: ntc.originCell
        }));

        // Content-identical or fully-clipped transforms push no undo entry.
        const batch = RotateTool.buildBatch(ctx, clearUpdates, placeUpdates);
        if (batch.length === 0) return;

        ctx.undoStack.push(ctx.state);
        ctx.state.applyBatch(batch);

        if (mappedSelection.size > 0) {
            ctx.renderer.setSelection(mappedSelection);
            ctx.selectionSync?.setSelection(mappedSelection);
        } else if (selected && selected.size > 0) {
             // they all fell out of bounds
             ctx.renderer.clearSelection();
             ctx.selectionSync?.clearSelection();
        }

    }

    onMouseDown(ctx: ToolContext, cell: Point) {
        if (ctx.appState.rotateMode !== 'free') return;
        this.anchor = cell;
        
        const selected = ctx.renderer.getSelectedCells();
        const activeLayer = ctx.state.getActiveLayer();
        this.movingCells = [];

        if (selected && selected.size > 0) {
            for (const key of selected) {
                const parsed = parseCellKey(key);
                if (!parsed) continue;
                const cell = ctx.state.getCell(parsed.col, parsed.row);
                if (cell) {
                    this.movingCells.push({ col: parsed.col, row: parsed.row, originCell: cell });
                }
            }
        } else {
            for (let row = 0; row < ctx.state.height; row++) {
                for (let col = 0; col < ctx.state.width; col++) {
                    const c = activeLayer.cells[row][col];
                    const isBlackBg = c.bg[0] === 0 && c.bg[1] === 0 && c.bg[2] === 0;
                    const isEmpty = (!c.char || c.char.trim() === '') && (c.bg[0] === -1 || isBlackBg);
                    if (!isEmpty) {
                        this.movingCells.push({ col, row, originCell: c });
                    }
                }
            }
            if (activeLayer.overflowCells) {
                for (const [key, c] of activeLayer.overflowCells.entries()) {
                    const parsed = parseCellKey(key);
                    if (!parsed) continue;
                    this.movingCells.push({ col: parsed.col, row: parsed.row, originCell: c });
                }
            }
        }

        if (this.movingCells.length === 0) return;

        let minCol = Infinity, maxCol = -Infinity;
        let minRow = Infinity, maxRow = -Infinity;
        for (const tc of this.movingCells) {
            if (tc.col < minCol) minCol = tc.col;
            if (tc.col > maxCol) maxCol = tc.col;
            if (tc.row < minRow) minRow = tc.row;
            if (tc.row > maxRow) maxRow = tc.row;
        }

        this.cx = (minCol + maxCol) / 2;
        this.cy = (minRow + maxRow) / 2;

        const dy = cell.y - this.cy;
        const dx = cell.x - this.cx;
        this.startAngle = RotateTool.angleOf(dx, dy, 0);
        this.currentTheta = 0;
    }

    onDrag(ctx: ToolContext, _from: Point, to: Point) {
        if (!this.anchor || ctx.appState.rotateMode !== 'free' || this.movingCells.length === 0) return;
        
        const dy = to.y - this.cy;
        const dx = to.x - this.cx;
        const currentAngle = RotateTool.angleOf(dx, dy, this.startAngle);
        this.currentTheta = currentAngle - this.startAngle;

        this.updateFreeHover(ctx, this.currentTheta);
    }

    private updateFreeHover(ctx: ToolContext, theta: number) {
        const previewMap = new Map<string, { col: number, row: number, cell: Cell }>();

        const W = getCellAspect(ctx.appState.fontFamily);
        const H = 1.0;

        // 1. Hide original positions
        for (const mc of this.movingCells) {
            previewMap.set(`${mc.col},${mc.row}`, {
                col: mc.col,
                row: mc.row,
                cell: { char: '', fg: [204, 204, 204], bg: [-1, -1, -1] }
            });
        }

        const cosInv = Math.cos(-theta);
        const sinInv = Math.sin(-theta);

        // We use Reverse Mapping (target to source) instead of a Forward Pass (source to target) 
        // to prevent sparse "holes" between characters at large angles. By scanning a conservatively 
        // scaled bounding box in the destination space and computing where each cell originated, 
        // we guarantee contiguous coverage.
        
        let Rmax = 0;
        for (const mc of this.movingCells) {
             const dx = (mc.col - this.cx) * W;
             const dy = (mc.row - this.cy) * H;
             const r = Math.sqrt(dx*dx + dy*dy);
             if (r > Rmax) Rmax = r;
        }

        const RmaxCol = Math.ceil(Rmax / W);
        const RmaxRow = Math.ceil(Rmax / H);
        
        const minC = Math.floor(this.cx - RmaxCol);
        const maxC = Math.ceil(this.cx + RmaxCol);
        const minR = Math.floor(this.cy - RmaxRow);
        const maxR = Math.ceil(this.cy + RmaxRow);

        // Put originals in a map for quick lookup
        const originHash = new Map<string, Cell>();
        for (const mc of this.movingCells) originHash.set(`${mc.col},${mc.row}`, mc.originCell);

        for (let r = minR - 1; r <= maxR + 1; r++) {
            for (let c = minC - 1; c <= maxC + 1; c++) {
                const px = (c - this.cx) * W;
                const py = (r - this.cy) * H;

                const ox = px * cosInv - py * sinInv;
                const oy = px * sinInv + py * cosInv;
                
                const sc = Math.round((ox / W) + this.cx);
                const sr = Math.round((oy / H) + this.cy);
                
                const oCell = originHash.get(`${sc},${sr}`);
                if (oCell) {
                    previewMap.set(`${c},${r}`, {
                        col: c,
                        row: r,
                        cell: oCell
                    });
                }
            }
        }

        ctx.renderer.setPreview(Array.from(previewMap.values()));
    }

    onMouseUp(ctx: ToolContext, cell: Point) {
        if (!this.anchor || ctx.appState.rotateMode !== 'free' || this.movingCells.length === 0) return;
        
        const dy = cell.y - this.cy;
        const dx = cell.x - this.cx;
        const currentAngle = RotateTool.angleOf(dx, dy, this.startAngle);
        this.currentTheta = currentAngle - this.startAngle;

        ctx.renderer.clearPreview();

        // Epsilon zero check: a click on the center (or float noise) must
        // not record an undo entry for a no-op rotation.
        if (Math.abs(this.currentTheta) < 1e-9) {
            this.anchor = null;
            this.movingCells = [];
            return;
        }

        const W = getCellAspect(ctx.appState.fontFamily);
        const H = 1.0;
        const cosInv = Math.cos(-this.currentTheta);
        const sinInv = Math.sin(-this.currentTheta);
        
        let Rmax = 0;
        for (const mc of this.movingCells) {
             const distx = (mc.col - this.cx) * W;
             const disty = (mc.row - this.cy) * H;
             const r = Math.sqrt(distx*distx + disty*disty);
             if (r > Rmax) Rmax = r;
        }

        const RmaxCol = Math.ceil(Rmax / W);
        const RmaxRow = Math.ceil(Rmax / H);
        const minC = Math.floor(this.cx - RmaxCol);
        const maxC = Math.ceil(this.cx + RmaxCol);
        const minR = Math.floor(this.cy - RmaxRow);
        const maxR = Math.ceil(this.cy + RmaxRow);

        const originHash = new Map<string, Cell>();
        for (const mc of this.movingCells) originHash.set(`${mc.col},${mc.row}`, mc.originCell);

        const newTargetCells: { col: number, row: number, originCell: Cell }[] = [];
        const mappedSelection = new Set<string>();
        const wasSelected = ctx.renderer.getSelectedCells().size > 0;

        for (let r = minR - 1; r <= maxR + 1; r++) {
            for (let c = minC - 1; c <= maxC + 1; c++) {
                const px = (c - this.cx) * W;
                const py = (r - this.cy) * H;
                const ox = px * cosInv - py * sinInv;
                const oy = px * sinInv + py * cosInv;
                const sc = Math.round((ox / W) + this.cx);
                const sr = Math.round((oy / H) + this.cy);
                
                const oCell = originHash.get(`${sc},${sr}`);
                if (oCell && RotateTool.inBounds(ctx, c, r)) {
                    newTargetCells.push({ col: c, row: r, originCell: oCell });
                    if (wasSelected) mappedSelection.add(`${c},${r}`);
                }
            }
        }

        const clearUpdates = this.movingCells.map(mc => ({
            col: mc.col, row: mc.row, cell: { char: '', fg: [204, 204, 204] as [number, number, number], bg: [-1, -1, -1] as [number, number, number] }
        }));

        const placeUpdates = newTargetCells.map(ntc => ({
            col: ntc.col, row: ntc.row, cell: ntc.originCell
        }));

        // Content-identical or fully-clipped rotations push no undo entry.
        const batch = RotateTool.buildBatch(ctx, clearUpdates, placeUpdates);
        if (batch.length === 0) {
            this.anchor = null;
            this.movingCells = [];
            return;
        }

        ctx.undoStack.push(ctx.state);
        ctx.state.applyBatch(batch);

        if (mappedSelection.size > 0) {
            ctx.renderer.setSelection(mappedSelection);
            ctx.selectionSync?.setSelection(mappedSelection);
        } else if (wasSelected) {
            ctx.renderer.clearSelection();
            ctx.selectionSync?.clearSelection();
        }

        this.anchor = null;
        this.movingCells = [];
    }

    onHover(_ctx: ToolContext, _cell: Point) {}
    onMouseLeave(_ctx: ToolContext) {}
}
