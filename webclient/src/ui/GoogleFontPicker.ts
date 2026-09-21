import { GOOGLE_FONTS, GOOGLE_FONT_CATEGORIES, GoogleFontCategory } from '../data/googleFonts';
import { loadFontPreview, preloadManifest } from '../utils/googleFontLoader';
import { sanitizeFontFamily } from '../utils/cssFont';
import { closeOtherModals, visibleModalIds } from './modalHelper';

const PAGE_SIZE = 40;
// Max concurrent preview loads to avoid flooding the network with requests
const PREVIEW_CONCURRENCY = 4;

function escapeCss(value: string): string {
    if (typeof CSS !== 'undefined' && typeof (CSS as unknown as { escape?: (s: string) => string }).escape === 'function') {
        return (CSS as unknown as { escape: (s: string) => string }).escape(value);
    }
    // Fallback when CSS.escape is unavailable: escape every character outside
    // the safe set as `\XXXXXX ` (codepoint hex + trailing space). A bare
    // backslash prefix is NOT enough inside a quoted attribute selector:
    // `"`, `]` and whitespace would still terminate or split the selector.
    return value.replace(/[^a-zA-Z0-9_-]/g, (ch) => {
        const cp = ch.codePointAt(0) ?? 0;
        return `\\${cp.toString(16).padStart(6, '0')} `;
    });
}

const CATEGORY_TABS: { label: string; category: GoogleFontCategory | 'all' }[] = [
    { label: 'All', category: 'all' },
    ...GOOGLE_FONT_CATEGORIES.map(c => ({ label: c, category: c as GoogleFontCategory })),
];

export class GoogleFontPicker {
    private modal: HTMLElement;
    private searchInput: HTMLInputElement;
    private listContainer: HTMLElement;
    private tabContainer: HTMLElement;
    private btnCancel: HTMLButtonElement;
    private btnOk: HTMLButtonElement;
    private onSelect: (family: string) => void;

    private selectedFamily: string | null = null;

    /** Element that had focus before the picker opened; restored on close. */
    private returnFocusTo: HTMLElement | null = null;

    private activeCategory: GoogleFontCategory | 'all' = 'all';
    private searchQuery = '';
    private filteredFonts: { family: string; category: string }[] = [];
    private renderedCount = 0;
    private observer: IntersectionObserver;
    private sentinel: HTMLElement;

    /** IntersectionObserver that lazily loads preview fonts when items scroll into view */
    private fontObserver: IntersectionObserver;
    /** Queue of font families waiting to be preview-loaded */
    private previewQueue: string[] = [];
    private previewInFlight = 0;
    private searchDebounce: ReturnType<typeof setTimeout> | null = null;
    private destroyed = false;

    private boundDocumentKeyDown = (e: KeyboardEvent) => {
        if (this.modal.classList.contains('hidden')) return;
        if (e.key === 'Escape') {
            // Picker-only: don't let this leak to canvas tools underneath
            // (e.g. SelectionTool would clear the canvas selection too).
            e.preventDefault();
            e.stopPropagation();
            this.close();
        } else if (e.key === 'Enter' && this.selectedFamily) {
            if (e.target === this.searchInput) {
                // Typing Enter in the search box must not confirm a stale
                // selection made before the query; highlight the top hit.
                e.preventDefault();
                this.selectFirstVisible();
            } else {
                e.preventDefault();
                e.stopPropagation();
                this.confirm();
            }
        }
    };

