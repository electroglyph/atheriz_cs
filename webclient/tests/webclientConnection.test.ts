import { describe, expect, it, vi } from 'vitest';
import { WebSocketConnection, WebSocketLike, GAME_CLIENT_MAX_RECONNECT_ATTEMPTS, gameTokenFromFragment, websocketUrl } from '../src/webclient/connection';

class FakeSocket implements WebSocketLike {
    readyState = 0;
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

    fail(): void {
        this.onerror?.(new Event('error'));
    }
}

describe('webclient connection', () => {
    it('derives ws and wss endpoints from the current host', () => {
        expect(websocketUrl({ protocol: 'http:', host: 'example.test:9999' })).toBe('ws://example.test:9999/ws');
        expect(websocketUrl({ protocol: 'https:', host: 'example.test' })).toBe('wss://example.test/ws');
    });

    it('forwards the landing-page token as a ws query', () => {
        expect(websocketUrl({ protocol: 'http:', host: 'example.test' }, 'abc123')).toBe('ws://example.test/ws?token=abc123');
        expect(websocketUrl({ protocol: 'http:', host: 'example.test' }, null)).toBe('ws://example.test/ws');
        expect(websocketUrl({ protocol: 'http:', host: 'example.test' }, '')).toBe('ws://example.test/ws');
    });

    it('reads the token from the url fragment', () => {
        expect(gameTokenFromFragment('#token=abc123')).toBe('abc123');
        expect(gameTokenFromFragment('#token=' + encodeURIComponent('a/b+c='))).toBe('a/b+c=');
        expect(gameTokenFromFragment('')).toBeNull();
        expect(gameTokenFromFragment('#other=1')).toBeNull();
        expect(gameTokenFromFragment('#token=')).toBeNull();
    });

    it('parses messages and sends only while open', () => {
        const socket = new FakeSocket();
        const messages: string[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => socket,
            onMessage: (message) => messages.push(message.command),
            minReconnectDelayMs: 1000,
        });

        connection.connect();
        expect(connection.send('text', ['ignored'])).toBe(false);
        socket.open();
        expect(connection.send('launch_draw')).toBe(true);
        socket.onmessage?.(new MessageEvent('message', { data: '["text", ["hello"], {}]' }));

        expect(socket.sent).toEqual(['["launch_draw",[],{}]']);
        expect(messages).toEqual(['text']);
        connection.close();
        expect(socket.closeCalls).toBe(1);
    });

    it('reconnects after an unexpected close', () => {
        vi.useFakeTimers();
        const sockets: FakeSocket[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => {
                const socket = new FakeSocket();
                sockets.push(socket);
                return socket;
            },
            onMessage: () => undefined,
            minReconnectDelayMs: 100,
            maxReconnectDelayMs: 100,
        });

        connection.connect();
        sockets[0].drop();
        vi.advanceTimersByTime(200);
        expect(sockets).toHaveLength(2);
        connection.close();
        vi.useRealTimers();
    });

    it('gives up after three reconnect attempts', () => {
        vi.useFakeTimers();
        vi.spyOn(Math, 'random').mockReturnValue(0);
        const sockets: FakeSocket[] = [];
        const states: string[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => {
                const socket = new FakeSocket();
                sockets.push(socket);
                return socket;
            },
            onMessage: () => undefined,
            onStateChange: (state) => states.push(state),
            minReconnectDelayMs: 100,
            maxReconnectDelayMs: 100,
        });

        connection.connect();
        sockets[0].open();
        for (const socket of sockets) socket.drop();
        vi.advanceTimersByTime(1000);
        for (const socket of sockets) socket.drop();
        vi.advanceTimersByTime(1000);
        for (const socket of sockets) socket.drop();
        vi.advanceTimersByTime(1000);
        sockets[3].drop();
        vi.advanceTimersByTime(60_000);

        expect(sockets).toHaveLength(4);
        expect(connection.getState()).toBe('failed');
        expect(states.filter((state) => state === 'failed')).toHaveLength(1);

        connection.connect();
        expect(connection.getState()).toBe('connecting');
        sockets[4].drop();
        vi.advanceTimersByTime(1000);
        expect(sockets).toHaveLength(6);
        connection.close();
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('does not reset the retry budget for instantly flapping connections', () => {
        vi.useFakeTimers();
        vi.spyOn(Math, 'random').mockReturnValue(0);
        const sockets: FakeSocket[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => {
                const socket = new FakeSocket();
                sockets.push(socket);
                return socket;
            },
            onMessage: () => undefined,
            minReconnectDelayMs: 100,
            maxReconnectDelayMs: 100,
        });

        connection.connect();
        for (const socket of sockets) {
            socket.open();
            socket.drop();
        }
        vi.advanceTimersByTime(100);
        for (const socket of sockets) {
            socket.open();
            socket.drop();
        }
        vi.advanceTimersByTime(100);
        for (const socket of sockets) {
            socket.open();
            socket.drop();
        }
        vi.advanceTimersByTime(100);
        sockets[3].open();
        sockets[3].drop();
        vi.advanceTimersByTime(60_000);

        expect(sockets).toHaveLength(4);
        expect(connection.getState()).toBe('failed');
        connection.close();
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('gives up after three failed retries even after a stable connection', () => {
        vi.useFakeTimers();
        vi.spyOn(Math, 'random').mockReturnValue(0);
        const sockets: FakeSocket[] = [];
        const states: string[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => {
                const socket = new FakeSocket();
                sockets.push(socket);
                return socket;
            },
            onMessage: () => undefined,
            onStateChange: (state) => states.push(state),
            minReconnectDelayMs: 100,
            maxReconnectDelayMs: 100,
        });

        connection.connect();
        sockets[0].open();
        vi.advanceTimersByTime(31_000);
        sockets[0].drop();
        vi.advanceTimersByTime(100);
        for (let i = 1; i < 10 && i < sockets.length; i += 1) {
            sockets[i].drop();
            vi.advanceTimersByTime(100);
        }
        vi.advanceTimersByTime(60_000);

        expect(sockets).toHaveLength(4);
        expect(connection.getState()).toBe('failed');
        connection.close();
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('resets the retry budget after a stable connection drops', () => {
        vi.useFakeTimers();
        vi.spyOn(Math, 'random').mockReturnValue(0);
        const sockets: FakeSocket[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => {
                const socket = new FakeSocket();
                sockets.push(socket);
                return socket;
            },
            onMessage: () => undefined,
            minReconnectDelayMs: 100,
            maxReconnectDelayMs: 100,
        });

        connection.connect();
        sockets[0].open();
        vi.advanceTimersByTime(31_000);
        sockets[0].drop();
        vi.advanceTimersByTime(100);

        expect(sockets).toHaveLength(2);
        expect(connection.getState()).toBe('connecting');
        connection.close();
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('ignores close events from stale sockets', () => {
        vi.useFakeTimers();
        vi.spyOn(Math, 'random').mockReturnValue(0);
        const sockets: FakeSocket[] = [];
        const states: string[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => {
                const socket = new FakeSocket();
                sockets.push(socket);
                return socket;
            },
            onMessage: () => undefined,
            onStateChange: (state) => states.push(state),
            minReconnectDelayMs: 100,
            maxReconnectDelayMs: 100,
        });

        connection.connect();
        sockets[0].drop();
        vi.advanceTimersByTime(100);
        sockets[1].open();
        sockets[0].drop();

        expect(connection.getState()).toBe('open');
        expect(states.at(-1)).toBe('open');
        connection.close();
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('game client budget survives a restart-length outage', () => {
        vi.useFakeTimers();
        vi.spyOn(Math, 'random').mockReturnValue(0);
        const sockets: FakeSocket[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => {
                const socket = new FakeSocket();
                sockets.push(socket);
                return socket;
            },
            onMessage: () => undefined,
            maxReconnectAttempts: GAME_CLIENT_MAX_RECONNECT_ATTEMPTS,
        });

        connection.connect();
        sockets[0].open();
        // In-game restart: the server drops the socket and stays down
        // through build + checkpoint + handover + boot (~6 s fast path,
        // longer when the build is cold). Every redial fails too.
        sockets[0].drop();
        for (let elapsed = 0; elapsed < 80_000; elapsed += 16_000) {
            vi.advanceTimersByTime(16_000);
            for (const socket of sockets) socket.drop();
        }

        // Still retrying: the class default of 3 gives up after ~3.5 s,
        // so outliving 80 s proves the game budget, not the default.
        expect(sockets.length).toBeGreaterThan(4);
        expect(connection.getState()).not.toBe('failed');
        // The replacement server finally answers the next redial.
        vi.advanceTimersByTime(16_000);
        expect(connection.getState()).toBe('connecting');
        sockets[sockets.length - 1].open();
        expect(connection.getState()).toBe('open');
        connection.close();
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('waits out an announced restart without consuming the retry budget', () => {
        vi.useFakeTimers();
        vi.spyOn(Math, 'random').mockReturnValue(0);
        const sockets: FakeSocket[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => {
                const socket = new FakeSocket();
                sockets.push(socket);
                return socket;
            },
            onMessage: () => undefined,
            minReconnectDelayMs: 100,
            maxReconnectDelayMs: 100,
        });

        connection.connect();
        sockets[0].open();
        connection.notifyRestarting(60_000);
        expect(connection.isWaitingForRestart()).toBe(true);
        // Ten refused redials would fail the default budget three times
        // over; the announced wait rides them all out.
        for (let i = 0; i < 10; i += 1) {
            for (const socket of sockets) socket.drop();
            vi.advanceTimersByTime(1000);
        }

        expect(sockets.length).toBeGreaterThan(4);
        expect(connection.getState()).not.toBe('failed');
        expect(connection.isWaitingForRestart()).toBe(true);
        // The replacement binds inside the wait: back promptly.
        sockets[sockets.length - 1].open();
        expect(connection.getState()).toBe('open');
        expect(connection.isWaitingForRestart()).toBe(false);
        connection.close();
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('hands a fresh budget to the world after the restart wait expires', () => {
        vi.useFakeTimers();
        vi.spyOn(Math, 'random').mockReturnValue(0);
        const sockets: FakeSocket[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => {
                const socket = new FakeSocket();
                sockets.push(socket);
                return socket;
            },
            onMessage: () => undefined,
            minReconnectDelayMs: 100,
            maxReconnectDelayMs: 100,
        });

        connection.connect();
        sockets[0].open();
        connection.notifyRestarting(500);
        for (const socket of sockets) socket.drop();
        vi.advanceTimersByTime(1000);
        expect(connection.isWaitingForRestart()).toBe(false);

        // Past the wait the normal budget applies from zero: attempts
        // burned while waiting must neither fail instantly nor never.
        for (const socket of sockets) socket.drop();
        vi.advanceTimersByTime(100);
        for (const socket of sockets) socket.drop();
        vi.advanceTimersByTime(100);
        for (const socket of sockets) socket.drop();
        vi.advanceTimersByTime(100);
        for (const socket of sockets) socket.drop();
        vi.advanceTimersByTime(60_000);

        expect(connection.getState()).toBe('failed');
        connection.close();
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('manual close drops a pending restart wait', () => {
        const connection = new WebSocketConnection({
            createSocket: () => new FakeSocket(),
            onMessage: () => undefined,
        });

        connection.notifyRestarting();
        expect(connection.isWaitingForRestart()).toBe(true);
        connection.close();
        expect(connection.isWaitingForRestart()).toBe(false);
    });

    it('reports errors from the active socket', () => {
        const socket = new FakeSocket();
        const errors: Event[] = [];
        const connection = new WebSocketConnection({
            createSocket: () => socket,
            onMessage: () => undefined,
            onError: (event) => errors.push(event),
        });

        connection.connect();
        socket.fail();

        expect(errors).toHaveLength(1);
    });
});
