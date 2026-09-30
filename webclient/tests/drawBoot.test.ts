// @vitest-environment jsdom
// Boot harness: runs the real initApp() in jsdom with a stubbed layout
// and 2D context, proving the boot wiring unit tests cannot see — the
// view grows to cover the viewport and the loaded grid is scrolled into
// view instead of opening on empty margin.
import { describe, it, expect, vi, beforeEach } from 'vitest';

const MAIN_IDS = [
    'btn-create-rooms',
    'btn-delete-rooms',
    'btn-edit-legend',
    'btn-preview',
    'btn-room-toggle',
    'room-color-swatch',
    'btn-room-color',
    'btn-load-image',
    'btn-load-ansi',
    'btn-save-server',
    'btn-color-adjust',
    'btn-zoom-in',
    'btn-zoom-out',
    'char-palette',
    'fg-picker-container',
    'bg-picker-container',
    'gradient-picker-container',
    'layer-manager-container',
    'room-editor-container',
    'sidebar',
    'sidebar-resizer',
    'right-sidebar',
    'right-sidebar-resizer',
];

const EXTRA_IDS = [
    'btn-export', 'btn-import-cancel', 'btn-import-confirm', 'btn-new',
    'btn-new-cancel', 'btn-new-confirm', 'btn-redo', 'btn-resize',
    'btn-resize-cancel', 'btn-resize-confirm', 'btn-text-cancel',
    'btn-text-confirm', 'btn-undo', 'chafa-options-container',
    'char-scan-status', 'color-adjust-all-layers', 'color-adjust-brightness',
    'color-adjust-brightness-val', 'color-adjust-cancel', 'color-adjust-contrast',
    'color-adjust-contrast-val', 'color-adjust-hue', 'color-adjust-hue-val',
    'color-adjust-modal', 'color-adjust-ok', 'color-adjust-saturation',
    'color-adjust-saturation-val', 'color-picker-modal', 'cp-b', 'cp-cancel',
    'cp-g', 'cp-hex', 'cp-hue-bar', 'cp-ok', 'cp-preview', 'cp-r', 'cp-sv-field',
    'eyedropper-target-select', 'fill-mode-select', 'font-select', 'gfp-cancel',
    'gfp-list', 'gfp-ok', 'gfp-search', 'gfp-sentinel', 'gfp-tabs',
    'google-font-picker-modal', 'gradient-target-select', 'image-import-modal',
    'image-upload', 'import-error', 'import-height', 'import-width',
    'legend-add-btn', 'legend-cancel-btn', 'legend-editor-list',
    'legend-editor-modal', 'legend-save-btn', 'line-diagonal-checkbox',
    'line-mode-select', 'new-canvas-modal', 'new-height', 'new-width',
    'oval-mode-select', 'preview-window', 'rect-mode-select',
    'resize-canvas-modal', 'resize-height', 'resize-width', 'rotate-mode-select',
    'select-mode-select', 'text-chafa-options-container', 'text-tool-align',
    'text-tool-font', 'text-tool-google-fonts-btn', 'text-tool-height-chars',
    'text-tool-input', 'text-tool-modal', 'text-tool-preview',
    'text-tool-stretch', 'text-tool-stretch-val', 'text-tool-style',
    'text-tool-width-chars', 'tool-brush', 'tool-erase', 'tool-eyedropper',
    'tool-fill', 'tool-gradient', 'tool-line', 'tool-move', 'tool-oval',
    'tool-rect', 'tool-rotate', 'tool-select', 'tool-text', 'tool-type',
    'type-style-select', 'type-tool-cancel', 'type-tool-input',
    'type-tool-modal', 'type-tool-ok', 'char-map-modal',
    'char-map-scroll-container', 'char-map-inner', 'char-map-selection',
    'btn-char-cancel', 'btn-char-confirm', 'move-denied-modal',
    'move-denied-modal-message', 'move-denied-modal-ok', 'map-error-modal',
    'map-error-modal-message', 'map-error-modal-ok', 'confirm-modal',
    'confirm-modal-message', 'confirm-modal-ok', 'confirm-modal-cancel',
];

function tagFor(id: string): string {
    if (id === 'text-tool-input') return 'textarea';
    if (id === 'text-tool-preview') return 'canvas';
    if (id.includes('upload')) return 'input';
    if (id === 'gfp-search' || id === 'cp-hex' || id === 'type-tool-input') return 'input';
    if (id === 'line-diagonal-checkbox' || id === 'color-adjust-all-layers') return 'input';
    if (/^(cp-[rgb]|new-(width|height)|resize-(width|height)|import-(width|height)|text-tool-(width|height)-chars|color-adjust-(brightness|contrast|hue|saturation))$/.test(id)) return 'input';
    if (id.endsWith('-select') || id === 'text-tool-font' || id === 'text-tool-style' || id === 'text-tool-align' || id === 'font-select') return 'select';
    if (/^(btn-|.*-(cancel|confirm|ok|save-btn|add-btn))/.test(id) || id === 'gfp-ok') return 'button';
    return 'div';
}

