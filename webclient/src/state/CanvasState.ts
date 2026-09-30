import { Cell, Layer, ServerBounds, ViewportGrowth } from '../types';
import { parseCellKey } from '../utils/cellKeys';

function cloneCell(cell: Cell): Cell {
    return {
        char: cell.char,
        fg: [...cell.fg] as [number, number, number],
        bg: [...cell.bg] as [number, number, number],
        bold: cell.bold,
        italic: cell.italic,
        underline: cell.underline,
    };
}

export class CanvasState {
    width: number;
    height: number;
    layers: Layer[];
    activeLayerIndex: number;
    /**
     * Violet server-grid rect (viewport coords). Defaults to the full
     * canvas so unmigrated paths keep working; tools expand it via
     * ensureBoundsFor* as they paint past it.
     */
    serverBounds: ServerBounds;
    private changeListeners: Set<() => void> = new Set();
    layerIdCounter: number = 0;

    /** Largest allowed canvas dimension (bounds memory: dims are floored, min 1). */
    public static readonly MAX_DIMENSION = 2048;

    /** Coerce a dimension to an integer in [1, MAX_DIMENSION]; throws on non-finite. */
    private static sanitizeDimension(value: number, name: string): number {
        if (!Number.isFinite(value)) {
            throw new RangeError(`CanvasState ${name} must be a finite number, got ${value}`);
        }
        return Math.min(CanvasState.MAX_DIMENSION, Math.max(1, Math.floor(value)));
    }

    /**
     * Replace the server-grid rect. Origin floors at 0, axes clamp to
     * 1..MAX_DIMENSION, and the rect is contained in viewport storage
     * (shrunk from the bottom-right when it would overhang). Notifies.
     */
    public setServerBounds(rect: ServerBounds): void {
        if (!Number.isFinite(rect.col) || !Number.isFinite(rect.row) ||
            !Number.isFinite(rect.w) || !Number.isFinite(rect.h)) {
            throw new RangeError(
                `CanvasState serverBounds must be finite numbers, got ${JSON.stringify(rect)}`);
        }
        const col = Math.max(0, Math.floor(rect.col));
        const row = Math.max(0, Math.floor(rect.row));
        const w = Math.min(CanvasState.sanitizeDimension(rect.w, 'bounds width'), this.width - col);
        const h = Math.min(CanvasState.sanitizeDimension(rect.h, 'bounds height'), this.height - row);
        this.serverBounds = { col, row, w: Math.max(1, w), h: Math.max(1, h) };
        this.notify();
    }

    /**
     * Expand the server-grid rect to include (col, row), clamped to
     * viewport storage. Returns whether the rect changed.
     */
    public ensureBoundsFor(col: number, row: number): boolean {
        return this.ensureBoundsForRect({ col, row, w: 1, h: 1 });
    }

    /**
     * Expand the server-grid rect to include `rect` (viewport coords),
     * clamped to viewport storage. Returns whether the rect changed.
     */
    public ensureBoundsForRect(rect: ServerBounds): boolean {
        const c0 = Math.max(0, Math.min(Math.floor(rect.col), this.width - 1));
        const r0 = Math.max(0, Math.min(Math.floor(rect.row), this.height - 1));
        const c1 = Math.max(0, Math.min(Math.ceil(rect.col + rect.w) - 1, this.width - 1));
        const r1 = Math.max(0, Math.min(Math.ceil(rect.row + rect.h) - 1, this.height - 1));
        const b = this.serverBounds;
        const col = Math.min(b.col, c0);
        const row = Math.min(b.row, r0);
        const w = Math.max(b.col + b.w, c1 + 1) - col;
        const h = Math.max(b.row + b.h, r1 + 1) - row;
        if (col === b.col && row === b.row && w === b.w && h === b.h) return false;
        this.serverBounds = { col, row, w, h };
        this.notify();
        return true;
    }

