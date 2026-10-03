// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest';

const terminalWrites = vi.hoisted(() => [] as string[]);
const fitCalls = vi.hoisted(() => ({ count: 0 }));

vi.mock('@xterm/xterm', () => ({
    Terminal: class {
        open() {}
        loadAddon() {}
        write(data: string) {
            terminalWrites.push(data);
        }
        writeln(data: string) {
            terminalWrites.push(`${data}\r\n`);
        }
    },
}));

vi.mock('@xterm/addon-fit', () => ({
    FitAddon: class {
        fit() {
            fitCalls.count++;
        }
    },
}));

class FakeEventSource {
    static instances: FakeEventSource[] = [];
    onmessage: ((event: { data: string }) => void) | null = null;
    onerror: (() => void) | null = null;
    url: string;
    constructor(url: string) {
        this.url = url;
        FakeEventSource.instances.push(this);
    }
    close() {}
}

function markup(): void {
    document.body.innerHTML =
        '<section id="server-log-section"><div id="server-log-terminal"></div></section>';
    terminalWrites.length = 0;
    fitCalls.count = 0;
    FakeEventSource.instances.length = 0;
}

describe('server log viewer', () => {
    it('writes the snapshot then streams live lines', async () => {
        markup();
        vi.stubGlobal(
            'fetch',
            vi.fn(async () => ({ ok: true, text: async () => '1720000000 (server) hi\n' })),
        );
        vi.stubGlobal('EventSource', FakeEventSource);
        await import('../src/serverlog');
        await vi.waitFor(() => expect(terminalWrites.length).toBeGreaterThan(0));
        expect(terminalWrites.join('')).toContain('(server) hi');
        // The section hides before the fetch and reveals on success, so a
        // disabled endpoint never flashes an empty terminal.
        expect(document.getElementById('server-log-section')?.hidden).toBe(false);
        const source = FakeEventSource.instances[0];
        expect(source.url).toBe('/server-log/stream');
        source.onmessage?.({ data: '1720000060 live line' });
        await vi.waitFor(() => expect(terminalWrites.join('')).toContain('live line'));
    });

    it('renders epochs in viewer-local time and passes plain lines through', async () => {
        const { renderLogLine } = await import('../src/serverlog');
        const date = new Date(1720000000 * 1000);
        const rendered = renderLogLine('1720000000 (server) hi');
        expect(rendered).toContain('(server) hi');
        expect(rendered).toContain(`${date.getFullYear()}`);
        expect(rendered).toContain(`:${date.getMinutes() < 10 ? `0${date.getMinutes()}` : `${date.getMinutes()}`}:`);
        expect(renderLogLine('(server) legacy')).toBe('(server) legacy');
    });

    it('opens the stream after the snapshot id so lines do not repeat', async () => {
        vi.resetModules();
        markup();
        vi.stubGlobal(
            'fetch',
            vi.fn(async () => ({
                ok: true,
                headers: { get: (name: string) => (name === 'X-Last-Seq' ? '42' : null) },
                text: async () => '1720000000 (server) hi\n',
            })),
        );
        vi.stubGlobal('EventSource', FakeEventSource);
        await import('../src/serverlog');
        await vi.waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
        // Snapshot renders first; the stream resumes after it instead of
        // replaying the same history (which duplicated every line on reload).
        expect(FakeEventSource.instances[0].url).toBe('/server-log/stream?lastId=42');
        expect(terminalWrites.join('')).toContain('(server) hi');
    });

    it('re-fits the terminal after revealing the section', async () => {
        vi.resetModules();
        markup();
        vi.stubGlobal(
            'fetch',
            vi.fn(async () => ({
                ok: true,
                headers: { get: () => null },
                text: async () => '1720000000 (server) hi\n',
            })),
        );
        vi.stubGlobal('EventSource', FakeEventSource);
        await import('../src/serverlog');
        await vi.waitFor(() => expect(terminalWrites.length).toBeGreaterThan(0));
        // Once at open (while hidden, zero size) and once more after the
        // reveal — without the second fit lines wrap at the hidden width.
        expect(fitCalls.count).toBeGreaterThanOrEqual(2);
        expect(document.getElementById('server-log-section')?.hidden).toBe(false);
    });

    it('hides the section when the endpoint is disabled', async () => {
        vi.resetModules();
        markup();
        vi.stubGlobal(
            'fetch',
            vi.fn(async () => ({ ok: false, text: async () => '' })),
        );
        vi.stubGlobal('EventSource', FakeEventSource);
        await import('../src/serverlog');
        await vi.waitFor(() => {
            expect(document.getElementById('server-log-section')?.hidden).toBe(true);
        });
        expect(FakeEventSource.instances).toHaveLength(0);
    });

    it('re-opens the stream after an error, resuming from the last frame', async () => {
        vi.resetModules();
        markup();
        vi.stubGlobal(
            'fetch',
            vi.fn(async () => ({
                ok: true,
                headers: { get: (name: string) => (name === 'X-Last-Seq' ? '42' : null) },
                text: async () => '1720000000 (server) hi\n',
            })),
        );
        vi.stubGlobal('EventSource', FakeEventSource);
        await import('../src/serverlog');
        await vi.waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
        expect(FakeEventSource.instances[0].url).toBe('/server-log/stream?lastId=42');
        // A live frame arrives, then the connection drops: the viewer must
        // re-open from the delivered frame, not the stale snapshot id —
        // otherwise a wedged tab silently replays or misses lines.
        const first = FakeEventSource.instances[0];
        first.onmessage?.({ data: '1720000060 live line', lastEventId: '43' } as { data: string });
        first.onerror?.();
        await vi.waitFor(() => expect(FakeEventSource.instances).toHaveLength(2), { timeout: 5000 });
        expect(FakeEventSource.instances[1].url).toBe('/server-log/stream?lastId=43');
        // The stream was live, so no unavailable note may appear.
        expect(terminalWrites.join('')).not.toContain('live stream unavailable');
    }, 10000);

    it('writes the unavailable note only once across retries', async () => {
        vi.resetModules();
        markup();
        vi.stubGlobal(
            'fetch',
            vi.fn(async () => ({
                ok: true,
                headers: { get: (name: string) => (name === 'X-Last-Seq' ? '42' : null) },
                text: async () => '1720000000 (server) hi\n',
            })),
        );
        vi.stubGlobal('EventSource', FakeEventSource);
        await import('../src/serverlog');
        await vi.waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
        // Two drops before anything goes live: each schedules a re-open,
        // but the note must appear exactly once.
        FakeEventSource.instances[0].onerror?.();
        await vi.waitFor(() => expect(FakeEventSource.instances).toHaveLength(2), { timeout: 5000 });
        FakeEventSource.instances[1].onerror?.();
        await vi.waitFor(() => expect(FakeEventSource.instances).toHaveLength(3), { timeout: 8000 });
        const notes = terminalWrites.join('').match(/live stream unavailable/g) ?? [];
        expect(notes).toHaveLength(1);
    }, 15000);
});
