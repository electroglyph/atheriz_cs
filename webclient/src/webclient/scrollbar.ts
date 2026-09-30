/** Custom scrollbar thumb for xterm terminals. Overlay scrollbars (now
 * the default on several platforms, and used by headless Chromium) give
 * the native viewport thumb zero layout space, so no CSS can force it
 * visible. This controller renders its own thumb element instead, shown
 * while scrolled up and hidden at the bottom. The terminal shape below is
 * satisfied by both the DOM terminal and @xterm/headless, which keeps the
 * geometry unit-testable (with jsdom elements) without a browser. */
export interface ScrollableTerminal {
    readonly buffer: {
        readonly active: {
            /** Line where the top of the viewport is. */
            readonly viewportY: number;
            /** Line where the top of the bottom page is (fully scrolled). */
            readonly baseY: number;
            /** Amount of lines in the buffer. */
            readonly length: number;
        };
    };
    /** Visible terminal rows. */
    readonly rows: number;
    onScroll(callback: () => void): { dispose(): void };
    scrollToLine(line: number): void;
}

/** True when the terminal is scrolled above the bottom line. */
export function isScrolledUp(term: ScrollableTerminal): boolean {
    return term.buffer.active.viewportY < term.buffer.active.baseY;
}

/** Thumb top/height in pixels for a track of trackPx. */
export function thumbMetrics(term: ScrollableTerminal, trackPx: number, minPx: number): { topPx: number; heightPx: number } {
    const length = Math.max(1, term.buffer.active.length);
    const topPx = (term.buffer.active.viewportY / length) * trackPx;
    const heightPx = Math.max(minPx, (term.rows / length) * trackPx);
    return { topPx, heightPx };
}

/** Drives one thumb element for one terminal, including drag-to-scroll. */
export class TerminalScrollbar {
    private dragPointerId: number | null = null;
    private dragStartY = 0;
    private dragStartLine = 0;
    private readonly onMoveBound = (event: PointerEvent) => this.onDragMove(event);
    private readonly onUpBound = (event: PointerEvent) => this.onDragEnd(event);

    constructor(
        private readonly term: ScrollableTerminal,
        private readonly container: HTMLElement,
        private readonly thumb: HTMLElement,
        private readonly minPx = 24,
    ) {
        container.appendChild(thumb);
        thumb.addEventListener('pointerdown', (event) => this.onDragStart(event));
        this.term.onScroll(() => this.update());
        this.update();
    }

    /** Recompute visibility and geometry from the live buffer state. */
    update(): void {
        const trackPx = this.container.clientHeight;
        const visible = isScrolledUp(this.term) && this.term.buffer.active.length > this.term.rows;
        this.thumb.classList.toggle('visible', visible);
        if (!visible) return;
        const { topPx, heightPx } = thumbMetrics(this.term, trackPx, this.minPx);
        this.thumb.style.top = `${Math.max(0, Math.min(topPx, trackPx - heightPx))}px`;
        this.thumb.style.height = `${Math.min(heightPx, trackPx)}px`;
    }

    /** Detach the thumb (tests and teardown). */
    dispose(): void {
        this.thumb.remove();
    }

    private onDragStart(event: PointerEvent): void {
        event.preventDefault();
        this.dragPointerId = event.pointerId;
        this.dragStartY = event.clientY;
        this.dragStartLine = this.term.buffer.active.viewportY;
        try {
            this.thumb.setPointerCapture(event.pointerId);
        } catch {
            // Older browsers may not support capture; dragging still works.
        }
        this.thumb.addEventListener('pointermove', this.onMoveBound);
        this.thumb.addEventListener('pointerup', this.onUpBound);
        this.thumb.addEventListener('pointercancel', this.onUpBound);
    }

    private onDragMove(event: PointerEvent): void {
        if (event.pointerId !== this.dragPointerId) return;
        const trackPx = Math.max(1, this.container.clientHeight);
        const deltaLines = ((event.clientY - this.dragStartY) / trackPx) * this.term.buffer.active.length;
        this.term.scrollToLine(Math.round(this.dragStartLine + deltaLines));
    }

    private onDragEnd(event: PointerEvent): void {
        if (event.pointerId !== this.dragPointerId) return;
        this.dragPointerId = null;
        try {
            this.thumb.releasePointerCapture(event.pointerId);
        } catch {
            // Capture was never taken or is unsupported; nothing to release.
        }
        this.thumb.removeEventListener('pointermove', this.onMoveBound);
        this.thumb.removeEventListener('pointerup', this.onUpBound);
        this.thumb.removeEventListener('pointercancel', this.onUpBound);
    }
}