    /**
     * Grow or shift viewport storage so (col, row) is addressable, then
     * report what happened. Right/bottom growth appends via the
     * content-preserving resize path; left/top growth inserts rows/cols at
     * index 0 and offsets the server bounds (existing indices shift).
     * Growth adds a small margin to amortize repeated strokes and stops at
     * MAX_DIMENSION: points still outside afterwards are reported via
     * `capped` and must be dropped by the caller. The bounds rect itself is
     * left alone (callers expand it with ensureBoundsFor* in the same undo
     * step). Emits at most one change notification.
     */
    public ensureViewportFor(col: number, row: number): ViewportGrowth {
        const plan = CanvasState.planViewportGrowth(this.width, this.height, col, row);
        if (plan.addedLeft === 0 && plan.addedTop === 0 &&
            plan.addedRight === 0 && plan.addedBottom === 0) {
            return { ...plan, col, row };
        }
        const newWidth = this.width + plan.addedLeft + plan.addedRight;
        const newHeight = this.height + plan.addedTop + plan.addedBottom;
        for (const layer of this.layers) {
            const isOpaqueBgLayer = this.layers[0] === layer;
            const makeCell = (): Cell => ({
                char: '',
                fg: [204, 204, 204],
                bg: isOpaqueBgLayer ? [0, 0, 0] : [-1, -1, -1],
            });
            const leftPad = (): Cell[] => Array.from({ length: plan.addedLeft }, makeCell);
            if (plan.addedRight > 0) {
                for (const r of layer.cells) {
                    while (r.length < this.width + plan.addedRight) r.push(makeCell());
                }
            }
            if (plan.addedLeft > 0) {
                for (const r of layer.cells) r.unshift(...leftPad());
            }
            if (plan.addedTop > 0) {
                const pad: Cell[][] = Array.from({ length: plan.addedTop }, () =>
                    Array.from({ length: newWidth }, makeCell));
                layer.cells = [...pad, ...layer.cells];
            }
            if (plan.addedBottom > 0) {
                while (layer.cells.length < newHeight) {
                    layer.cells.push(Array.from({ length: newWidth }, makeCell));
                }
            }
            if (layer.overflowCells) {
                const shifted = new Map<string, Cell>();
                for (const [key, cell] of layer.overflowCells.entries()) {
                    const parsed = parseCellKey(key);
                    if (!parsed) continue;
                    const c = parsed.col + plan.addedLeft;
                    const r = parsed.row + plan.addedTop;
                    if (c >= 0 && c < newWidth && r >= 0 && r < newHeight) {
                        layer.cells[r][c] = cloneCell(cell);
                    } else {
                        shifted.set(`${c},${r}`, cell);
                    }
                }
                layer.overflowCells = shifted;
            }
        }
        this.width = newWidth;
        this.height = newHeight;
        this.serverBounds = {
            col: this.serverBounds.col + plan.addedLeft,
            row: this.serverBounds.row + plan.addedTop,
            w: this.serverBounds.w,
            h: this.serverBounds.h,
        };
        this.notify();
        return { ...plan, col: col + plan.addedLeft, row: row + plan.addedTop };
    }

    /**
     * Pure growth planner: how much storage to add on each side so that
     * (col, row) fits, without mutating anything. Right/bottom append with
     * a margin; left/top insert exactly what the point needs. All amounts
     * stop at MAX_DIMENSION.
     */
    public static planViewportGrowth(
        width: number, height: number, col: number, row: number,
    ): Omit<ViewportGrowth, 'col' | 'row'> {
        const MARGIN = 8;
        const max = CanvasState.MAX_DIMENSION;
        let addedLeft = 0, addedTop = 0, addedRight = 0, addedBottom = 0;
        if (col < 0) addedLeft = Math.min(-col, max - width);
        else if (col >= width) addedRight = Math.min(col - width + 1 + MARGIN, max - width);
        if (row < 0) addedTop = Math.min(-row, max - height);
        else if (row >= height) addedBottom = Math.min(row - height + 1 + MARGIN, max - height);
        addedLeft = Math.max(0, addedLeft);
        addedTop = Math.max(0, addedTop);
        addedRight = Math.max(0, addedRight);
        addedBottom = Math.max(0, addedBottom);
        const capped = col < -addedLeft || row < -addedTop ||
            col >= width + addedLeft + addedRight ||
            row >= height + addedTop + addedBottom;
        return { addedLeft, addedTop, addedRight, addedBottom, capped };
    }

    constructor(width: number, height: number, initializeBlack: boolean = true) {
        this.width = CanvasState.sanitizeDimension(width, 'width');
        this.height = CanvasState.sanitizeDimension(height, 'height');
        this.layers = [];
        this.activeLayerIndex = 0;
        this.serverBounds = { col: 0, row: 0, w: this.width, h: this.height };
        this.addLayer("Background", initializeBlack);
    }

    public createEmptyCells(initializeBlack: boolean = false): Cell[][] {
        return Array.from({ length: this.height }, () =>
            Array.from({ length: this.width }, () => ({
                char: '',
                fg: [204, 204, 204] as [number, number, number],
                // Use [-1,-1,-1] to represent transparent background for layers above background
                bg: initializeBlack ? [0, 0, 0] as [number, number, number] : [-1, -1, -1] as [number, number, number]
            }))
        );
    }

    public addLayer(name?: string, initializeBlack: boolean = false) {
        this.layerIdCounter++;
        this.layers.push({
            id: `layer-${this.layerIdCounter}`,
            name: name || `Layer ${this.layers.length + 1}`,
            visible: true,
            cells: this.createEmptyCells(initializeBlack),
            overflowCells: new Map()
        });
        this.activeLayerIndex = this.layers.length - 1;
        this.notify();
    }

