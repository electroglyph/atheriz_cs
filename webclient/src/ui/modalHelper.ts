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

/**
 * Known modal ids kept as a fallback for overlays that are not tagged with
 * the `.modal` class (or are queried before styles apply). The class-based
 * query below is authoritative; this list only covers stragglers.
 */
const FALLBACK_MODAL_IDS = [
    'new-canvas-modal',
    'resize-canvas-modal',
    'image-import-modal',
    'char-map-modal',
    'text-tool-modal',
    'google-font-picker-modal',
    'color-picker-modal',
    'type-tool-modal',
    'move-denied-modal',
    'map-error-modal',
    'legend-editor-modal',
    'color-adjust-modal',
];

export function closeOtherModals(currentId: string, keepIds: string[] = []): void {
    // Primary path: hide every `.modal` overlay except the target (and any
    // explicitly kept). Class-based so newly added modals stack correctly
    // without updating a hardcoded list.
    for (const el of Array.from(document.querySelectorAll('.modal'))) {
        const id = (el as HTMLElement).id;
        if (!id || id === currentId || keepIds.includes(id)) continue;
        el.classList.add('hidden');
    }
    // Fallback: cover known ids that might lack the `.modal` class.
    for (const id of FALLBACK_MODAL_IDS) {
        if (id === currentId || keepIds.includes(id)) continue;
        document.getElementById(id)?.classList.add('hidden');
    }
    const preview = document.getElementById('preview-window');
    if (preview && currentId !== 'preview-window' && preview.style.display !== 'none' && preview.style.display !== '') {
        preview.style.display = 'none';
    }
}
