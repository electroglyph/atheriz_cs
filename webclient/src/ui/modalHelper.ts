/**
 * Ids of all modal overlays currently visible (present in the DOM and not
 * `.hidden`). Used by sub-dialogs (e.g. the Google font picker) to stack on
 * top of their parent instead of hiding it.
 */
export function visibleModalIds(): string[] {
    return Array.from(document.querySelectorAll('.modal:not(.hidden)'))
        .map((el) => (el as HTMLElement).id)
        .filter((id) => id.length > 0);
}

export function closeOtherModals(currentId: string, keepIds: string[] = []): void {
    const modalIds = [
        'new-canvas-modal',
        'resize-canvas-modal',
        'image-import-modal',
        'char-map-modal',
        'text-tool-modal',
        'google-font-picker-modal',
        'color-picker-modal',
        'type-tool-modal',
        'move-denied-modal',
        'color-adjust-modal',
    ];
    for (const id of modalIds) {
        if (id === currentId || keepIds.includes(id)) continue;
        document.getElementById(id)?.classList.add('hidden');
    }
    const preview = document.getElementById('preview-window');
    if (preview && currentId !== 'preview-window' && preview.style.display !== 'none' && preview.style.display !== '') {
        preview.style.display = 'none';
    }
}