    constructor(onSelect: (family: string) => void) {
        this.onSelect = onSelect;

        this.modal = document.getElementById('google-font-picker-modal')!;
        this.searchInput = document.getElementById('gfp-search') as HTMLInputElement;
        this.listContainer = document.getElementById('gfp-list')!;
        this.tabContainer = document.getElementById('gfp-tabs')!;
        this.btnCancel = document.getElementById('gfp-cancel') as HTMLButtonElement;
        this.btnOk = document.getElementById('gfp-ok') as HTMLButtonElement;
        this.sentinel = document.getElementById('gfp-sentinel')!;

        this.buildTabs();
        this.bindEvents();

        this.observer = new IntersectionObserver((entries) => {
            if (entries.some(e => e.isIntersecting)) {
                this.renderMore();
            }
        }, { root: this.listContainer, rootMargin: '200px' });

        // Lazy font loader: fires only when a font item enters the viewport
        this.fontObserver = new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    const family = (entry.target as HTMLElement).dataset.family;
                    if (family) this.enqueuePreview(family);
                    this.fontObserver.unobserve(entry.target);
                }
            }
        }, { root: this.listContainer, rootMargin: '100px' });
    }

    private buildTabs() {
        this.tabContainer.innerHTML = '';
        for (const tab of CATEGORY_TABS) {
            const btn = document.createElement('button');
            btn.textContent = tab.label;
            btn.className = 'gfp-tab' + (tab.category === this.activeCategory ? ' active' : '');
            btn.addEventListener('click', () => {
                this.activeCategory = tab.category;
                this.updateActiveTab();
                this.applyFilter();
            });
            this.tabContainer.appendChild(btn);
        }
    }

    private updateActiveTab() {
        const btns = this.tabContainer.querySelectorAll('.gfp-tab');
        btns.forEach((btn, i) => {
            btn.classList.toggle('active', CATEGORY_TABS[i].category === this.activeCategory);
        });
    }

    private bindEvents() {
        this.btnCancel.addEventListener('click', () => this.close());
        this.btnOk.addEventListener('click', () => this.confirm());

        this.searchInput.addEventListener('input', () => {
            if (this.searchDebounce) clearTimeout(this.searchDebounce);
            this.searchDebounce = setTimeout(() => {
                this.searchDebounce = null;
                this.searchQuery = this.searchInput.value.toLowerCase().trim();
                this.applyFilter();
            }, 150);
        });

        this.modal.addEventListener('click', (e) => {
            if (e.target === this.modal) this.close();
        });

        document.addEventListener('keydown', this.boundDocumentKeyDown);
    }

    public destroy() {
        if (this.destroyed) return;
        this.destroyed = true;
        document.removeEventListener('keydown', this.boundDocumentKeyDown);
        if (this.searchDebounce) {
            clearTimeout(this.searchDebounce);
            this.searchDebounce = null;
        }
        this.observer.disconnect();
        this.fontObserver.disconnect();
        this.previewQueue.length = 0;
    }

    private applyFilter() {
        this.filteredFonts = GOOGLE_FONTS.filter(f => {
            if (this.activeCategory !== 'all' && f.category !== this.activeCategory) return false;
            if (this.searchQuery && !f.family.toLowerCase().includes(this.searchQuery)) return false;
            return true;
        });
        this.renderedList();
    }

    private renderedList() {
        this.listContainer.innerHTML = '';
        this.renderedCount = 0;

        this.sentinel = document.createElement('div');
        this.sentinel.id = 'gfp-sentinel';
        this.listContainer.appendChild(this.sentinel);

        this.observer.disconnect();
        this.observer.observe(this.sentinel);

        // Stop observing discarded items so detached nodes are not retained.
        this.fontObserver.disconnect();

        this.renderMore();
    }

    private renderMore() {
        const start = this.renderedCount;
        const end = Math.min(start + PAGE_SIZE, this.filteredFonts.length);

        const fragment = document.createDocumentFragment();
        for (let i = start; i < end; i++) {
            const f = this.filteredFonts[i];
            const item = document.createElement('div');
            item.className = 'gfp-item';
            item.textContent = f.family;
            // Font is applied via CSS once loaded; fontFamily is set after load to avoid
            // the browser trying to shape text with an unloaded font immediately.
            item.dataset.family = f.family;
            item.addEventListener('click', () => {
                this.selectItem(item, f.family);
            });
            item.addEventListener('dblclick', () => {
                this.selectItem(item, f.family);
                this.confirm();
            });
            // Observe for lazy font loading instead of loading eagerly
            this.fontObserver.observe(item);
            fragment.appendChild(item);
        }

        this.listContainer.insertBefore(fragment, this.sentinel);
        this.renderedCount = end;
    }

    /** Highlight a single item as the pending selection; confirms nothing. */
    private selectItem(item: HTMLElement, family: string): void {
        this.listContainer.querySelectorAll('.gfp-item.selected').forEach(el => {
            el.classList.remove('selected');
        });
        item.classList.add('selected');
        this.selectedFamily = family;
        this.btnOk.disabled = false;
    }

    /** Highlight the top filtered font; confirms nothing. */
    private selectFirstVisible(): void {
        const first = this.filteredFonts[0];
        if (!first) return;
        const item = this.listContainer.querySelector(`[data-family="${escapeCss(first.family)}"]`);
        if (item instanceof HTMLElement) {
            this.selectItem(item, first.family);
        } else {
            // Item not rendered (virtualized below the fold): still record
            // the pending selection so Use Font applies it.
            this.selectedFamily = first.family;
            this.btnOk.disabled = false;
        }
    }

    /** Apply the highlighted font, if any, and dismiss the picker. */
    private confirm(): void {
        if (this.modal.classList.contains('hidden')) return;
        if (!this.selectedFamily) return;
        // Idempotent: Enter on a focused Use Font button fires both keydown
        // and click, which would otherwise apply the font twice.
        const family = this.selectedFamily;
        this.selectedFamily = null;
        this.onSelect(family);
        this.close();
    }

    /**
     * Enqueue a font for preview loading, respecting the concurrency limit
     * so we don't flood the dev server with dozens of simultaneous requests.
     */
    private enqueuePreview(family: string): void {
        this.previewQueue.push(family);
        this.drainPreviewQueue();
    }

    private drainPreviewQueue(): void {
        while (this.previewInFlight < PREVIEW_CONCURRENCY && this.previewQueue.length > 0) {
            const family = this.previewQueue.shift()!;
            this.previewInFlight++;
            // loadFontPreview is fire-and-forget (void); use a small setTimeout
            // to yield back to the browser between loads.
            Promise.resolve().then(() => {
                loadFontPreview(family);
                // Once the stylesheet is injected, apply fontFamily to the item element.
                // The family is sanitized first: it flows into a style string
                // where quotes or semicolons could break out of the value.
                const item = this.listContainer.querySelector(`[data-family="${escapeCss(family)}"]`) as HTMLElement | null;
                if (item) item.style.fontFamily = `"${sanitizeFontFamily(family)}", sans-serif`;
            }).finally(() => {
                this.previewInFlight--;
                this.drainPreviewQueue();
            });
        }
    }

    public open() {
        // Sub-dialog: stack above whatever is already open (e.g. the Text
        // tool) instead of hiding it. Hiding the parent here is what used to
        // nuke the whole text dialog when a font was picked, because closing
        // the picker then left both modals hidden.
        closeOtherModals('google-font-picker-modal', visibleModalIds());
        this.returnFocusTo = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        preloadManifest();
        this.searchInput.value = '';
        this.searchQuery = '';
        this.activeCategory = 'all';
        this.selectedFamily = null;
        this.btnOk.disabled = true;
        this.updateActiveTab();
        this.applyFilter();
        this.modal.classList.remove('hidden');
        this.searchInput.focus();
    }

    public close() {
        this.modal.classList.add('hidden');
        // Stop observing all items immediately so pending preview loads are
        // not triggered after the picker is dismissed, preventing extra
        // font downloads.
        this.fontObserver.disconnect();
        this.previewQueue.length = 0;
        // Do NOT reset previewInFlight: in-flight loads still settle and
        // decrement it in their finally blocks; zeroing here would drive the
        // counter negative and admit extra concurrent loads on next open.
        // Hand focus back where it was (usually the G Fonts button) so the
        // parent dialog stays keyboard-usable.
        if (this.returnFocusTo?.isConnected) this.returnFocusTo.focus();
        this.returnFocusTo = null;
    }
}
