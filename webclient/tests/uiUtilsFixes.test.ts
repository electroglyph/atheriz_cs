// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

class MockIntersectionObserver {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
}

beforeEach(() => {
    (globalThis as unknown as { IntersectionObserver: unknown }).IntersectionObserver =
        MockIntersectionObserver as unknown as typeof IntersectionObserver;
});

afterEach(() => {
    document.body.innerHTML = '';
    document.head.querySelectorAll('link[rel="stylesheet"]').forEach((el) => el.remove());
    localStorage.clear();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
});

describe('U1 modalHelper stacks unknown modals by class', () => {
    it('hides legend-editor and map-error modals when opening another modal', async () => {
        const { closeOtherModals } = await import('../src/ui/modalHelper');
        document.body.innerHTML = `
            <div id="text-tool-modal" class="modal"></div>
            <div id="legend-editor-modal" class="modal"></div>
            <div id="map-error-modal" class="modal"></div>
            <div id="color-adjust-modal" class="modal"></div>`;
        closeOtherModals('text-tool-modal');
        expect(document.getElementById('text-tool-modal')!.classList.contains('hidden')).toBe(false);
        expect(document.getElementById('legend-editor-modal')!.classList.contains('hidden')).toBe(true);
        expect(document.getElementById('map-error-modal')!.classList.contains('hidden')).toBe(true);
        expect(document.getElementById('color-adjust-modal')!.classList.contains('hidden')).toBe(true);
    });

    it('hides future .modal ids without a hardcoded-list update', async () => {
        const { closeOtherModals, visibleModalIds } = await import('../src/ui/modalHelper');
        document.body.innerHTML = `
            <div id="char-map-modal" class="modal"></div>
            <div id="some-future-modal" class="modal"></div>`;
        expect(visibleModalIds().sort()).toEqual(['char-map-modal', 'some-future-modal']);
        closeOtherModals('char-map-modal');
        expect(document.getElementById('some-future-modal')!.classList.contains('hidden')).toBe(true);
        expect(document.getElementById('char-map-modal')!.classList.contains('hidden')).toBe(false);
    });

    it('respects keepIds for stacked sub-dialogs', async () => {
        const { closeOtherModals } = await import('../src/ui/modalHelper');
        document.body.innerHTML = `
            <div id="text-tool-modal" class="modal"></div>
            <div id="google-font-picker-modal" class="modal hidden"></div>`;
        document.getElementById('google-font-picker-modal')!.classList.remove('hidden');
        closeOtherModals('google-font-picker-modal', ['text-tool-modal']);
        expect(document.getElementById('text-tool-modal')!.classList.contains('hidden')).toBe(false);
        expect(document.getElementById('google-font-picker-modal')!.classList.contains('hidden')).toBe(false);
    });
});

