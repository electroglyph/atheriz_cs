import { CanvasState } from '../state/CanvasState';
import { UndoStack } from '../state/UndoStack';
import { AppState } from '../types';
import { renderTextToAnsiLayer, buildTextBatch, applyTextRender } from '../utils/TextToANSI';
import { ChafaConfig, DEFAULT_CHAFA_OPTIONS } from '../utils/chafaDefaults';
import { CellMetrics } from '../utils/fontMetrics';
import { GoogleFontPicker } from './GoogleFontPicker';
import { loadFontFull, fontNameToCSS } from '../utils/googleFontLoader';
import { sanitizeFontList } from '../utils/cssFont';
import { FEATURED_GOOGLE_FONTS } from '../data/featuredGoogleFonts';
import { closeOtherModals } from './modalHelper';

/** Upper bound for waiting on document.fonts before previewing anyway. */
export const TEXT_PREVIEW_FONTS_TIMEOUT_MS = 5000;

/** Font styles offered by the #text-tool-style select; anything else is rejected. */
const ALLOWED_FONT_STYLES = new Set(['normal', 'bold', 'italic', 'bold italic']);

/** Text alignments offered by the #text-tool-align select. */
const ALLOWED_TEXT_ALIGNS: CanvasTextAlign[] = ['left', 'center', 'right'];

export class TextToolDialog {
    private modal: HTMLElement;
    private input: HTMLTextAreaElement;
    private fontSelect: HTMLSelectElement;
    private styleSelect: HTMLSelectElement;
    private maxWidthInput: HTMLInputElement;
    private maxWidthVal: HTMLElement;
    private stretchInput: HTMLInputElement;
    private stretchVal: HTMLElement;
    private previewCanvas: HTMLCanvasElement;
    
    private btnCancel: HTMLButtonElement;
    private btnConfirm: HTMLButtonElement;
    private btnGoogleFonts: HTMLButtonElement;
    private alignSelect: HTMLSelectElement;
    private chafaOptionsContainer!: HTMLElement;

    private userConfig!: ChafaConfig;

    private systemFontsLoaded = false;
    private systemFontsLoading = false;
    private localFontsAdded = false;
    private googleFontPicker: GoogleFontPicker;
    private onConfirm: (state: CanvasState) => void;
    private appState: AppState;
    // Never store the canvas: New/undo/load swap the state object, and any
    // stored reference silently goes stale (render-text then resurrected the
    // discarded map). The getter reads the owner's live binding every time.
    private getCanvasState: () => CanvasState;
    private getCellMetrics: () => CellMetrics;
    private undoStack: UndoStack | null = null;

    constructor(appState: AppState, getCanvasState: () => CanvasState, onConfirm: (state: CanvasState) => void, getCellMetrics: () => CellMetrics, undoStack?: UndoStack) {
        this.appState = appState;
        this.getCanvasState = getCanvasState;
        this.onConfirm = onConfirm;
        this.getCellMetrics = getCellMetrics;
        this.undoStack = undoStack ?? null;

        this.modal = document.getElementById('text-tool-modal') as HTMLElement;
        this.input = document.getElementById('text-tool-input') as HTMLTextAreaElement;
        this.fontSelect = document.getElementById('text-tool-font') as HTMLSelectElement;
        this.styleSelect = document.getElementById('text-tool-style') as HTMLSelectElement;
        this.maxWidthInput = document.getElementById('text-tool-max-width') as HTMLInputElement;
        this.maxWidthVal = document.getElementById('text-tool-max-width-val') as HTMLElement;
        this.stretchInput = document.getElementById('text-tool-stretch') as HTMLInputElement;
        this.stretchVal = document.getElementById('text-tool-stretch-val') as HTMLElement;
        this.previewCanvas = document.getElementById('text-tool-preview') as HTMLCanvasElement;
        
        this.btnCancel = document.getElementById('btn-text-cancel') as HTMLButtonElement;
        this.btnConfirm = document.getElementById('btn-text-confirm') as HTMLButtonElement;
        this.btnGoogleFonts = document.getElementById('text-tool-google-fonts-btn') as HTMLButtonElement;
        this.alignSelect = document.getElementById('text-tool-align') as HTMLSelectElement;
        this.chafaOptionsContainer = document.getElementById('text-chafa-options-container')!;

        this.googleFontPicker = new GoogleFontPicker((family) => this.selectGoogleFont(family));

        this.userConfig = { ...DEFAULT_CHAFA_OPTIONS, symbols: 'block' };
        this.buildOptionsUI();
        this.bindEvents();
    }