function absorbing2DContext(): unknown {
    return new Proxy(
        {},
        {
            get: (_t, p) => {
                if (p === 'measureText') return () => ({});
                if (p === 'getImageData') return () => ({ data: [] });
                if (p === 'getContextAttributes') return () => ({ alpha: true });
                return () => undefined;
            },
            set: () => true,
        },
    );
}

function installStubs(): void {
    let html = `<div id="canvas-container"><canvas id="main-canvas"></canvas></div>`;
    for (const id of [...MAIN_IDS, ...EXTRA_IDS]) {
        html += `<${tagFor(id)} id="${id}"></${tagFor(id)}>`;
    }
    document.body.innerHTML = html;
    // ColorAdjustDialog drags its modal by .modal-content > h2.
    const colorModal = document.getElementById('color-adjust-modal');
    if (colorModal) colorModal.innerHTML = '<div class="modal-content"><h2>Color</h2></div>';
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
        absorbing2DContext() as RenderingContext,
    );
    vi.stubGlobal('IntersectionObserver', class {
        observe(): void {}
        unobserve(): void {}
        disconnect(): void {}
    });
    (document as unknown as Record<string, unknown>).fonts = {
        ready: Promise.resolve(),
        load: async () => [],
        check: () => true,
    };
    localStorage.clear();
}

async function bootDrawApp(): Promise<void> {
    await import('../src/main');
    // initApp is async behind document.fonts.ready: poll until the boot
    // wiring under test has run (bounded; failure surfaces as timeout).
    await vi.waitFor(() => {
        const scroller = document.getElementById('canvas-container');
        if (!scroller || scroller.scrollLeft === 0) throw new Error('boot scroll pending');
    });
}

