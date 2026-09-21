import { describe, expect, it, vi } from 'vitest';
import * as fs from 'node:fs';
import * as path from 'node:path';
import {
    MAX_INBOUND_FRAME_BYTES,
    WebSocketConnection,
    WebSocketLike,
    decodeWireData,
    decodeWireDataAsync,
} from '../src/webclient/connection';
import { getEllipsePerimeter, getLinePoints } from '../src/utils/geometry';
import {
    normalizeServerText,
    promptVisibleLength,
    stripAnsiBroad,
    stripNonSgrAnsi,
    wrapText,
} from '../src/webclient/text';
import { renderMap } from '../src/webclient/map';
import { detectAnsiDimensions, parseAnsiSymbol, parseAnsiToCells, stripAnsi } from '../src/utils/ansiParser';

class FakeSocket implements WebSocketLike {
    readyState = 0;
    binaryType = '';
    onopen: ((event: Event) => void) | null = null;
    onclose: ((event: CloseEvent) => void) | null = null;
    onerror: ((event: Event) => void) | null = null;
    onmessage: ((event: MessageEvent) => void) | null = null;
    sent: string[] = [];
    closeCalls = 0;

    send(data: string): void {
        this.sent.push(data);
    }

    close(): void {
        this.closeCalls += 1;
    }

    open(): void {
        this.readyState = 1;
        this.onopen?.(new Event('open'));
    }

    drop(): void {
        this.readyState = 3;
        this.onclose?.({} as CloseEvent);
    }

    deliver(data: unknown): void {
        this.onmessage?.({ data } as MessageEvent);
    }
}

describe('W3: binary Blob frames decode instead of dropping', () => {
    it('sync decodeWireData still returns null for Blob (compat)', () => {
        expect(decodeWireData(new Blob(['hello']))).toBeNull();
    });

    it('decodeWireDataAsync resolves Blob bodies to text', async () => {
        await expect(decodeWireDataAsync(new Blob(['["text", ["hi"], {}]']))).resolves.toBe('["text", ["hi"], {}]');
        await expect(decodeWireDataAsync('plain')).resolves.toBe('plain');
        await expect(decodeWireDataAsync(42)).resolves.toBeNull();
    });

    it('decodeWireDataAsync falls back to text() when arrayBuffer() fails', async () => {
        const flaky = { arrayBuffer: () => Promise.reject(new Error('denied')), text: () => Promise.resolve('fallback') };
        await expect(decodeWireDataAsync(flaky)).resolves.toBe('fallback');
        const dead = { arrayBuffer: () => Promise.reject(new Error('denied')) };
        await expect(decodeWireDataAsync(dead)).resolves.toBeNull();
    });

    it('connect() requests arraybuffer frames from the socket', () => {
        const socket = new FakeSocket();
        const conn = new WebSocketConnection({ createSocket: () => socket, onMessage: () => undefined });
        conn.connect();
        expect(socket.binaryType).toBe('arraybuffer');
        conn.close();
    });

    it('Blob-like onmessage delivers the parsed message asynchronously', async () => {
        const socket = new FakeSocket();
        const messages: string[] = [];
        const invalid: number[] = [];
        const conn = new WebSocketConnection({
            createSocket: () => socket,
            onMessage: (message) => messages.push(message.command),
            onInvalidMessage: () => invalid.push(1),
        });
        conn.connect();
        socket.open();
        socket.deliver(new Blob(['["text", ["hello"], {}]']));
        // Still async: nothing delivered synchronously.
        expect(messages).toEqual([]);
        await vi.waitFor(() => {
            expect(messages).toEqual(['text']);
        });
        expect(invalid).toEqual([]);
        conn.close();
    });
});

