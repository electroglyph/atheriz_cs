import { CanvasState } from '../state/CanvasState';
import { CellMetrics } from '../utils/fontMetrics';
import { parseCellKey } from '../utils/cellKeys';
import { Cell, Color, ServerBounds } from '../types';

/** Violet server-grid outline: the rect actually sent back to the server. */
export const SERVER_BOUNDS_COLOR = '#A855F7';

/**
 * State origin inside the drawable surface, in cells. The canvas element
 * always covers the whole scroller viewport with the stored map sitting
 * at this inset, so every visible grid square is drawable surface: there
 * is no separate look-but-don't-touch margin. Clicks map through this
 * inset (CanvasController subtracts it) and land as state coords that
 * may sit outside storage — the tools already grow storage in all four
 * directions, so drawing anywhere just works.
 */
export const STATE_ORIGIN_MARGIN = 128;

/** Right/bottom keep past storage when the view auto-covers, in cells. */
const VIEW_STATE_MARGIN = 32;

/** Overscan past the visible scroll area when the view grows, in px. */
const VIEW_OVERSCAN_PX = 256;

export interface CanvasViewSize {
    viewCols: number;
    viewRows: number;
}

/** Cells of breathing room between the viewport edge and the content
 * when the host auto-scrolls content into view. */
const CONTENT_SCROLL_MARGIN_CELLS = 3;

export interface ContentScroll {
    scrollLeft: number;
    scrollTop: number;
}

/**
 * Pure scroll math: put the top-left of the given state rect in view
 * with a small margin, clamped at zero. The host applies it after load
 * and New — without this the editor opens staring at empty margin while
 * the map sits at the view inset, off-screen.
 */
export function planContentScroll(args: {
    offsetCol: number;
    offsetRow: number;
    rectCol: number;
    rectRow: number;
    cellW: number;
    cellH: number;
}): ContentScroll {
    const cw = Number.isFinite(args.cellW) && args.cellW > 0 ? args.cellW : 1;
    const ch = Number.isFinite(args.cellH) && args.cellH > 0 ? args.cellH : 1;
    return {
        scrollLeft: Math.max(0, (args.offsetCol + args.rectCol) * cw - CONTENT_SCROLL_MARGIN_CELLS * cw),
        scrollTop: Math.max(0, (args.offsetRow + args.rectRow) * ch - CONTENT_SCROLL_MARGIN_CELLS * ch),
    };
}

/**
 * Pure planner for the drawable surface size. The view only ever grows:
 * it must cover the state rect at its inset plus a margin, and the
 * scrolled viewport plus overscan. Never shrinks (shrinking would jump
 * the scroll position and strand storage outside the element).
 */
export function planCanvasView(args: {
    stateW: number;
    stateH: number;
    offsetCol: number;
    offsetRow: number;
    viewCols: number;
    viewRows: number;
    scrollX: number;
    scrollY: number;
    clientW: number;
    clientH: number;
    cellW: number;
    cellH: number;
}): CanvasViewSize {
    let needCols = Math.max(args.viewCols, args.offsetCol + args.stateW + VIEW_STATE_MARGIN);
    let needRows = Math.max(args.viewRows, args.offsetRow + args.stateH + VIEW_STATE_MARGIN);
    if (Number.isFinite(args.cellW) && args.cellW > 0) {
        needCols = Math.max(needCols,
            Math.ceil((args.scrollX + args.clientW + VIEW_OVERSCAN_PX) / args.cellW));
    }
    if (Number.isFinite(args.cellH) && args.cellH > 0) {
        needRows = Math.max(needRows,
            Math.ceil((args.scrollY + args.clientH + VIEW_OVERSCAN_PX) / args.cellH));
    }
    return {
        viewCols: Math.max(1, Math.floor(needCols)),
        viewRows: Math.max(1, Math.floor(needRows)),
    };
}

/**
 * Exterior boundary edges of a room-cell set, in cell units. An edge
 * between two room cells is interior (drawn by neither side once the
 * union is stroked); only edges facing a non-room cell are returned, so
 * a deleted interior square can never keep an outline: its border
 * pixels belong to no room anymore. Each edge is [x1, y1, x2, y2].
 */