    private bindEvents() {
        this.btnCancel.addEventListener('click', () => this.close());
        
        this.btnConfirm.addEventListener('click', async () => {
            const text = this.input.value;
            if (!text) {
                this.close();
                return;
            }
            let maxWidth = parseInt(this.maxWidthInput.value, 10);
            if (!Number.isFinite(maxWidth) || maxWidth < 1) maxWidth = 80;
            
            this.btnConfirm.disabled = true;
            this.btnConfirm.innerText = 'Converting...';

            
            try {
                const target = this.getCanvasState();
                const result = await renderTextToAnsiLayer(
                    text,
                    maxWidth,
                    { width: target.width, height: target.height },
                    this.userConfig,
                    this.previewCanvas,
                    this.getCellMetrics(),
                );
                // The canvas may have been replaced (New/resize/load/undo)
                // while the async conversion was in flight. Re-read the live
                // state: applying to `target` would resurrect the discarded
                // map — old text and all — underneath the user.
                const live = this.getCanvasState();
                if (result) {
                    applyTextRender(
                        live,
                        this.undoStack,
                        result.label,
                        buildTextBatch(result.cells, result.cols, result.rows, live.width, live.height),
                    );
                }
                this.onConfirm(live);
                this.close();
            } catch (e) {
                console.error("Text conversion failed:", e);
                alert("Conversion failed. Check console.");
            } finally {
                this.btnConfirm.disabled = false;
                this.btnConfirm.innerText = 'Convert to ANSI Layer';
            }
        });

        this.input.addEventListener('input', () => this.schedulePreview());
        this.fontSelect.addEventListener('change', () => {
            void this.ensureSelectedFontLoaded().then(() => this.schedulePreview());
        });
        this.styleSelect.addEventListener('change', () => this.schedulePreview());
        this.stretchInput.addEventListener('input', () => {
            this.stretchVal.innerText = `${this.stretchInput.value}%`;
            this.schedulePreview();
        });
        this.maxWidthInput.addEventListener('input', () => {
            this.maxWidthVal.innerText = this.maxWidthInput.value;
            this.schedulePreview();
        });

        this.fontSelect.addEventListener('focus', () => this.loadSystemFonts());
        this.btnGoogleFonts.addEventListener('click', () => this.googleFontPicker.open());
        this.alignSelect.addEventListener('change', () => this.schedulePreview());
    }

    private previewTimer: ReturnType<typeof setTimeout> | null = null;

    private schedulePreview() {
        if (this.previewTimer !== null) clearTimeout(this.previewTimer);
        this.previewTimer = setTimeout(() => {
            this.previewTimer = null;
            this.updatePreview();
        }, 80);
    }