describe('U10: connection hardening (timeout, frame cap, send guard)', () => {
    it('a stalled connect closes the socket and schedules a reconnect', () => {
        vi.useFakeTimers();
        try {
            const sockets: FakeSocket[] = [];
            const states: string[] = [];
            const conn = new WebSocketConnection({
                createSocket: () => {
                    const socket = new FakeSocket();
                    sockets.push(socket);
                    return socket;
                },
                onMessage: () => undefined,
                onStateChange: (state) => states.push(state),
                minReconnectDelayMs: 50,
                maxReconnectDelayMs: 50,
                connectTimeoutMs: 1000,
            });
            conn.connect();
            expect(sockets).toHaveLength(1);
            expect(conn.getState()).toBe('connecting');
            vi.advanceTimersByTime(1000);
            expect(sockets[0].closeCalls).toBe(1);
            vi.advanceTimersByTime(100);
            expect(sockets).toHaveLength(2);
            expect(conn.getState()).toBe('connecting');
            expect(states).toContain('closed');
            conn.close();
        } finally {
            vi.useRealTimers();
        }
    });

    it('an opened socket is not timed out', () => {
        vi.useFakeTimers();
        try {
            const sockets: FakeSocket[] = [];
            const conn = new WebSocketConnection({
                createSocket: () => {
                    const socket = new FakeSocket();
                    sockets.push(socket);
                    return socket;
                },
                onMessage: () => undefined,
                connectTimeoutMs: 1000,
            });
            conn.connect();
            sockets[0].open();
            vi.advanceTimersByTime(60_000);
            expect(sockets).toHaveLength(1);
            expect(sockets[0].closeCalls).toBe(0);
            conn.close();
        } finally {
            vi.useRealTimers();
        }
    });

    it('oversize string frames are invalid, not parsed', () => {
        const socket = new FakeSocket();
        const messages: string[] = [];
        let invalid = 0;
        const conn = new WebSocketConnection({
            createSocket: () => socket,
            onMessage: (message) => messages.push(message.command),
            onInvalidMessage: () => {
                invalid += 1;
            },
        });
        conn.connect();
        socket.open();
        socket.deliver('x'.repeat(MAX_INBOUND_FRAME_BYTES + 1));
        expect(invalid).toBe(1);
        expect(messages).toEqual([]);
        conn.close();
    });

    it('send returns false instead of throwing on unstringifiable args', () => {
        const socket = new FakeSocket();
        const conn = new WebSocketConnection({ createSocket: () => socket, onMessage: () => undefined });
        conn.connect();
        socket.open();
        const circular: Record<string, unknown> = {};
        circular.self = circular;
        expect(conn.send('text', [circular])).toBe(false);
        expect(socket.sent).toEqual([]);
        conn.close();
    });
});

describe('W4: geometry rejects non-finite input instead of hanging', () => {
    it('getLinePoints returns [] for NaN/Infinity endpoints', () => {
        expect(getLinePoints({ x: NaN, y: 0 }, { x: 5, y: 5 })).toEqual([]);
        expect(getLinePoints({ x: 0, y: 0 }, { x: Infinity, y: 5 })).toEqual([]);
        expect(getLinePoints({ x: 0, y: -Infinity }, { x: 5, y: 5 })).toEqual([]);
        expect(getLinePoints({ x: 0, y: 0 }, { x: 5, y: NaN })).toEqual([]);
    });

    it('getLinePoints still draws finite lines', () => {
        expect(getLinePoints({ x: 0, y: 0 }, { x: 2, y: 0 })).toEqual([
            { x: 0, y: 0 },
            { x: 1, y: 0 },
            { x: 2, y: 0 },
        ]);
    });

    it('getEllipsePerimeter returns [] for non-finite bounds', () => {
        expect(getEllipsePerimeter(NaN, 0, 5, 5)).toEqual([]);
        expect(getEllipsePerimeter(0, 0, Infinity, 5)).toEqual([]);
        expect(getEllipsePerimeter(0, 0, 5, -Infinity)).toEqual([]);
    });

    it('getEllipsePerimeter still draws finite ellipses', () => {
        const points = getEllipsePerimeter(0, 0, 4, 4);
        expect(points.length).toBeGreaterThan(0);
        for (const point of points) {
            expect(Number.isFinite(point.x)).toBe(true);
            expect(Number.isFinite(point.y)).toBe(true);
        }
    });
});