export function roomBoundaryEdges(cells: Set<string>): Array<[number, number, number, number]> {
    const present = new Set<string>();
    const coords: Array<{ col: number; row: number }> = [];
    for (const key of cells) {
        const parsed = parseCellKey(key);
        if (!parsed || present.has(key)) continue;
        present.add(key);
        coords.push(parsed);
    }
    const edges: Array<[number, number, number, number]> = [];
    for (const { col, row } of coords) {
        if (!present.has(`${col},${row - 1}`)) edges.push([col, row, col + 1, row]);
        if (!present.has(`${col},${row + 1}`)) edges.push([col, row + 1, col + 1, row + 1]);
        if (!present.has(`${col - 1},${row}`)) edges.push([col, row, col, row + 1]);
        if (!present.has(`${col + 1},${row}`)) edges.push([col + 1, row, col + 1, row + 1]);
    }
    return edges;
}

export class GridRenderer {
    private canvas: HTMLCanvasElement;
    private ctx: CanvasRenderingContext2D;
    private state: CanvasState;
    private metrics: CellMetrics;

    // Optional overlays for previewing tool actions
    private previewCells: Map<string, Cell> = new Map();
    private selectedCells: Set<string> = new Set();
    private roomCells: Set<string> = new Set();
    private roomColor: string = '#00CCCC';
    private roomVisible: boolean = true;
    // Violet outline of the server grid: the rect actually sent to the server.
    private serverBounds: ServerBounds | null = null;
    // Drawable surface in cells. Defaults to the storage size at origin
    // (0,0): first paint and unit tests behave exactly like the old
    // storage-sized canvas until the host sets a real view.
    private viewCols: number;
    private viewRows: number;
    // State origin inside the surface, in cells. Fixed once set: left/top
    // inserts shift content within storage, so the origin never moves.
    private offsetCol = 0;
    private offsetRow = 0;

    private renderBound = () => this.render();
    /** Fixed ratio for tests; production follows window.devicePixelRatio. */
    private fixedPixelRatio: number | null = null;

    constructor(canvas: HTMLCanvasElement, state: CanvasState, metrics: CellMetrics, devicePixelRatio?: number) {
        this.canvas = canvas;
        const ctx = canvas.getContext('2d', { alpha: false }); // Optimize for no transparency
        if (!ctx) throw new Error("Could not get 2D context");
        this.ctx = ctx;
        this.state = state;
        this.metrics = metrics;
        this.serverBounds = { ...state.serverBounds };
        this.viewCols = state.width;
        this.viewRows = state.height;
        if (devicePixelRatio !== undefined) this.fixedPixelRatio = devicePixelRatio;

        // Resize the actual canvas element based on state * metrics
        this.resize();
        this.state.onChange(this.renderBound);
        if (this.fixedPixelRatio === null) this.watchPixelRatio();
    }

    /** Backing-store scale: crisp on HiDPI and OS-scaled displays. */
    private currentPixelRatio(): number {
        if (this.fixedPixelRatio !== null) return this.fixedPixelRatio;
        if (typeof window !== 'undefined' && typeof window.devicePixelRatio === 'number' &&
            Number.isFinite(window.devicePixelRatio) && window.devicePixelRatio > 0) {
            return window.devicePixelRatio;
        }
        return 1;
    }

    /** Re-render when the display scale changes (zoom, monitor move). */
    private watchPixelRatio(): void {
        if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return;
        const mq = window.matchMedia(`(resolution: ${this.currentPixelRatio()}dppx)`);
        const onChange = (): void => {
            mq.removeEventListener?.('change', onChange);
            this.resize();
            this.watchPixelRatio();
        };
        mq.addEventListener?.('change', onChange);
    }