describe('draw boot wiring', () => {
    beforeEach(() => {
        vi.restoreAllMocks();
        vi.resetModules();
        installStubs();
    });

    it('grows the view past storage and scrolls the grid into view', async () => {
        await bootDrawApp();
        const canvas = document.getElementById('main-canvas') as HTMLCanvasElement;
        const scroller = document.getElementById('canvas-container') as HTMLElement;
        // No grant: 24x24 boot state, fallback 11x22 metrics, 128 inset.
        // View covers inset + storage + margin.
        expect(canvas.width).toBe((128 + 24 + 32) * 11);
        expect(canvas.height).toBe((128 + 24 + 32) * 22);
        // Scrolled so the state origin shows with a 3-cell margin.
        expect(scroller.scrollLeft).toBe(128 * 11 - 3 * 11);
        expect(scroller.scrollTop).toBe(128 * 22 - 3 * 22);
    });

    it('a rect dragged past storage grows it and mirrors the expanded grid', async () => {        await bootDrawApp();
        // Live module copies (post-resetModules): spies must target these,
        // not the statically-imported ones.
        const { GridRenderer } = await import('../src/canvas/GridRenderer');
        const { RectangleTool } = await import('../src/tools/RectangleTool');
        const canvas = document.getElementById('main-canvas') as HTMLCanvasElement;
        // Layout stub: the element the boot sized (184 cells of 11x22).
        vi.spyOn(canvas, 'getBoundingClientRect').mockReturnValue({
            left: 0, top: 0, right: 2024, bottom: 4048, width: 2024, height: 4048,
            x: 0, y: 0, toJSON: () => ({}),
        } as unknown as DOMRect);
        const mirrorSpy = vi.spyOn(GridRenderer.prototype, 'setServerBounds');
        const rectUpSpy = vi.spyOn(RectangleTool.prototype, 'onMouseUp');
        const widthBefore = canvas.width;

        // Switch to the rect tool through the real toolbar button.
        document.getElementById('tool-rect')!.dispatchEvent(
            new MouseEvent('click', { bubbles: true }));

        // Drag from state (5,5) to state (40,40): past the 24x24 storage.
        // Element px = (128 + state) * cell + 2.
        const px = (s: number) => (128 + s) * 11 + 2;
        const py = (s: number) => (128 + s) * 22 + 2;
        canvas.dispatchEvent(new MouseEvent('mousedown', {
            button: 0, clientX: px(5), clientY: py(5),
        }));
        window.dispatchEvent(new MouseEvent('mousemove', {
            clientX: px(40), clientY: py(40),
        }));
        window.dispatchEvent(new MouseEvent('mouseup', {
            button: 0, clientX: px(40), clientY: py(40),
        }));

        // The gesture reached the rect tool (not the default brush).
        expect(rectUpSpy).toHaveBeenCalledTimes(1);
        // Storage grew right/bottom past col/row 40.
        expect(canvas.width).toBeGreaterThan(widthBefore);
        // The expanded violet grid was mirrored into the renderer.
        expect(mirrorSpy).toHaveBeenCalled();
        const lastRect = mirrorSpy.mock.calls[mirrorSpy.mock.calls.length - 1][0] as {
            col: number; row: number; w: number; h: number;
        };
        expect(lastRect.col).toBeLessThanOrEqual(0);
        expect(lastRect.row).toBeLessThanOrEqual(0);
        expect(lastRect.col + lastRect.w).toBeGreaterThan(40);
        expect(lastRect.row + lastRect.h).toBeGreaterThan(40);
    });

    it('a denied room move restores its squares and keeps later strokes', async () => {
        // Fake draw socket: main constructs the session with `new
        // WebSocket(url)`, so stub the global and drive the wire by hand.
        const instances: Array<{
            sent: string[];
            open(): void;
            receive(data: string): void;
        }> = [];
        vi.stubGlobal('WebSocket', class {
            readyState = 0;
            onopen: ((e: Event) => void) | null = null;
            onclose: ((e: CloseEvent) => void) | null = null;
            onerror: ((e: Event) => void) | null = null;
            onmessage: ((e: MessageEvent) => void) | null = null;
            sent: string[] = [];
            constructor() { instances.push(this as never); }
            send(data: string): void { this.sent.push(data); }
            close(): void { this.readyState = 3; }
            open(): void { this.readyState = 1; this.onopen?.(new Event('open')); }
            receive(data: string): void {
                this.onmessage?.(new MessageEvent('message', { data }));
            }
        });
        // Seed the launch grant: one room with one glyph, no network.
        localStorage.setItem('atheriz_draw_grant', JSON.stringify({
            key: 'K0',
            payload: {
                area: 'T', z: 0,
                grid: [[0, 0, 'A']],
                rooms: [{ x: 0, y: 0, desc: 'R', exits: [] }],
            },
        }));
        localStorage.setItem('atheriz_draw_grant_ts', String(Date.now()));
        await bootDrawApp();
        expect(instances).toHaveLength(1);
        const socket = instances[0];
        socket.open();
        socket.receive('["map_ack",[0,"K1"],{}]');

        const { CanvasState } = await import('../src/state/CanvasState');
        const applySpy = vi.spyOn(CanvasState.prototype, 'applyBatch');
        const setSpy = vi.spyOn(CanvasState.prototype, 'setCell');
        const canvas = document.getElementById('main-canvas') as HTMLCanvasElement;
        // Loaded 2x2 storage at the 128 inset, 11x22 fallback metrics.
        vi.spyOn(canvas, 'getBoundingClientRect').mockReturnValue({
            left: 0, top: 0, right: 1782, bottom: 4048, width: 1782, height: 4048,
            x: 0, y: 0, toJSON: () => ({}),
        } as unknown as DOMRect);
        // Element px for state (col,row): (128+col)*11+2, (128+row)*22+2.
        const px = (c: number) => (128 + c) * 11 + 2;
        const py = (r: number) => (128 + r) * 22 + 2;

        // Drag the room glyph from state (0,1) to (2,1) with the move tool.
        document.getElementById('tool-move')!.dispatchEvent(
            new MouseEvent('click', { bubbles: true }));
        canvas.dispatchEvent(new MouseEvent('mousedown', { button: 0, clientX: px(0), clientY: py(1) }));
        window.dispatchEvent(new MouseEvent('mousemove', { clientX: px(2), clientY: py(1) }));
        window.dispatchEvent(new MouseEvent('mouseup', { button: 0, clientX: px(2), clientY: py(1) }));
        expect(socket.sent.some((s) => s.includes('"map_validate_moves"'))).toBe(true);

        // A later brush stroke elsewhere, before the deny lands.
        document.getElementById('tool-brush')!.dispatchEvent(
            new MouseEvent('click', { bubbles: true }));
        canvas.dispatchEvent(new MouseEvent('mousedown', { button: 0, clientX: px(0), clientY: py(0) }));
        window.dispatchEvent(new MouseEvent('mouseup', { button: 0, clientX: px(0), clientY: py(0) }));

        // Server denies the move.
        socket.receive('["moves_denied",[1,"K2",[0]],{}]');

        // The restore batch ran last: denied squares repainted, the later
        // stroke's square untouched by it.
        const lastBatch = applySpy.mock.calls[applySpy.mock.calls.length - 1][0] as Array<{
            col: number; row: number; cell: { char: string };
        }>;
        const at = (c: number, r: number) =>
            lastBatch.filter((u) => u.col === c && u.row === r).map((u) => u.cell.char);
        expect(at(0, 1)).toEqual(['A']);
        expect(at(2, 1)).toEqual(['']);
        expect(at(0, 0)).toEqual([]);
        // ...while the brush stroke itself was painted beforehand
        // (single clicks go through setCell, not applyBatch).
        expect(setSpy).toHaveBeenCalledWith(0, 0, expect.objectContaining({ char: '█' }));
        // Dialog surfaced without asserting a cause.
        const msg = document.getElementById('move-denied-modal-message')!.textContent ?? '';
        expect(msg).toContain('rejected');
        expect(msg).not.toContain('occupied');
    });
});