describe('W7: broad ANSI stripping (non-SGR, OSC, lone ESC)', () => {
    it('stripAnsiBroad removes CSI clears, cursor moves, OSC, and lone ESC', () => {
        expect(stripAnsiBroad('\x1b[2Jhello')).toBe('hello');
        expect(stripAnsiBroad('\x1b[1;1Hhi')).toBe('hi');
        expect(stripAnsiBroad('a\x1b]8;;http://example\x07b')).toBe('ab');
        expect(stripAnsiBroad('a\x1b]8;;http://example\x1b\\b')).toBe('ab');
        expect(stripAnsiBroad('a\x1bB')).toBe('aB');
        expect(stripAnsiBroad('\x1b[31mred\x1b[0m')).toBe('red');
        expect(stripAnsiBroad('plain')).toBe('plain');
    });

    it('stripNonSgrAnsi keeps SGR colors but drops control sequences', () => {
        expect(stripNonSgrAnsi('\x1b[31mred\x1b[0m\x1b[2J')).toBe('\x1b[31mred\x1b[0m');
        expect(stripNonSgrAnsi('a\x1b]8;;http://example\x07b')).toBe('ab');
        expect(stripNonSgrAnsi('\x1b[1;1H\x1b[38;2;1;2;3mX')).toBe('\x1b[38;2;1;2;3mX');
        expect(stripNonSgrAnsi('plain')).toBe('plain');
    });

    it('promptVisibleLength ignores non-SGR sequences', () => {
        expect(promptVisibleLength('\x1b[2Jhi')).toBe(2);
        expect(promptVisibleLength('\x1b]8;;http://example\x07hi')).toBe(2);
        expect(promptVisibleLength('\x1b[31mred\x1b[0m> ')).toBe(5);
    });

    it('wrapText wraps at the same place with non-SGR prefixes present', () => {
        expect(wrapText('\x1b[2Jone two', 5)).toBe(`\x1b[2J${wrapText('one two', 5)}`);
    });

    it('normalizeServerText keeps SGR color behavior identical', () => {
        const red = '\x1b[38;2;255;0;0m';
        expect(normalizeServerText(`${red}red\x1b[0m`, 80, false)).toContain(red);
    });

    it('legend output sanitizes desc/area but keeps symbol SGR colors', () => {
        const output = renderMap(
            {
                map: 'ab',
                max_y: 0,
                area: 'T\x1b]8;;http://evil\x07own',
                legend: [
                    { symbol: '\x1b[38;2;255;0;0mX\x1b[0m', desc: '\x1b[2JEvil Door' },
                ],
            } as unknown as Parameters<typeof renderMap>[0],
            40,
            12,
        );
        expect(output).not.toContain('\x1b[2J');
        expect(output).not.toContain('\x1b]8;;');
        expect(output).toContain('Evil Door');
        expect(output).toContain('\x1b[38;2;255;0;0mX');
        expect(output).toContain('Town');
    });

    it('main.ts sanitizes echoed user input and raw server commands', () => {
        const main = fs.readFileSync(path.resolve(import.meta.dirname, '../src/webclient/main.ts'), 'utf-8');
        expect(main).toContain('stripAnsiBroad');
        expect(main).toContain('Unknown command: ${stripAnsiBroad(');
        expect(main).toContain('${stripAnsiBroad(text)}');
        expect(main).toContain('Unknown server command: ${stripAnsiBroad(message.command)}');
    });
});

describe('U9: ansiParser bounds (dims, canvas width, SGR bytes)', () => {
    it('stripAnsi removes non-SGR and OSC sequences', () => {
        expect(stripAnsi('\x1b[2J\x1b[31mX\x1b[0m')).toBe('X');
        expect(stripAnsi('a\x1b]8;;http://example\x07b')).toBe('ab');
        expect(stripAnsi('\x1b[31mred\x1b[0m')).toBe('red');
    });

    it('detectAnsiDimensions clamps the untrusted size header', () => {
        expect(detectAnsiDimensions('\x1b[8;9999;9999t...')).toEqual({ width: 500, height: 500 });
        expect(detectAnsiDimensions('\x1b[8;0;0t...')).toEqual({ width: 1, height: 1 });
        expect(detectAnsiDimensions('\x1b[8;5;10t...')).toEqual({ width: 10, height: 5 });
    });

    it('parseAnsiSymbol clamps SGR RGB bytes to 0..255 finite', () => {
        const hot = parseAnsiSymbol('\x1b[38;2;999;0;300mX\x1b[0m');
        expect(hot.fg).toEqual([255, 0, 255]);
        expect(hot.fg.every(Number.isFinite)).toBe(true);
        const palette = parseAnsiSymbol('\x1b[38;5;999mX\x1b[0m');
        expect(palette.fg).toEqual([255, 255, 255]);
    });

    it('parseAnsiToCells survives a zero canvas width', async () => {
        const cells = await parseAnsiToCells('', 0);
        expect(cells).toHaveLength(1);
    });
});
