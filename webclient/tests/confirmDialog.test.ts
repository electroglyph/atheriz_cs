// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { ConfirmDialog } from '../src/ui/ConfirmDialog';

function mountDialog(): void {
    document.body.innerHTML = `
        <div id="confirm-modal" class="modal hidden">
            <p id="confirm-modal-message"></p>
            <button id="confirm-modal-cancel">Cancel</button>
            <button id="confirm-modal-ok">OK</button>
        </div>`;
}

describe('ConfirmDialog', () => {
    it('throws when the container is missing', () => {
        document.body.innerHTML = '';
        expect(() => new ConfirmDialog('confirm-modal')).toThrow();
    });

    it('resolves true on OK', async () => {
        mountDialog();
        const dialog = new ConfirmDialog('confirm-modal');
        const pending = dialog.confirm('Delete it?');
        expect(document.querySelector('#confirm-modal-message')!.textContent).toBe('Delete it?');
        expect(dialog.isVisible()).toBe(true);
        (document.querySelector('#confirm-modal-ok') as HTMLButtonElement).click();
        await expect(pending).resolves.toBe(true);
        expect(dialog.isVisible()).toBe(false);
    });

    it('resolves false on Cancel, Escape, and backdrop click', async () => {
        mountDialog();
        const dialog = new ConfirmDialog('confirm-modal');
        const onCancel = dialog.confirm('Delete it?');
        (document.querySelector('#confirm-modal-cancel') as HTMLButtonElement).click();
        await expect(onCancel).resolves.toBe(false);

        const onEscape = dialog.confirm('Delete it?');
        window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
        await expect(onEscape).resolves.toBe(false);

        const onBackdrop = dialog.confirm('Delete it?');
        (document.querySelector('#confirm-modal') as HTMLElement).click();
        await expect(onBackdrop).resolves.toBe(false);
        expect(dialog.isVisible()).toBe(false);
    });
});