    public getActiveLayer(): Layer {
        // Lazily recreate the background layer if everything was removed, and
        // clamp a stale index instead of returning undefined.
        if (this.layers.length === 0) {
            this.addLayer("Background", true);
            return this.layers[this.layers.length - 1];
        }
        if (this.activeLayerIndex < 0) this.activeLayerIndex = 0;
        else if (this.activeLayerIndex >= this.layers.length) this.activeLayerIndex = this.layers.length - 1;
        return this.layers[this.activeLayerIndex];
    }

    /**
     * Resolves the final visible cell at (col, row) by blending layers from the top (foreground) 
     * to the bottom (background). Once an opaque background is found, lower layers are obscured.
     */
    public getCompositeCell(col: number, row: number): Cell | null {
        if (col < 0 || col >= this.width || row < 0 || row >= this.height) return null;
        
        let finalChar = '';
        let finalFg: [number, number, number] = [204, 204, 204];
        let finalBg: [number, number, number] = [-1, -1, -1];
        let finalBold: boolean | undefined;
        let finalItalic: boolean | undefined;
        let finalUnderline: boolean | undefined;
        let charFound = false;
        let bgFound = false;

        for (let i = this.layers.length - 1; i >= 0; i--) {
            const layer = this.layers[i];
            if (!layer.visible) continue;
            
            const cell = layer.cells[row][col];
            
            if (!charFound && cell.char && cell.char.trim() !== '') {
                finalChar = cell.char;
                finalFg = [...cell.fg] as [number, number, number];
                finalBold = cell.bold;
                finalItalic = cell.italic;
                finalUnderline = cell.underline;
                charFound = true;
            }
            
            if (!bgFound && cell.bg[0] !== -1) {
                finalBg = [...cell.bg] as [number, number, number];
                bgFound = true;
            }

            if (charFound && bgFound) break;
        }

        if (!bgFound) finalBg = [0, 0, 0];
        
        return { char: finalChar, fg: finalFg, bg: finalBg, bold: finalBold, italic: finalItalic, underline: finalUnderline };
    }

    public getCell(col: number, row: number): Cell | null {
        const layer = this.getActiveLayer();
        if (col < 0 || col >= this.width || row < 0 || row >= this.height) {
            return layer.overflowCells?.get(`${col},${row}`) || null;
        }
        return layer.cells[row][col];
    }

    public setCell(col: number, row: number, cell: Cell, triggerChange: boolean = true) {
        const layer = this.getActiveLayer();
        if (col < 0 || col >= this.width || row < 0 || row >= this.height) {
            if (!layer.overflowCells) layer.overflowCells = new Map();
            const isEmpty = (!cell.char || cell.char.trim() === '') && cell.bg[0] === -1;
            if (isEmpty) {
                layer.overflowCells.delete(`${col},${row}`);
            } else {
                layer.overflowCells.set(`${col},${row}`, { char: cell.char, fg: [...cell.fg] as [number, number, number], bg: [...cell.bg] as [number, number, number], bold: cell.bold, italic: cell.italic, underline: cell.underline });
            }
        } else {
            layer.cells[row][col] = { char: cell.char, fg: [...cell.fg] as [number, number, number], bg: [...cell.bg] as [number, number, number], bold: cell.bold, italic: cell.italic, underline: cell.underline };
        }
        if (triggerChange) {
            this.notify();
        }
    }

    public applyBatch(updates: { col: number, row: number, cell: Cell }[]) {
        let changed = false;
        const layer = this.getActiveLayer();
        for (const u of updates) {
            if (u.col >= 0 && u.col < this.width && u.row >= 0 && u.row < this.height) {
                layer.cells[u.row][u.col] = { char: u.cell.char, fg: [...u.cell.fg] as [number, number, number], bg: [...u.cell.bg] as [number, number, number], bold: u.cell.bold, italic: u.cell.italic, underline: u.cell.underline };
                changed = true;
            } else {
                if (!layer.overflowCells) layer.overflowCells = new Map();
                const isEmpty = (!u.cell.char || u.cell.char.trim() === '') && u.cell.bg[0] === -1;
                if (isEmpty) {
                    if (layer.overflowCells.has(`${u.col},${u.row}`)) {
                         layer.overflowCells.delete(`${u.col},${u.row}`);
                         changed = true;
                    }
                } else {
                    layer.overflowCells.set(`${u.col},${u.row}`, { char: u.cell.char, fg: [...u.cell.fg] as [number, number, number], bg: [...u.cell.bg] as [number, number, number], bold: u.cell.bold, italic: u.cell.italic, underline: u.cell.underline });
                    changed = true;
                }
            }
        }
        if (changed) this.notify();
    }

