import { closeOtherModals } from './modalHelper';

/** OK/Cancel confirmation dialog mirroring MessageDialog's chrome
 * (modal stacking, Escape/backdrop dismissal). Escape, backdrop click,
 * and Cancel resolve false; OK resolves true. */
export class ConfirmDialog {
    private container: HTMLElement;
    private messageEl: HTMLElement;
    private okButton: HTMLButtonElement;
    private cancelButton: HTMLButtonElement;
    private pending: ((value: boolean) => void) | null = null;
    private boundKeyDown = (e: KeyboardEvent) => {
        if (e.key === 'Escape' && this.isVisible()) this.settle(false);
    };
    private boundBackdropClick = (e: MouseEvent) => {
        if (e.target === this.container) this.settle(false);
    };

    constructor(containerId: string) {
        const container = document.getElementById(containerId);
        if (!container) throw new Error(`Missing dialog container #${containerId}`);
        this.container = container;
        const messageEl = document.getElementById(`${containerId}-message`);
        if (!messageEl) throw new Error(`Missing dialog message #${containerId}-message`);
        this.messageEl = messageEl;
        const okButton = document.getElementById(`${containerId}-ok`) as HTMLButtonElement | null;
        if (!okButton) throw new Error(`Missing dialog button #${containerId}-ok`);
        this.okButton = okButton;
        const cancelButton = document.getElementById(`${containerId}-cancel`) as HTMLButtonElement | null;
        if (!cancelButton) throw new Error(`Missing dialog button #${containerId}-cancel`);
        this.cancelButton = cancelButton;
        this.okButton.addEventListener('click', () => this.settle(true));
        this.cancelButton.addEventListener('click', () => this.settle(false));
        this.container.addEventListener('click', this.boundBackdropClick);
        window.addEventListener('keydown', this.boundKeyDown);
    }

    public destroy(): void {
        this.container.removeEventListener('click', this.boundBackdropClick);
        window.removeEventListener('keydown', this.boundKeyDown);
    }

    public confirm(message: string): Promise<boolean> {
        closeOtherModals(this.container.id);
        this.messageEl.textContent = message;
        this.container.classList.remove('hidden');
        return new Promise<boolean>((resolve) => {
            this.pending = resolve;
        });
    }

    public hide(): void {
        this.settle(false);
    }

    public isVisible(): boolean {
        return !this.container.classList.contains('hidden');
    }

    private settle(value: boolean): void {
        if (!this.isVisible() && this.pending === null) return;
        this.container.classList.add('hidden');
        const resolve = this.pending;
        this.pending = null;
        resolve?.(value);
    }
}