    private async updatePreview() {
        try {
            const text = this.input.value;
            if (!text) {
                const ctx = this.previewCanvas.getContext('2d');
                if (ctx) {
                    this.previewCanvas.width = 100;
                    this.previewCanvas.height = 100;
                    ctx.clearRect(0, 0, 100, 100);
                }
                return;
            }

            const rawFamilies = this.fontSelect.value || 'Arial';
            // Sanitize the family list before embedding it in a canvas font
            // string: a hostile option value must not break out of the style.
            const fontFamilies = sanitizeFontList(rawFamilies, 'Arial');
            const rawStyle = this.styleSelect.value || 'normal';
            const fontStyle = ALLOWED_FONT_STYLES.has(rawStyle) ? rawStyle : 'normal';
            const rawAlign = this.alignSelect.value || 'left';
            const align: CanvasTextAlign = (ALLOWED_TEXT_ALIGNS as string[]).includes(rawAlign)
                ? rawAlign as CanvasTextAlign
                : 'left';
            let stretch = parseInt(this.stretchInput.value, 10) / 100;
            if (!Number.isFinite(stretch) || stretch <= 0) stretch = 1;

            const fontSize = 96;
            const fontStr = fontStyle === 'normal'
                ? `${fontSize}px ${fontFamilies}`
                : `${fontStyle} ${fontSize}px ${fontFamilies}`;

            try {
                await document.fonts.load(fontStr, text);
            } catch (err) {
                console.warn('TextToolDialog: preview font load failed, using fallback rendering', err);
            }
            try {
                await Promise.race([
                    document.fonts.ready,
                    new Promise((_, reject) => setTimeout(
                        () => reject(new Error('document.fonts.ready timeout')),
                        TEXT_PREVIEW_FONTS_TIMEOUT_MS,
                    )),
                ]);
            } catch (err) {
                console.warn('TextToolDialog: fonts.ready timed out, previewing with available fonts', err);
            }

            // Split into lines so multi-line input renders as stacked rows
            const lines = text.split('\n');
            const lineHeight = Math.round(fontSize * 1.2);
            const canvasW = 1200;
            const canvasH = Math.max(200, lineHeight * lines.length + 40);

            this.previewCanvas.width = canvasW;
            this.previewCanvas.height = canvasH;
            const ctx = this.previewCanvas.getContext('2d');
            if (!ctx) {
                console.warn('TextToolDialog: 2d preview context unavailable');
                return;
            }

            const bgColor = `rgb(${this.appState.bgColor[0]},${this.appState.bgColor[1]},${this.appState.bgColor[2]})`;
            const fgColor = `rgb(${this.appState.fgColor[0]},${this.appState.fgColor[1]},${this.appState.fgColor[2]})`;
            ctx.fillStyle = bgColor;
            ctx.fillRect(0, 0, canvasW, canvasH);

            ctx.font = fontStr;
            ctx.fillStyle = fgColor;
            ctx.textBaseline = 'top';

            // Determine x anchor position based on alignment
            let anchorX: number;
            if (align === 'center') {
                anchorX = canvasW / 2;
            } else if (align === 'right') {
                anchorX = canvasW - 20;
            } else {
                anchorX = 20;
            }

            ctx.save();
            // Apply horizontal stretch around the anchor point
            ctx.translate(anchorX, 0);
            ctx.scale(stretch, 1);
            ctx.translate(-anchorX / stretch, 0);

            ctx.textAlign = align;

            for (let i = 0; i < lines.length; i++) {
                ctx.fillText(lines[i], anchorX / stretch, 20 + i * lineHeight);
            }
            ctx.restore();
        } catch (err) {
            console.error('Error drawing preview:', err);
        }
    }

    private initFonts() {
        if (this.localFontsAdded) return;
        this.localFontsAdded = true;

        const localFonts = [
            { name: 'Unifont', val: 'Unifont' },
            { name: 'KreativeSquare', val: 'KreativeSquare' },
            { name: 'Fira Code', val: "'Fira Code', 'FiraCode'" },
            { name: 'Arial', val: 'Arial' },
            { name: 'Times New Roman', val: '"Times New Roman"' },
            { name: 'Impact', val: 'Impact' },
            { name: 'Courier New', val: '"Courier New"' }
        ];

        for (const f of localFonts) {
            const opt = document.createElement('option');
            opt.value = f.val;
            opt.textContent = f.name;
            this.fontSelect.appendChild(opt);
        }

        // Featured Google Fonts: bundled under public/gfonts, so they work
        // offline. Anything else comes via the G Fonts button (streams from
        // Google). Values use fontNameToCSS so picker selections dedupe here.
        const featuredSep = document.createElement('option');
        featuredSep.disabled = true;
        featuredSep.textContent = '── Featured Google Fonts ──';
        this.fontSelect.appendChild(featuredSep);

        for (const f of FEATURED_GOOGLE_FONTS) {
            const opt = document.createElement('option');
            opt.value = fontNameToCSS(f.family);
            opt.textContent = f.family;
            this.fontSelect.appendChild(opt);
        }
        
        // Try to sync with app font
        const matchesApp = Array.from(this.fontSelect.options).find(o => o.value === this.appState.fontFamily);
        if (matchesApp) {
            this.fontSelect.value = this.appState.fontFamily;
        } else {
            this.fontSelect.value = 'Arial';
        }
    }