    public resize() {
        // The backing store scales with the device pixel ratio while the
        // element stays CSS-px sized: without this the browser upscales a
        // CSS-px bitmap on scaled displays and every canvas line reads
        // softer than the scroller's CSS grid around it. Drawing stays in
        // CSS px under setTransform. The element spans the whole drawable
        // view (viewport coverage), not just storage.
        const dpr = this.currentPixelRatio();
        const cssW = this.viewCols * this.metrics.width;
        const cssH = this.viewRows * this.metrics.height;
        this.canvas.width = Math.max(1, Math.round(cssW * dpr));
        this.canvas.height = Math.max(1, Math.round(cssH * dpr));
        this.canvas.style.width = `${cssW}px`;
        this.canvas.style.height = `${cssH}px`;

        // Setting width/height resets context state, so re-apply transform + font
        this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        this.ctx.font = this.metrics.font;
        this.ctx.textBaseline = "middle";
        this.ctx.textAlign = "center";
        
        this.render();
    }

    public updateState(newState: CanvasState) {
        this.state.offChange(this.renderBound);
        this.state = newState;
        this.serverBounds = { ...newState.serverBounds };
        this.state.onChange(this.renderBound);
        // The view persists across swaps (no scroll jumps) but must still
        // cover the new storage at the fixed inset.
        this.coverState();
        this.resize();
    }

    public updateMetrics(metrics: CellMetrics) {
        this.metrics = metrics;
        this.resize();
    }

    /** Fix the state origin inside the surface (host sets this once from
     * STATE_ORIGIN_MARGIN). Content shifts; the element size is untouched
     * unless the state no longer fits, which coverState repairs. */
    public setViewOrigin(col: number, row: number): void {
        this.offsetCol = Number.isFinite(col) ? Math.max(0, Math.floor(col)) : 0;
        this.offsetRow = Number.isFinite(row) ? Math.max(0, Math.floor(row)) : 0;
        this.coverState();
        this.resize();
    }

    /** Current state origin, for host scroll math (mirrors setViewOrigin). */
    public getViewOrigin(): { offsetCol: number; offsetRow: number } {
        return { offsetCol: this.offsetCol, offsetRow: this.offsetRow };
    }

    /** Grow the view to cover storage at the inset (expand-only). */
    private coverState(): void {
        // No viewport info here (NaN cell dims skip the coverage term):
        // this only repairs the storage half of the invariant.
        const need = planCanvasView({
            stateW: this.state.width,
            stateH: this.state.height,
            offsetCol: this.offsetCol,
            offsetRow: this.offsetRow,
            viewCols: this.viewCols,
            viewRows: this.viewRows,
            scrollX: 0,
            scrollY: 0,
            clientW: 0,
            clientH: 0,
            cellW: NaN,
            cellH: NaN,
        });
        this.viewCols = need.viewCols;
        this.viewRows = need.viewRows;
    }

    /** Grow the view to cover storage plus the scrolled viewport
     * (expand-only). Deliberately state-free: margin extension pushes no
     * undo entry and marks nothing dirty. Returns true when the element
     * was resized. */
    public ensureViewForViewport(scrollX: number, scrollY: number, clientW: number, clientH: number): boolean {
        const need = planCanvasView({
            stateW: this.state.width,
            stateH: this.state.height,
            offsetCol: this.offsetCol,
            offsetRow: this.offsetRow,
            viewCols: this.viewCols,
            viewRows: this.viewRows,
            scrollX,
            scrollY,
            clientW,
            clientH,
            cellW: this.metrics.width,
            cellH: this.metrics.height,
        });
        if (need.viewCols === this.viewCols && need.viewRows === this.viewRows) return false;
        this.viewCols = need.viewCols;
        this.viewRows = need.viewRows;
        this.resize();
        return true;
    }

    public setPreview(cells: {col: number, row: number, cell: Cell}[]) {
        this.previewCells.clear();
        for (const c of cells) {
            this.previewCells.set(`${c.col},${c.row}`, c.cell);
        }
        this.render();
    }

    public clearPreview() {
        if (this.previewCells.size > 0) {
            this.previewCells.clear();
            this.render();
        }
    }

    public setSelection(cells: Set<string>) {
        // Defensive copy: callers keep mutating their set after handing it
        // over, which would otherwise poison the renderer's outline state.
        this.selectedCells = new Set(cells);
        this.render();
    }

    public getSelectedCells(): Set<string> {
        return this.selectedCells;
    }

