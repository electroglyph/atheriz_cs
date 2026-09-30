// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { Terminal } from '@xterm/headless';
import { TerminalScrollbar, isScrolledUp, thumbMetrics } from '../src/webclient/scrollbar';

function mount(heightPx: number): HTMLElement {
    const el = document.createElement('div');
    Object.defineProperty(el, 'clientHeight', { value: heightPx });
    document.body.appendChild(el);
    return el;
}

function thumb(container: HTMLElement): HTMLElement {
    const el = document.createElement('div');
    el.className = 'terminal-scrollbar-thumb';
    return el;
}

function writeLines(term: Terminal, count: number): Promise<void> {
    return new Promise((resolve) => {
        let text = '';
        for (let i = 0; i < count; i++) text += `line ${i}\n`;
        term.write(text, () => resolve());
    });
}

async function fullTerminal(): Promise<Terminal> {
    const term = new Terminal({ cols: 80, rows: 5, scrollback: 50, allowProposedApi: true });
    await writeLines(term, 30);
    return term;
}

function pointer(target: HTMLElement, type: string, init: { pointerId: number; clientY: number }): void {
    const event = new Event(type, { bubbles: true }) as Event & { pointerId?: number; clientY?: number };
    event.pointerId = init.pointerId;
    event.clientY = init.clientY;
    target.dispatchEvent(event);
}

describe('terminal scrollbar', () => {
    it('hides the thumb at the bottom', async () => {
        const term = await fullTerminal();
        expect(isScrolledUp(term)).toBe(false);
        const container = mount(300);
        const bar = new TerminalScrollbar(term, container, thumb(container));
        expect(container.querySelector('.terminal-scrollbar-thumb')).not.toBeNull();
        expect(container.querySelector('.visible')).toBeNull();
        bar.dispose();
    });

    it('shows a proportional thumb after scrolling up', async () => {
        const term = await fullTerminal();
        const container = mount(300);
        const el = thumb(container);
        const bar = new TerminalScrollbar(term, container, el);
        term.scrollLines(-3);
        bar.update();
        expect(isScrolledUp(term)).toBe(true);
        expect(el.classList.contains('visible')).toBe(true);
        const length = term.buffer.active.length;
        const expectedTop = (term.buffer.active.viewportY / length) * 300;
        expect(parseFloat(el.style.top)).toBeCloseTo(expectedTop, 0);
        expect(parseFloat(el.style.height)).toBeGreaterThanOrEqual(24);
        term.scrollToBottom();
        bar.update();
        expect(el.classList.contains('visible')).toBe(false);
        bar.dispose();
    });

    it('computes thumb fractions from buffer state', () => {
        const fake = { buffer: { active: { viewportY: 27, baseY: 30, length: 35 } }, rows: 5 };
        const { topPx, heightPx } = thumbMetrics(fake as never, 300, 24);
        expect(topPx).toBeCloseTo((27 / 35) * 300, 5);
        expect(heightPx).toBeCloseTo(Math.max(24, (5 / 35) * 300), 5);
    });

    it('drags the viewport by pointer', async () => {
        const term = await fullTerminal();
        const container = mount(300);
        const el = thumb(container);
        const bar = new TerminalScrollbar(term, container, el);
        term.scrollLines(-10);
        bar.update();
        const startLine = term.buffer.active.viewportY;
        const length = term.buffer.active.length;
        pointer(el, 'pointerdown', { pointerId: 7, clientY: 100 });
        pointer(el, 'pointermove', { pointerId: 7, clientY: 160 });
        const expected = Math.round(startLine + ((160 - 100) / 300) * length);
        expect(term.buffer.active.viewportY).toBe(expected);
        pointer(el, 'pointerup', { pointerId: 7, clientY: 160 });
        const settled = term.buffer.active.viewportY;
        pointer(el, 'pointermove', { pointerId: 9, clientY: 290 });
        expect(term.buffer.active.viewportY).toBe(settled);
        bar.dispose();
    });

    it('stays hidden when nothing scrolled', async () => {
        const term = new Terminal({ cols: 80, rows: 5, scrollback: 50, allowProposedApi: true });
        await writeLines(term, 2);
        expect(isScrolledUp(term)).toBe(false);
    });
});