    public fill(cell: Cell) {
        const layer = this.getActiveLayer();
        for (let row = 0; row < this.height; row++) {
            for (let col = 0; col < this.width; col++) {
                layer.cells[row][col] = {
                    char: cell.char,
                    fg: [...cell.fg] as [number, number, number],
                    bg: [...cell.bg] as [number, number, number],
                    bold: cell.bold,
                    italic: cell.italic,
                    underline: cell.underline
                };
            }
        }
        this.notify();
    }

    public clone(): CanvasState {
        const copy = new CanvasState(this.width, this.height, false);
        copy.layers = [];
        copy.layerIdCounter = this.layerIdCounter;
        copy.activeLayerIndex = this.activeLayerIndex;
        copy.serverBounds = { ...this.serverBounds };
        
        for (const layer of this.layers) {
            const newCells = this.createEmptyCells();
            for (let r = 0; r < this.height; r++) {
                for (let c = 0; c < this.width; c++) {
                    const src = layer.cells[r][c];
                    newCells[r][c] = {
                        char: src.char,
                        fg: [...src.fg] as [number, number, number],
                        bg: [...src.bg] as [number, number, number],
                        bold: src.bold,
                        italic: src.italic,
                        underline: src.underline
                    };
                }
            }
            copy.layers.push({
                id: layer.id,
                name: layer.name,
                visible: layer.visible,
                cells: newCells,
                overflowCells: layer.overflowCells ? new Map(Array.from(layer.overflowCells.entries()).map(([k, cell]) => [k, { char: cell.char, fg: [...cell.fg] as [number, number, number], bg: [...cell.bg] as [number, number, number], bold: cell.bold, italic: cell.italic, underline: cell.underline }])) : new Map()
            });
        }
        return copy;
    }

    public onChange(listener: () => void) {
        this.changeListeners.add(listener);
    }

    public offChange(listener: () => void) {
        this.changeListeners.delete(listener);
    }

    public notify() {
        for (const listener of this.changeListeners) {
            listener();
        }
    }

    public resize(newWidth: number, newHeight: number) {
        newWidth = CanvasState.sanitizeDimension(newWidth, 'width');
        newHeight = CanvasState.sanitizeDimension(newHeight, 'height');
        if (newWidth === this.width && newHeight === this.height) return;

        for (let i = 0; i < this.layers.length; i++) {
            const layer = this.layers[i];
            const isOpaqueBgLayer = i === 0;
            const defaultBg = isOpaqueBgLayer ? [0, 0, 0] : [-1, -1, -1];

            const newCells: Cell[][] = Array.from({ length: newHeight }, () =>
                Array.from({ length: newWidth }, () => ({
                    char: '',
                    fg: [204, 204, 204] as [number, number, number],
                    bg: defaultBg as [number, number, number],
                    bold: false,
                    italic: false,
                    underline: false
                }))
            );

            for (let r = 0; r < newHeight; r++) {
                for (let c = 0; c < newWidth; c++) {
                    if (r < this.height && c < this.width) {
                        const src = layer.cells[r][c];
                        newCells[r][c] = {
                            char: src.char,
                            fg: [...src.fg] as [number, number, number],
                            bg: [...src.bg] as [number, number, number],
                            bold: src.bold,
                            italic: src.italic,
                            underline: src.underline
                        };
                    }
                }
            }

            if (layer.overflowCells) {
                for (const [key, cell] of layer.overflowCells.entries()) {
                    const parsed = parseCellKey(key);
                    if (!parsed) {
                        layer.overflowCells.delete(key);
                        continue;
                    }
                    const c = parsed.col;
                    const r = parsed.row;
                    if (c >= 0 && c < newWidth && r >= 0 && r < newHeight) {
                        newCells[r][c] = {
                            char: cell.char,
                            fg: [...cell.fg] as [number, number, number],
                            bg: [...cell.bg] as [number, number, number],
                            bold: cell.bold,
                            italic: cell.italic,
                            underline: cell.underline
                        };
                        layer.overflowCells.delete(key);
                    }
                }
            }

            layer.cells = newCells;
        }

        this.width = newWidth;
        this.height = newHeight;
        // Viewport growth never moves bounds, but a shrink (e.g. loading an
        // empty map) must keep the bounds⊆storage invariant: clamp in place.
        const b = this.serverBounds;
        b.col = Math.max(0, Math.min(b.col, newWidth - 1));
        b.row = Math.max(0, Math.min(b.row, newHeight - 1));
        b.w = Math.max(1, Math.min(b.w, newWidth - b.col));
        b.h = Math.max(1, Math.min(b.h, newHeight - b.row));
        this.notify();
    }
}