    public clearSelection() {
        if (this.selectedCells.size > 0) {
            this.selectedCells = new Set();
            this.render();
        }
    }

    public setRoomCells(cells: Set<string>) {
        this.roomCells = cells;
        this.render();
    }

    public getRoomCells(): Set<string> {
        return this.roomCells;
    }

    public setRoomColor(color: string) {
        this.roomColor = color;
        this.render();
    }

    public setRoomVisible(visible: boolean) {
        this.roomVisible = visible;
        this.render();
    }

    /** Refresh the violet server-grid outline (null hides it). */
    public setServerBounds(rect: ServerBounds | null) {
        this.serverBounds = rect ? { ...rect } : null;
        this.render();
    }

    public render() {
        const { width, height } = this.metrics;
        const ox = this.offsetCol * width;
        const oy = this.offsetRow * height;

        this.ctx.fillStyle = '#000000';
        this.ctx.fillRect(0, 0, this.viewCols * width, this.viewRows * height);

        const bgColors = new Map<string, Path2D>();
        const chars = new Map<string, {text: string, x: number, y: number}[]>();
        const underlines: {x: number, y: number, w: number, color: string}[] = [];

        const addBg = (col: number, row: number, r: number, g: number, b: number) => {
            // Transparent markers and plain black both show the cleared canvas.
            if (r < 0 || g < 0 || b < 0) return;
            if (r === 0 && g === 0 && b === 0) return;
            const key = `rgb(${r},${g},${b})`;
            let path = bgColors.get(key);
            if (!path) {
                path = new Path2D();
                bgColors.set(key, path);
            }
            path.rect(col * width, row * height, width, height);
        };

        const fontKey = (r: number, g: number, b: number, opacity: number, bold: boolean, italic: boolean) => {
            const colorKey = opacity === 1.0 ? `rgb(${r},${g},${b})` : `rgba(${r},${g},${b},${opacity})`;
            return `${colorKey}|${bold?'b':''}${italic?'i':''}`;
        };

        const addChar = (col: number, row: number, char: string, r: number, g: number, b: number, opacity: number = 1.0, bold?: boolean, italic?: boolean, underline?: boolean) => {
            if (!char || char === ' ') return;
            const key = fontKey(r, g, b, opacity, !!bold, !!italic);
            let list = chars.get(key);
            if (!list) {
                list = [];
                chars.set(key, list);
            }
            list.push({ text: char, x: col * width + width / 2, y: row * height + height / 2 });

            if (underline) {
                const colorKey = opacity === 1.0 ? `rgb(${r},${g},${b})` : `rgba(${r},${g},${b},${opacity})`;
                underlines.push({ x: col * width, y: row * height + height - 2, w: width, color: colorKey });
            }
        };

        // One surface for the whole view: the loop runs over element
        // cells and reads storage at the inset. Outside storage the
        // composite is null and the square stays empty grid.
        for (let row = 0; row < this.viewRows; row++) {
            for (let col = 0; col < this.viewCols; col++) {
                const scol = col - this.offsetCol;
                const srow = row - this.offsetRow;
                const previewCell = this.previewCells.get(`${scol},${srow}`);
                const baseCell = this.state.getCompositeCell(scol, srow) || { char: '', fg: [204, 204, 204] as [number, number, number], bg: [0, 0, 0] as [number, number, number] };
                const cell = previewCell ?? baseCell;
                const opacity = previewCell ? 0.7 : 1.0;

                // Resolve the transparent-background marker on the preview path
                // exactly like getCompositeCell does, so a raw transparent
                // preview cell never emits invalid CSS such as rgb(-1,-1,-1).
                const bg: Color = cell.bg[0] === -1 ? [0, 0, 0] : cell.bg;
                addBg(col, row, bg[0], bg[1], bg[2]);
                addChar(col, row, cell.char, cell.fg[0], cell.fg[1], cell.fg[2], opacity, cell.bold, cell.italic, cell.underline);
            }
        }

        for (const [color, path] of bgColors.entries()) {
            this.ctx.fillStyle = color;
            this.ctx.fill(path);
        }

        // Enhance grid lines over everything but text.
        // Half-pixel boundaries keep 1px strokes crisp: integer coords
        // would straddle two pixels and smear into a dimmer 2px line.
        this.ctx.strokeStyle = '#222';
        this.ctx.lineWidth = 1;
        this.ctx.beginPath();
        for (let col = 1; col < this.viewCols; col++) {
            this.ctx.moveTo(col * width + 0.5, 0);
            this.ctx.lineTo(col * width + 0.5, this.viewRows * height);
        }
        for (let row = 1; row < this.viewRows; row++) {
            this.ctx.moveTo(0, row * height + 0.5);
            this.ctx.lineTo(this.viewCols * width, row * height + 0.5);
        }
        this.ctx.stroke();

        // Violet server-grid outline, under the room and selection overlays.
        if (this.serverBounds) {
            const b = this.serverBounds;
            this.ctx.strokeStyle = SERVER_BOUNDS_COLOR;
            this.ctx.lineWidth = 2;
            this.ctx.strokeRect(ox + b.col * width, oy + b.row * height, b.w * width, b.h * height);
        }

        const strokeCellOutlines = (cells: Set<string>) => {
            this.ctx.beginPath();
            for (const key of cells) {
                const parsed = parseCellKey(key);
                if (!parsed) continue;
                const col = parsed.col;
                const row = parsed.row;
                const x = ox + col * width;
                const y = oy + row * height;
                this.ctx.moveTo(x, y);
                this.ctx.lineTo(x + width, y);
                this.ctx.moveTo(x + width, y);
                this.ctx.lineTo(x + width, y + height);
                this.ctx.moveTo(x + width, y + height);
                this.ctx.lineTo(x, y + height);
                this.ctx.moveTo(x, y + height);
                this.ctx.lineTo(x, y);
            }
            this.ctx.stroke();
        };

        if (this.roomVisible && this.roomCells.size > 0) {
            // Subtle fill first, so every room cell — interior ones included —
            // reads as a room and a deletion visibly empties the square.
            this.ctx.save();
            this.ctx.globalAlpha = 0.16;
            this.ctx.fillStyle = this.roomColor;
            for (const key of this.roomCells) {
                const parsed = parseCellKey(key);
                if (!parsed) continue;
                this.ctx.fillRect(ox + parsed.col * width, oy + parsed.row * height, width, height);
            }
            this.ctx.restore();
            // Union boundary, not per-cell boxes: shared edges belong to no
            // room once drawn, so a deleted square keeps no outline.
            this.ctx.strokeStyle = this.roomColor;
            this.ctx.lineWidth = 2;
            this.ctx.beginPath();
            for (const [x1, y1, x2, y2] of roomBoundaryEdges(this.roomCells)) {
                this.ctx.moveTo(ox + x1 * width, oy + y1 * height);
                this.ctx.lineTo(ox + x2 * width, oy + y2 * height);
            }
            this.ctx.stroke();
        }

        if (this.selectedCells.size > 0) {
            this.ctx.strokeStyle = '#FFCC00';
            this.ctx.lineWidth = 2;
            strokeCellOutlines(this.selectedCells);
        }

        const baseFont = this.metrics.font;

        for (const [key, list] of chars.entries()) {
            const [colorKey] = key.split('|');
            const hasBold = key.includes('|b');
            const hasItalic = key.includes('i');
            let fontStr = '';
            if (hasBold && hasItalic) fontStr = `bold italic ${baseFont}`;
            else if (hasBold) fontStr = `bold ${baseFont}`;
            else if (hasItalic) fontStr = `italic ${baseFont}`;
            else fontStr = baseFont;

            this.ctx.font = fontStr;
            this.ctx.fillStyle = colorKey;
            for (const item of list) {
                this.ctx.fillText(item.text, item.x, item.y);
            }
        }

        if (underlines.length > 0) {
            this.ctx.lineWidth = 1;
            this.ctx.beginPath();
            for (const ul of underlines) {
                this.ctx.strokeStyle = ul.color;
                this.ctx.moveTo(ul.x, ul.y);
                this.ctx.lineTo(ul.x + ul.w, ul.y);
            }
            this.ctx.stroke();
        }

        this.ctx.font = baseFont;
    }
}