    private async loadSystemFonts() {
        if (this.systemFontsLoaded || this.systemFontsLoading) return;
        this.systemFontsLoading = true;

        try {
            if ('queryLocalFonts' in window) {
                // @ts-ignore
                const fonts = await window.queryLocalFonts();
                const familySet = new Set<string>();
                for (const f of fonts) {
                    familySet.add(f.family);
                }

                const sep = document.createElement('option');
                sep.disabled = true;
                sep.textContent = '── System Fonts ──';
                this.fontSelect.appendChild(sep);

                const sorted = Array.from(familySet).sort();
                for (const family of sorted) {
                    const opt = document.createElement('option');
                    opt.value = `"${family}"`;
                    opt.textContent = family;
                    this.fontSelect.appendChild(opt);
                }
            }
            this.systemFontsLoaded = true;
        } catch (e) {
            console.error('Failed to load system fonts for Text tool:', e);
        } finally {
            this.systemFontsLoading = false;
        }
    }

        /**
     * Featured dropdown entries have no stylesheet until first use; inject it
     * (local cache when bundled, Google CDN otherwise) before previewing.
     * Picker-added fonts are already loaded; system fonts need nothing.
     */
    private async ensureSelectedFontLoaded(): Promise<void> {
        const selected = this.fontSelect.value;
        const featured = FEATURED_GOOGLE_FONTS.find(f => fontNameToCSS(f.family) === selected);
        if (featured) {
            await loadFontFull(featured.family);
        }
    }

    private async selectGoogleFont(family: string) {
        await loadFontFull(family);
        const cssVal = fontNameToCSS(family);

        const existing = Array.from(this.fontSelect.options).find(o => o.value === cssVal);
        if (existing) {
            this.fontSelect.value = cssVal;
        } else {
            const sep = document.createElement('option');
            sep.disabled = true;
            sep.textContent = '── Google Fonts ──';
            const hasGFSection = Array.from(this.fontSelect.options).some(o => o.textContent === '── Google Fonts ──');
            if (!hasGFSection) {
                this.fontSelect.appendChild(sep);
            }
            const opt = document.createElement('option');
            opt.value = cssVal;
            opt.textContent = family;
            this.fontSelect.appendChild(opt);
            this.fontSelect.value = cssVal;
        }
        this.schedulePreview();
    }

    public async open() {
        closeOtherModals('text-tool-modal');
        this.initFonts();
        const matchingOption = Array.from(this.fontSelect.options).find((option) => option.value === this.appState.fontFamily);
        if (matchingOption) this.fontSelect.value = this.appState.fontFamily;
        this.input.value = '';
        const liveWidth = this.getCanvasState().width;
        this.maxWidthInput.max = liveWidth.toString();
        this.maxWidthInput.value = liveWidth.toString();
        this.maxWidthVal.innerText = liveWidth.toString();
        this.stretchInput.value = '100';
        this.stretchVal.innerText = '100%';
        await this.ensureSelectedFontLoaded();
        await this.updatePreview();
        this.modal.classList.remove('hidden');
        this.input.focus();
    }

    public close() {
        if (this.previewTimer !== null) {
            clearTimeout(this.previewTimer);
            this.previewTimer = null;
        }
        this.modal.classList.add('hidden');
    }

    public destroy(): void {
        if (this.previewTimer !== null) {
            clearTimeout(this.previewTimer);
            this.previewTimer = null;
        }
        this.googleFontPicker.destroy();
    }

