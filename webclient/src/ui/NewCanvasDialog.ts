import { closeOtherModals } from './modalHelper';

/** Bounds for canvas dimensions: 1..2048 integer cells. */
export const CANVAS_DIM_MIN = 1;
export const CANVAS_DIM_MAX = 2048;

/**
 * Parses a canvas dimension from a text input. Returns null unless the
 * value is a finite integer in [1, 2048], so callers ignore the commit
 * instead of creating degenerate canvases.
 */
export function parseCanvasDim(value: string): number | null {
    if (!/^-?\d+$/.test(value.trim())) return null;
    const n = Number(value.trim());
    if (!Number.isSafeInteger(n)) return null;
    if (n < CANVAS_DIM_MIN || n > CANVAS_DIM_MAX) return null;
    return n;
}

export class NewCanvasDialog {
    private modal: HTMLElement;
    private btnNew: HTMLButtonElement;
    private btnCancel: HTMLButtonElement;
    private btnConfirm: HTMLButtonElement;
    private inputs: NodeListOf<HTMLButtonElement>;
    private inputW: HTMLInputElement;
    private inputH: HTMLInputElement;
    
    private onConfirmCallback: (w: number, h: number) => void;

    constructor(onConfirm: (w: number, h: number) => void) {
        this.onConfirmCallback = onConfirm;
        
        this.modal = document.getElementById('new-canvas-modal')!;
        this.btnNew = document.getElementById('btn-new') as HTMLButtonElement;
        this.btnCancel = document.getElementById('btn-new-cancel') as HTMLButtonElement;
        this.btnConfirm = document.getElementById('btn-new-confirm') as HTMLButtonElement;
        
        this.inputs = this.modal.querySelectorAll('.preset-buttons button');
        this.inputW = document.getElementById('new-width') as HTMLInputElement;
        this.inputH = document.getElementById('new-height') as HTMLInputElement;

        this.bindEvents();
    }

    private bindEvents() {
        this.btnNew.addEventListener('click', () => {
            closeOtherModals('new-canvas-modal');
            this.modal.classList.remove('hidden');
        });

        this.btnCancel.addEventListener('click', () => {
            this.modal.classList.add('hidden');
        });

        this.btnConfirm.addEventListener('click', () => {
            const w = parseCanvasDim(this.inputW.value);
            const h = parseCanvasDim(this.inputH.value);
            if (w !== null && h !== null) {
                this.onConfirmCallback(w, h);
                this.modal.classList.add('hidden');
            }
        });

        for (const preset of Array.from(this.inputs)) {
            preset.addEventListener('click', () => {
                this.inputW.value = preset.dataset['w'] || "24";
                this.inputH.value = preset.dataset['h'] || "24";
            });
        }
    }
}
