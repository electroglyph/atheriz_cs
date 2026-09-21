import { Color, AppState } from '../types';
import { rgbToHex, hexToRgb, colorEquals, cssColor } from '../utils/colors';
import { ColorPickerModal } from './ColorPickerModal';

/** Min/max for an 8-bit color channel. */
export const COLOR_CHANNEL_MIN = 0;
export const COLOR_CHANNEL_MAX = 255;

/**
 * Parses a single 0-255 color channel from a text input. Returns null when
 * the value is not a finite integer in range, so callers can ignore the
 * commit instead of clamping garbage into state.
 */
export function parseColorChannel(value: string): number | null {
    if (!/^-?\d+$/.test(value.trim())) return null;
    const n = Number(value.trim());
    if (!Number.isSafeInteger(n)) return null;
    if (n < COLOR_CHANNEL_MIN || n > COLOR_CHANNEL_MAX) return null;
    return n;
}

/** True when every channel of the tuple is a finite 0-255 integer. */
export function isValidColor(c: Color): boolean {
    return c.length === 3 && c.every((v) => typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= 255);
}

export class ColorPicker {
    private appState: AppState;
    private isForeground: boolean;
    private container: HTMLElement;
    private onChangeAction: () => void;
    
    private selectedSwatch!: HTMLElement;
    private historyContainer!: HTMLElement;
    private hexInput!: HTMLInputElement;
    private rInput!: HTMLInputElement;
    private gInput!: HTMLInputElement;
    private bInput!: HTMLInputElement;

    private history: Color[] = [];

    private boundColorPicked = (e: Event) => {
        const detail = (e as CustomEvent).detail as { isFg: boolean; color: Color };
        if (detail.isFg === this.isForeground) {
            this.setColor(detail.color);
            this.commitToHistory(detail.color);
        }
    };

    constructor(containerId: string, isFg: boolean, appState: AppState, onChange: () => void) {
        this.container = document.getElementById(containerId)!;
        this.isForeground = isFg;
        this.appState = appState;
        this.onChangeAction = onChange;

        for (let i=0; i<8; i++) this.history.push([0,0,0]);
        this.history[0] = this.getColor();

        this.render();
        this.updateUI();

        window.addEventListener('colorPicked', this.boundColorPicked);
    }

    public destroy() {
        window.removeEventListener('colorPicked', this.boundColorPicked);
    }

    private getColor(): Color {
        return this.isForeground ? this.appState.fgColor : this.appState.bgColor;
    }

    private setColor(c: Color) {
        if (colorEquals(c, this.getColor())) return;

        if (this.isForeground) {
            this.appState.fgColor = c;
        } else {
            this.appState.bgColor = c;
        }

        this.updateUI();
        this.onChangeAction();
    }

    private commitToHistory(c: Color) {
        const exists = this.history.some(hc => colorEquals(c, hc));
        if (!exists) {
            this.history.unshift([...c]);
            this.history.pop();
            this.updateUI();
        }
    }

    private render() {
        this.container.innerHTML = `
            <div class="current-color-swatch"></div>
            <div class="color-history"></div>
            <div class="color-inputs">
                <label>R<input type="number" class="ci-r" min="0" max="255"></label>
                <label>G<input type="number" class="ci-g" min="0" max="255"></label>
                <label>B<input type="number" class="ci-b" min="0" max="255"></label>
                <input type="text" class="ci-hex" placeholder="#FFFFFF">
            </div>
        `;

        this.selectedSwatch = this.container.querySelector('.current-color-swatch')!;
        this.historyContainer = this.container.querySelector('.color-history')!;
        this.rInput = this.container.querySelector('.ci-r')!;
        this.gInput = this.container.querySelector('.ci-g')!;
        this.bInput = this.container.querySelector('.ci-b')!;
        this.hexInput = this.container.querySelector('.ci-hex')!;

        this.selectedSwatch.style.cursor = 'pointer';
        this.selectedSwatch.addEventListener('click', () => {
            const current = this.getColor();
            ColorPickerModal.getInstance().open(current).then((result) => {
                if (result) {
                    this.setColor(result);
                    this.commitToHistory(result);
                }
            });
        });

        const updateFromRGB = () => {
            const r = parseColorChannel(this.rInput.value);
            const g = parseColorChannel(this.gInput.value);
            const b = parseColorChannel(this.bInput.value);
            if (r === null || g === null || b === null) {
                // Out-of-range or non-numeric input: ignore the commit and
                // restore the displayed values instead of clamping garbage.
                this.updateUI();
                return;
            }
            const c: Color = [r, g, b];
            this.setColor(c);
            this.commitToHistory(c);
        };

        this.rInput.addEventListener('change', updateFromRGB);
        this.gInput.addEventListener('change', updateFromRGB);
        this.bInput.addEventListener('change', updateFromRGB);

        this.hexInput.addEventListener('change', () => {
            const c = hexToRgb(this.hexInput.value);
            if (!c || !isValidColor(c)) {
                // Invalid hex: keep the old color, restore the input display.
                this.updateUI();
                return;
            }
            this.setColor(c);
            this.commitToHistory(c);
        });
    }

    private updateUI() {
        const c = this.getColor();
        this.selectedSwatch.style.backgroundColor = cssColor(c);
        
        this.rInput.value = c[0].toString();
        this.gInput.value = c[1].toString();
        this.bInput.value = c[2].toString();
        this.hexInput.value = rgbToHex(c);

        this.historyContainer.innerHTML = '';
        for (const hc of this.history) {
            const hDom = document.createElement('div');
            hDom.className = 'history-swatch';
            hDom.style.backgroundColor = cssColor(hc);
            hDom.addEventListener('click', () => this.setColor(hc));
            this.historyContainer.appendChild(hDom);
        }
    }
}