    private buildOptionsUI() {
        this.chafaOptionsContainer.innerHTML = '';
        const keys = Object.keys(DEFAULT_CHAFA_OPTIONS) as (keyof ChafaConfig)[];

        const enumMap: Record<string, string[]> = {
            format: ['CHAFA_PIXEL_MODE_SYMBOLS', 'CHAFA_PIXEL_MODE_SIXELS', 'CHAFA_PIXEL_MODE_KITTY', 'CHAFA_PIXEL_MODE_ITERM2'],
            colors: [
                'CHAFA_CANVAS_MODE_TRUECOLOR', 
                'CHAFA_CANVAS_MODE_INDEXED_256', 
                'CHAFA_CANVAS_MODE_INDEXED_240', 
                'CHAFA_CANVAS_MODE_INDEXED_16', 
                'CHAFA_CANVAS_MODE_INDEXED_16_8', 
                'CHAFA_CANVAS_MODE_INDEXED_8', 
                'CHAFA_CANVAS_MODE_FGBG_BGFG', 
                'CHAFA_CANVAS_MODE_FGBG'
            ],
            colorExtractor: ['CHAFA_COLOR_EXTRACTOR_AVERAGE', 'CHAFA_COLOR_EXTRACTOR_MEDIAN'],
            colorSpace: ['CHAFA_COLOR_SPACE_RGB', 'CHAFA_COLOR_SPACE_DIN99D'],
            dither: ['CHAFA_DITHER_MODE_NONE', 'CHAFA_DITHER_MODE_ORDERED', 'CHAFA_DITHER_MODE_DIFFUSION', 'CHAFA_DITHER_MODE_NOISE']
        };
        
        for (const key of keys) {
            // Lock format to Symbols by hiding the option entirely
            if (key === 'format') continue;

            const val = DEFAULT_CHAFA_OPTIONS[key];
            const div = document.createElement('div');
            const label = document.createElement('label');
            label.style.display = 'block';
            label.style.fontSize = '0.8em';
            label.style.color = '#ccc';
            label.style.marginBottom = '2px';
            label.innerText = key;
            
            let control: HTMLElement;
            
            if (enumMap[key]) {
                const select = document.createElement('select');
                select.style.width = '100%';
                select.style.padding = '4px';
                select.style.backgroundColor = '#333';
                select.style.color = '#fff';
                select.style.border = '1px solid #555';
                select.style.borderRadius = '4px';
                select.style.fontSize = '12px';

                for (const optVal of enumMap[key]) {
                    const opt = document.createElement('option');
                    opt.value = optVal;
                    // Prettify name: CHAFA_PIXEL_MODE_SYMBOLS -> Symbols
                    const parts = optVal.split('_');
                    opt.textContent = parts[parts.length - 1].toLowerCase().replace(/^\w/, (c) => c.toUpperCase());
                    if (optVal === val) opt.selected = true;
                    select.appendChild(opt);
                }
                select.addEventListener('change', () => {
                    (this.userConfig as unknown as Record<string, string>)[key] = select.value;
                });
                control = select;
            } else if (typeof val === 'boolean') {
                const input = document.createElement('input');
                input.type = 'checkbox';
                input.checked = val;
                input.style.verticalAlign = 'middle';
                input.style.marginRight = '5px';
                input.addEventListener('change', () => {
                    (this.userConfig[key] as boolean) = input.checked;
                });
                label.prepend(input);
                control = document.createElement('span'); 
            } else if (typeof val === 'number') {
                const input = document.createElement('input');
                input.type = 'number';
                input.step = key.includes('threshold') || key.includes('Intensity') || key.includes('Ratio') ? "0.1" : "1";
                input.value = val.toString();
                input.style.width = '100%';
                input.style.padding = '4px';
                input.style.backgroundColor = '#333';
                input.style.color = '#fff';
                input.style.border = '1px solid #555';
                input.style.borderRadius = '4px';
                input.style.fontSize = '12px';
                input.addEventListener('change', () => {
                    const parsed = parseFloat(input.value);
                    if (Number.isFinite(parsed)) {
                        (this.userConfig[key] as number) = parsed;
                    } else {
                        input.value = String(this.userConfig[key]);
                    }
                });
                control = input;
            } else {
                const input = document.createElement('input');
                input.type = 'text';
                input.value = val as string;
                input.style.width = '100%';
                input.style.padding = '4px';
                input.style.backgroundColor = '#333';
                input.style.color = '#fff';
                input.style.border = '1px solid #555';
                input.style.borderRadius = '4px';
                input.style.fontSize = '12px';
                input.addEventListener('change', () => {
                    (this.userConfig[key] as string) = input.value;
                });
                control = input;
            }
            
            div.appendChild(label);
            if (control.tagName !== 'SPAN') {
                div.appendChild(control);
            }

            if (key === 'symbols') {
                const helpText = document.createElement('div');
                helpText.style.fontSize = '10px';
                helpText.style.color = '#8bb';
                helpText.style.marginTop = '4px';
                helpText.style.lineHeight = '1.3';
                helpText.innerText = 'Classes: all, none, space, solid, stipple, block, border, diagonal, dot, quad, half, hhalf, vhalf, inverted, braille, technical, geometric, ascii, legacy, sextant, wedge, wide, narrow.\nUse + to combine, - to subtract.';
                div.appendChild(helpText);
            }

            this.chafaOptionsContainer.appendChild(div);
        }
    }
}
