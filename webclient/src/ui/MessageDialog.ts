import { closeOtherModals } from './modalHelper';

export class MessageDialog {
    private container: HTMLElement;
    private messageEl: HTMLElement;
    private okButton: HTMLButtonElement;
    // Denial notices must be acknowledged, not waved away: a sticky
    // dialog ignores Escape and backdrop clicks, so a rejected edit or
    // delete cannot vanish unseen (its rooms roll back underneath it).
    private readonly dismissable: boolean;
    private boundKeyDown = (e: KeyboardEvent) => {
        if (e.key === 'Escape' && this.dismissable && this.isVisible()) this.hide();
    };
    private boundBackdropClick = (e: MouseEvent) => {
        if (e.target === this.container && this.dismissable) this.hide();
    };

    constructor(containerId: string, options?: { dismissable?: boolean }) {
        this.dismissable = options?.dismissable ?? true;
        const container = document.getElementById(containerId);
        if (!container) throw new Error(`Missing dialog container #${containerId}`);
        this.container = container;
        const messageEl = document.getElementById(`${containerId}-message`);
        if (!messageEl) throw new Error(`Missing dialog message #${containerId}-message`);
        this.messageEl = messageEl;
        const okButton = document.getElementById(`${containerId}-ok`) as HTMLButtonElement | null;
        if (!okButton) throw new Error(`Missing dialog button #${containerId}-ok`);
        this.okButton = okButton;
        this.okButton.addEventListener('click', () => this.hide());
        this.container.addEventListener('click', this.boundBackdropClick);
        window.addEventListener('keydown', this.boundKeyDown);
    }

    public destroy(): void {
        this.container.removeEventListener('click', this.boundBackdropClick);
        window.removeEventListener('keydown', this.boundKeyDown);
    }

    public show(message: string): void {
        closeOtherModals(this.container.id);
        this.messageEl.textContent = message;
        this.container.classList.remove('hidden');
    }

    public hide(): void {
        this.container.classList.add('hidden');
    }

    public isVisible(): boolean {
        return !this.container.classList.contains('hidden');
    }
}