describe('U2 CharMapDialog escapeCss fallback', () => {
    it('escapes quotes, brackets and whitespace without CSS.escape', async () => {
        const originalCSS = (globalThis as unknown as { CSS?: unknown }).CSS;
        (globalThis as unknown as { CSS?: unknown }).CSS = undefined;
        try {
            const { escapeCss } = await import('../src/ui/CharMapDialog');
            // Codepoint-hex escapes with a terminating space delimiter.
            expect(escapeCss('"')).toBe('\\000022 ');
            expect(escapeCss(']')).toBe('\\00005d ');
            expect(escapeCss(' ')).toBe('\\000020 ');
            expect(escapeCss('ABC-09_')).toBe('ABC-09_');
            // The escaped form must be usable inside a quoted attribute selector.
            expect(() => document.querySelector(`[data-x="${escapeCss('a"b c]d')}"]`)).not.toThrow();
        } finally {
            (globalThis as unknown as { CSS?: unknown }).CSS = originalCSS;
        }
    });

    it('normalizes empty/non-string font families to the fallback', async () => {
        const { normalizeCharMapFont, CHAR_MAP_FALLBACK_FONT } = await import('../src/ui/CharMapDialog');
        expect(normalizeCharMapFont('')).toBe(CHAR_MAP_FALLBACK_FONT);
        expect(normalizeCharMapFont('   ')).toBe(CHAR_MAP_FALLBACK_FONT);
        expect(normalizeCharMapFont(null)).toBe(CHAR_MAP_FALLBACK_FONT);
        expect(normalizeCharMapFont(undefined)).toBe(CHAR_MAP_FALLBACK_FONT);
        expect(normalizeCharMapFont('Arial')).toBe('Arial');
    });

    function mountCharMap(): void {
        document.body.innerHTML = `
            <div id="char-map-modal" class="modal hidden">
                <div id="char-map-scroll-container"><div id="char-map-inner"></div></div>
                <div id="char-map-selection"></div>
                <button id="btn-char-cancel"></button><button id="btn-char-confirm"></button>
                <div id="char-scan-status"></div>
            </div>`;
    }

    it('throws a clean error naming the missing element', async () => {
        const { CharMapDialog } = await import('../src/ui/CharMapDialog');
        document.body.innerHTML = '';
        expect(() => new CharMapDialog(() => {})).toThrow(/char-map-modal/);
    });

    it('scans the fallback font when opened with an empty family', async () => {
        const { CharMapDialog } = await import('../src/ui/CharMapDialog');
        const { GlyphScanner } = await import('../src/utils/GlyphScanner');
        mountCharMap();
        const scan = vi.spyOn(GlyphScanner, 'scanFont').mockResolvedValue([65]);
        const dialog = new CharMapDialog(() => {});
        await dialog.open('   ');
        expect(scan).toHaveBeenCalledWith('monospace', expect.any(Function));
        dialog.destroy();
    });

    it('destroy() removes its scroll listener and invalidates scans', async () => {
        const { CharMapDialog } = await import('../src/ui/CharMapDialog');
        mountCharMap();
        const dialog = new CharMapDialog(() => {});
        const scroller = (dialog as unknown as { scrollContainer: HTMLElement }).scrollContainer;
        const removeSpy = vi.spyOn(scroller, 'removeEventListener');
        dialog.destroy();
        expect(removeSpy).toHaveBeenCalledWith('scroll', expect.any(Function));
        dialog.destroy();
        expect(removeSpy).toHaveBeenCalledTimes(1);
    });
});

describe('U3 launch storage guards and popup cap', () => {
    it('readDrawGrant returns null when storage throws', async () => {
        const { readDrawGrant } = await import('../src/webclient/launch');
        vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
            throw new Error('denied');
        });
        expect(readDrawGrant()).toBeNull();
    });

    it('clearDrawGrant never throws when storage throws', async () => {
        const { clearDrawGrant } = await import('../src/webclient/launch');
        vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
            throw new Error('denied');
        });
        expect(() => clearDrawGrant()).not.toThrow();
    });

    it('reuses a single popup-fallback div across blocked launches', async () => {
        const launch = await import('../src/webclient/launch');
        launch.__resetLaunchThrottleForTests();
        vi.spyOn(window, 'open').mockReturnValue(null);
        launch.__resetLaunchThrottleForTests();
        expect(launch.launchDraw()).toBe(false);
        launch.__resetLaunchThrottleForTests();
        expect(launch.launchDraw()).toBe(false);
        expect(document.querySelectorAll('.popup-fallback').length).toBe(1);
    });
});

