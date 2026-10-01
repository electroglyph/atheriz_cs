// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest';

const terminalWrites = vi.hoisted(() => [] as string[]);

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
        fit() {}
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
});