describe('U3 history load caps', () => {
    it('slices stored entries to maxSize and truncates oversized entries', async () => {
        const { CommandHistory, MAX_ENTRY_SIZE } = await import('../src/webclient/history');
        expect(MAX_ENTRY_SIZE).toBe(4096);
        const seed = Array.from({ length: 30 }, (_, i) => `cmd-${i}`);
        seed.push('x'.repeat(9000));
        localStorage.setItem('uifix-history', JSON.stringify(seed));
        const history = new CommandHistory('uifix-history', 10);
        const items = (history as unknown as { history: string[] }).history;
        expect(items.length).toBeLessThanOrEqual(10);
        for (const item of items) expect(item.length).toBeLessThanOrEqual(MAX_ENTRY_SIZE);
    });

    it('truncates oversized entries added at runtime', async () => {
        const { CommandHistory, MAX_ENTRY_SIZE } = await import('../src/webclient/history');
        const history = new CommandHistory('uifix-history-2', 10);
        history.add('y'.repeat(9000));
        const items = (history as unknown as { history: string[] }).history;
        expect(items[0].length).toBe(MAX_ENTRY_SIZE);
    });
});

describe('U4 numeric and color input validation', () => {
    it('parseColorChannel accepts 0..255 integers only', async () => {
        const { parseColorChannel } = await import('../src/ui/ColorPicker');
        expect(parseColorChannel('0')).toBe(0);
        expect(parseColorChannel('255')).toBe(255);
        expect(parseColorChannel('128')).toBe(128);
        expect(parseColorChannel('')).toBeNull();
        expect(parseColorChannel('abc')).toBeNull();
        expect(parseColorChannel('256')).toBeNull();
        expect(parseColorChannel('-1')).toBeNull();
        expect(parseColorChannel('1.5')).toBeNull();
        expect(parseColorChannel('NaN')).toBeNull();
        expect(parseColorChannel('0x10')).toBeNull();
        expect(parseColorChannel(' 42 ')).toBe(42);
    });

    it('parseModalColorChannel accepts 0..255 integers only', async () => {
        const { parseModalColorChannel } = await import('../src/ui/ColorPickerModal');
        expect(parseModalColorChannel('255')).toBe(255);
        expect(parseModalColorChannel('300')).toBeNull();
        expect(parseModalColorChannel('-5')).toBeNull();
        expect(parseModalColorChannel('')).toBeNull();
        expect(parseModalColorChannel('12.5')).toBeNull();
    });

    it('ColorPicker ignores out-of-range RGB commits', async () => {
        const { ColorPicker } = await import('../src/ui/ColorPicker');
        document.body.innerHTML = '<div id="uifix-fg"></div>';
        const appState = { fgColor: [10, 20, 30], bgColor: [0, 0, 0] } as unknown as import('../src/types').AppState;
        const picker = new ColorPicker('uifix-fg', true, appState, () => {});
        const inputs = (picker as unknown as { rInput: HTMLInputElement; gInput: HTMLInputElement; bInput: HTMLInputElement });
        inputs.rInput.value = '999';
        inputs.gInput.value = '20';
        inputs.bInput.value = '30';
        inputs.rInput.dispatchEvent(new Event('change'));
        expect(appState.fgColor).toEqual([10, 20, 30]);
        inputs.rInput.value = '100';
        inputs.rInput.dispatchEvent(new Event('change'));
        expect(appState.fgColor).toEqual([100, 20, 30]);
        picker.destroy();
    });

    it('parseCanvasDim accepts 1..2048 integers only', async () => {
        const { parseCanvasDim, CANVAS_DIM_MIN, CANVAS_DIM_MAX } = await import('../src/ui/NewCanvasDialog');
        expect(CANVAS_DIM_MIN).toBe(1);
        expect(CANVAS_DIM_MAX).toBe(2048);
        expect(parseCanvasDim('24')).toBe(24);
        expect(parseCanvasDim('2048')).toBe(2048);
        expect(parseCanvasDim('0')).toBeNull();
        expect(parseCanvasDim('-3')).toBeNull();
        expect(parseCanvasDim('2049')).toBeNull();
        expect(parseCanvasDim('')).toBeNull();
        expect(parseCanvasDim('abc')).toBeNull();
        expect(parseCanvasDim('12.5')).toBeNull();
        expect(parseCanvasDim('NaN')).toBeNull();
    });

    it('parseSliderInt falls back to 0 for non-finite input', async () => {
        const { parseSliderInt } = await import('../src/ui/ColorAdjustDialog');
        expect(parseSliderInt('10')).toBe(10);
        expect(parseSliderInt('-20')).toBe(-20);
        expect(parseSliderInt('')).toBe(0);
        expect(parseSliderInt('abc')).toBe(0);
    });

    it('GradientPicker zero-width bar inserts at t=0 instead of NaN', async () => {
        const { GradientPicker } = await import('../src/ui/GradientPicker');
        const { ColorPickerModal } = await import('../src/ui/ColorPickerModal');
        document.body.innerHTML = '<div id="uifix-grad"></div>';
        const appState = { gradientStops: [[0, 0, 0], [255, 255, 255]] } as unknown as import('../src/types').AppState;
        const picker = new GradientPicker('uifix-grad', appState);
        vi.spyOn(ColorPickerModal, 'getInstance').mockReturnValue({
            open: async () => [1, 2, 3],
        } as unknown as InstanceType<typeof ColorPickerModal>);
        const bar = (picker as unknown as { previewBar: HTMLElement }).previewBar;
        bar.getBoundingClientRect = () => ({ left: 0, top: 0, width: 0, height: 10, right: 0, bottom: 10, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect;
        (picker as unknown as { onBarClick(e: MouseEvent): void }).onBarClick(new MouseEvent('click', { clientX: 50 }));
        await new Promise((resolve) => setTimeout(resolve, 0));
        expect(appState.gradientStops.length).toBe(3);
        expect(appState.gradientStops[1]).toEqual([1, 2, 3]);
    });

    it('ImageImportDialog rejects out-of-range confirm dims with an error', async () => {
        const { ImageImportDialog } = await import('../src/ui/ImageImportDialog');
        document.body.innerHTML = `
            <div id="image-import-modal" class="modal hidden">
                <input id="image-upload" type="file" />
                <input id="import-width" value="80" />
                <input id="import-height" value="40" />
                <div id="chafa-options-container"></div>
                <div id="import-error" style="display:none"></div>
                <button id="btn-import-cancel"></button>
                <button id="btn-import-confirm"></button>
            </div>`;
        const onConfirm = vi.fn();
        const dialog = new ImageImportDialog(onConfirm);
        const inner = dialog as unknown as {
            currentBuffer: ArrayBuffer | null;
            inputW: HTMLInputElement;
            inputH: HTMLInputElement;
            errorEl: HTMLElement | null;
        };
        inner.currentBuffer = new ArrayBuffer(8);
        inner.inputW.value = '99999';
        inner.inputH.value = '40';
        (document.getElementById('btn-import-confirm') as HTMLButtonElement).click();
        expect(onConfirm).not.toHaveBeenCalled();
        expect(inner.errorEl!.style.display).toBe('block');
        inner.inputW.value = '80';
        (document.getElementById('btn-import-confirm') as HTMLButtonElement).click();
        expect(onConfirm).toHaveBeenCalledTimes(1);
        expect(onConfirm.mock.calls[0][1]).toBe(80);
        expect(onConfirm.mock.calls[0][2]).toBe(40);
    });
});

describe('U5 font and network races', () => {
    function stubFonts(load: () => Promise<unknown>): void {
        Object.defineProperty(document, 'fonts', {
            value: { load, ready: Promise.resolve() },
            configurable: true,
        });
    }

    afterEach(() => {
        delete (document as unknown as Record<string, unknown>).fonts;
    });

    it('loadFontFull signals success as a boolean', async () => {
        stubFonts(async () => []);
        const { loadFontFull } = await import('../src/utils/googleFontLoader');
        await expect(loadFontFull('UiFixFontSuccess')).resolves.toBe(true);
        await expect(loadFontFull('UiFixFontSuccess')).resolves.toBe(true);
    });

    it('loadFontFull signals failure as false when the font load throws', async () => {
        stubFonts(async () => {
            throw new Error('nope');
        });
        const { loadFontFull } = await import('../src/utils/googleFontLoader');
        await expect(loadFontFull('UiFixFontMissing')).resolves.toBe(false);
    });

    it('loadFontPreview encodes the full family value in the stylesheet URL', async () => {
        stubFonts(async () => []);
        const { loadFontPreview } = await import('../src/utils/googleFontLoader');
        loadFontPreview('Encode Test;Fam X');
        await vi.waitFor(() => {
            expect(document.head.querySelectorAll('link[rel="stylesheet"]').length).toBeGreaterThan(0);
        });
        const href = (document.head.querySelector('link[rel="stylesheet"]') as HTMLLinkElement).href;
        expect(href).toContain('family=Encode%20Test%3BFam%20X');
        expect(href).not.toContain(' ');
    });

    it('GlyphScanner caps its cache and evicts the oldest entries', async () => {
        const { GlyphScanner } = await import('../src/utils/GlyphScanner');
        expect(GlyphScanner.MAX_CACHE_ENTRIES).toBe(500);
        const api = GlyphScanner as unknown as {
            storeInCache(f: string, g: number[]): void;
            cache: Map<string, number[]>;
        };
        api.cache.clear();
        for (let i = 0; i < 600; i++) api.storeInCache(`fam-${i}`, [i]);
        expect(api.cache.size).toBeLessThanOrEqual(500);
        expect(api.cache.has('fam-599')).toBe(true);
        expect(api.cache.has('fam-0')).toBe(false);
        api.cache.clear();
    });

    it('GlyphScanner still scans normally after cancel() with no work in flight', async () => {
        const { GlyphScanner } = await import('../src/utils/GlyphScanner');
        vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => setTimeout(() => cb(0), 0));
        Object.defineProperty(document, 'fonts', {
            value: { load: async () => [] },
            configurable: true,
        });
        const fakeCtx = {
            font: '',
            textBaseline: '',
            measureText: (ch: string) => {
                const cp = ch.codePointAt(0);
                return cp === 0xffff || cp === 0x1ffff || cp === 0x10ffff
                    ? { width: 7, actualBoundingBoxLeft: 0, actualBoundingBoxRight: 7 }
                    : { width: 10, actualBoundingBoxLeft: 0, actualBoundingBoxRight: 10 };
            },
        };
        vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(fakeCtx as unknown as CanvasRenderingContext2D);
        GlyphScanner.cancel();
        const glyphs = await GlyphScanner.scanFont('UiFixPostCancelFont', () => {});
        expect(glyphs.length).toBeGreaterThan(0);
        GlyphScanner.cancel();
    });
});

describe('U6 listener, timer and palette lifecycle', () => {
    function mountTextTool(): void {
        document.body.innerHTML = `
            <div id="text-tool-modal" class="modal hidden"></div>
            <textarea id="text-tool-input"></textarea>
            <select id="text-tool-font"></select>
            <select id="text-tool-style"><option value="normal">Normal</option></select>
            <input id="text-tool-max-width" /><span id="text-tool-max-width-val"></span>
            <input id="text-tool-stretch" /><span id="text-tool-stretch-val"></span>
            <canvas id="text-tool-preview"></canvas>
            <button id="btn-text-cancel"></button><button id="btn-text-confirm"></button>
            <button id="text-tool-google-fonts-btn"></button>
            <select id="text-tool-align"><option value="left">Left</option></select>
            <div id="text-chafa-options-container"></div>
            <div id="google-font-picker-modal" class="modal hidden"><div id="gfp-tabs"></div><input id="gfp-search" /><div id="gfp-list"></div><button id="gfp-cancel"></button><button id="gfp-ok"></button><div id="gfp-sentinel"></div></div>`;
    }

    it('TextToolDialog.destroy clears the preview timer and nested picker', async () => {
        const { TextToolDialog } = await import('../src/ui/TextToolDialog');
        mountTextTool();
        const appState = { fontFamily: 'Unifont', bgColor: [0, 0, 0], fgColor: [255, 255, 255] } as unknown as import('../src/types').AppState;
        const dialog = new TextToolDialog(appState, () => ({ width: 10, height: 10 }) as never, () => {}, () => ({ width: 8, height: 16, font: '8px monospace', advance: 8 }));
        const inner = dialog as unknown as {
            previewTimer: ReturnType<typeof setTimeout> | null;
            googleFontPicker: { destroy(): void };
        };
        inner.previewTimer = setTimeout(() => {}, 5000);
        const pickerDestroy = vi.spyOn(inner.googleFontPicker, 'destroy');
        dialog.destroy();
        expect(inner.previewTimer).toBeNull();
        expect(pickerDestroy).toHaveBeenCalledTimes(1);
    });

    function mountFontPicker(): void {
        document.body.innerHTML = `
            <div id="google-font-picker-modal" class="modal hidden">
                <div id="gfp-tabs"></div><input id="gfp-search" /><div id="gfp-list"></div><button id="gfp-cancel"></button><button id="gfp-ok"></button><div id="gfp-sentinel"></div>
            </div>`;
    }

    it('GoogleFontPicker.destroy disconnects observers and clears debounce', async () => {
        const { GoogleFontPicker } = await import('../src/ui/GoogleFontPicker');
        mountFontPicker();
        const picker = new GoogleFontPicker(() => {});
        const inner = picker as unknown as {
            observer: IntersectionObserver;
            fontObserver: IntersectionObserver;
            searchDebounce: ReturnType<typeof setTimeout> | null;
        };
        const obsDisc = vi.spyOn(inner.observer, 'disconnect');
        const fontDisc = vi.spyOn(inner.fontObserver, 'disconnect');
        inner.searchDebounce = setTimeout(() => {}, 5000);
        picker.destroy();
        expect(obsDisc).toHaveBeenCalled();
        expect(fontDisc).toHaveBeenCalled();
        expect(inner.searchDebounce).toBeNull();
    });

    it('PreviewWindow.destroy removes the modal div from the DOM', async () => {
        const { PreviewWindow } = await import('../src/ui/PreviewWindow');
        const { CanvasState } = await import('../src/state/CanvasState');
        const win = new PreviewWindow(() => new CanvasState(2, 2), () => 'Unifont');
        expect(document.getElementById('preview-window')).not.toBeNull();
        win.destroy();
        expect(document.getElementById('preview-window')).toBeNull();
    });

    it('CharPalette caps custom chars and dedupes by char', async () => {
        const { CharPalette, MAX_CUSTOM_CHARS } = await import('../src/ui/CharPalette');
        expect(MAX_CUSTOM_CHARS).toBe(256);
        document.body.innerHTML = '<div id="uifix-palette"></div>';
        const appState = { selectedChar: '█' } as unknown as import('../src/types').AppState;
        const palette = new CharPalette('uifix-palette', appState, () => {});
        const groups = (palette as unknown as { groups: { name: string; chars: string[] }[] }).groups;
        const custom = () => groups.find((group) => group.name === 'Custom')!.chars;
        const before = custom().length;
        palette.addCustomChars(Array.from({ length: 300 }, (_, i) => `uifix-${i}`));
        expect(custom().length).toBeLessThanOrEqual(MAX_CUSTOM_CHARS);
        expect(custom().length).toBe(Math.min(MAX_CUSTOM_CHARS, before + 300));
        const dupLen = custom().length;
        palette.addCustomChars(['uifix-dup-unique-1', 'uifix-dup-unique-1']);
        expect(custom().filter((c) => c === 'uifix-dup-unique-1').length).toBeLessThanOrEqual(1);
        expect(custom().length).toBeLessThanOrEqual(dupLen + 1);
    });
});

describe('U7 font-string sanitizing', () => {
    it('sanitizeFontFamily allows alphanumerics, space, underscore, dash only', async () => {
        const { sanitizeFontFamily } = await import('../src/utils/cssFont');
        expect(sanitizeFontFamily('Fira Code')).toBe('Fira Code');
        expect(sanitizeFontFamily('M PLUS_1p-400')).toBe('M PLUS_1p-400');
        expect(sanitizeFontFamily('Evil";color:red')).toBe('Evilcolorred');
        expect(sanitizeFontFamily('a(b)c')).toBe('abc');
        expect(sanitizeFontFamily('  spaced   out  ')).toBe('spaced out');
        expect(sanitizeFontFamily('')).toBe('monospace');
        expect(sanitizeFontFamily(null)).toBe('monospace');
        expect(sanitizeFontFamily(undefined)).toBe('monospace');
        expect(sanitizeFontFamily(42)).toBe('monospace');
        expect(sanitizeFontFamily('!!!')).toBe('monospace');
    });

    it('sanitizeFontList preserves fallback lists while stripping injection', async () => {
        const { sanitizeFontList } = await import('../src/utils/cssFont');
        expect(sanitizeFontList("'Fira Code', 'FiraCode'")).toBe('"Fira Code", FiraCode');
        expect(sanitizeFontList('Arial')).toBe('Arial');
        expect(sanitizeFontList('Evil; family, Good')).toBe('"Evil family", Good');
        expect(sanitizeFontList('')).toBe('monospace');
        expect(sanitizeFontList(null)).toBe('monospace');
    });

    it('toCssFontFamily falls back on null and strips hostile constructs', async () => {
        const { toCssFontFamily } = await import('../src/utils/cssFont');
        expect(toCssFontFamily(null)).toBe('monospace');
        expect(toCssFontFamily(undefined)).toBe('monospace');
        expect(toCssFontFamily('')).toBe('monospace');
        expect(toCssFontFamily('Fira Code')).toBe('"Fira Code"');
        expect(toCssFontFamily("'Fira Code', 'FiraCode'")).toBe("'Fira Code', 'FiraCode'");
        const hostile = toCssFontFamily('Arial; color: red');
        expect(hostile).not.toContain(';');
        expect(toCssFontFamily('url(evil)')).not.toContain('url(');
        expect(toCssFontFamily('Weird!Family')).not.toContain('!');
    });

    it('Toolbar sanitizes hostile font values on change', async () => {
        const { Toolbar } = await import('../src/ui/Toolbar');
        const { UndoStack } = await import('../src/state/UndoStack');
        document.body.innerHTML = `
            <button id="tool-brush"></button><button id="tool-erase"></button><button id="tool-type"></button><button id="tool-text"></button><button id="tool-rect"></button><button id="tool-oval"></button><button id="tool-line"></button><button id="tool-gradient"></button><button id="tool-fill"></button><button id="tool-eyedropper"></button><button id="tool-select"></button><button id="tool-move"></button><button id="tool-rotate"></button>
            <select id="type-style-select"></select><select id="rect-mode-select"></select><select id="oval-mode-select"></select><select id="line-mode-select"></select><select id="gradient-target-select"></select><select id="fill-mode-select"></select><select id="eyedropper-target-select"></select><select id="select-mode-select"></select><select id="rotate-mode-select"></select>
            <button id="btn-undo"></button><button id="btn-redo"></button><button id="btn-export"></button>
            <select id="font-select"></select>
            <input type="checkbox" id="line-diagonal-checkbox" />`;
        const appState = {
            activeToolId: 'brush', typeStyle: 'regular', rectMode: 'light', ovalMode: 'light', lineMode: 'light',
            gradientTarget: 'foreground', fillMode: 'brush', eyedropperTarget: 'fg-fg', selectMode: 'rectangle',
            rotateMode: 'cw90', fontFamily: 'Unifont',
        } as unknown as import('../src/types').AppState;
        let seen = '';
        const toolbar = new Toolbar(appState, new UndoStack(), () => {}, () => {}, (family) => { seen = family; });
        const select = document.getElementById('font-select') as HTMLSelectElement;
        const hostile = document.createElement('option');
        hostile.value = 'Evil";color:red';
        hostile.textContent = 'Evil';
        select.appendChild(hostile);
        select.value = 'Evil";color:red';
        select.dispatchEvent(new Event('change'));
        expect(seen).not.toContain(';');
        expect(seen).not.toContain('"');
        expect(appState.fontFamily).toBe(seen);
        toolbar.destroy();
    });
});

describe('U8 finite font metrics', () => {
    it('deriveCellMetrics falls back on NaN width', async () => {
        const { deriveCellMetrics } = await import('../src/utils/fontMetrics');
        expect(deriveCellMetrics(20, { width: NaN }).width).toBe(Math.ceil(20 * 0.6));
        expect(deriveCellMetrics(20, { width: Infinity }).width).toBe(Math.ceil(20 * 0.6));
        expect(deriveCellMetrics(20, { width: 15 }).width).toBe(15);
    });

    it('deriveCellMetrics falls back on NaN vertical metrics', async () => {
        const { deriveCellMetrics } = await import('../src/utils/fontMetrics');
        expect(deriveCellMetrics(22, { width: 10, fontBoundingBoxAscent: NaN, fontBoundingBoxDescent: 4 }).height)
            .toBe(Math.ceil(22 * 1.2));
    });

    it('measureCellMetrics returns fallback metrics when 2d context is null', async () => {
        const { measureCellMetrics } = await import('../src/utils/fontMetrics');
        const createElement = document.createElement.bind(document);
        vi.spyOn(document, 'createElement').mockImplementation(((tag: string) => {
            const el = createElement(tag);
            if (tag === 'canvas') Object.defineProperty(el, 'getContext', { value: () => null });
            return el;
        }) as typeof document.createElement);
        const metrics = measureCellMetrics('Arial', 18);
        expect(metrics.width).toBe(Math.ceil(18 * 0.6));
        expect(metrics.height).toBe(Math.ceil(18 * 1.2));
        expect(metrics.font).toBe('18px Arial');
    });

    it('pickPreviewFontSize skips non-finite measures', async () => {
        const { pickPreviewFontSize } = await import('../src/ui/PreviewWindow');
        const size = pickPreviewFontSize(() => ({ width: NaN, height: NaN, font: '', advance: NaN }), 10, 10, 1000, 1000);
        expect(size).toBe(2);
        const ok = pickPreviewFontSize(
            (fs) => (fs > 10
                ? { width: 100, height: 100, font: '', advance: 8 }
                : { width: 5, height: 8, font: '', advance: 5 }),
            10, 10, 500, 500,
        );
        expect(ok).toBe(10);
    });
});

describe('U9 ansiParser dims and disposal', () => {
    it('parseAnsiToCells clamps hostile dims instead of allocating huge grids', async () => {
        const { parseAnsiToCells } = await import('../src/utils/ansiParser');
        const cells = await parseAnsiToCells('', 0, 0);
        expect(cells.length).toBe(1);
        const capped = await parseAnsiToCells('', 100000, 100000);
        expect(capped.length).toBeLessThanOrEqual(500 * 500);
    });

    it('parseAnsiToState clamps hostile dims for state and terminal', async () => {
        const { parseAnsiToState } = await import('../src/utils/ansiParser');
        const state = await parseAnsiToState('', 100000, 100000);
        expect(state.width).toBeLessThanOrEqual(500);
        expect(state.height).toBeLessThanOrEqual(500);
        expect(state.width).toBeGreaterThanOrEqual(1);
    });
});
