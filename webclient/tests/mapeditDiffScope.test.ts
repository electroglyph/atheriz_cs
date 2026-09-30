// @ts-nocheck
import { describe, expect, it, vi } from 'vitest';
import { WebSocketLike } from '../src/webclient/connection';
import { CanvasState } from '../src/state/CanvasState';
import { GridRenderer } from '../src/canvas/GridRenderer';
import { MapEditSession } from '../src/mapedit';

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
}

function makeSocketHolder(): { socket: FakeSocket; createSocket: (url: string) => WebSocketLike } {
    let socket!: FakeSocket;
    return {
        get socket() {
            return socket;
        },
        createSocket: () => {
            socket = new FakeSocket();
            return socket;
        },
    };
}

function ack(socket: FakeSocket, seq: number, key: string): void {
    socket.onmessage?.(new MessageEvent('message', { data: `["map_ack",[${seq},"${key}"],{}]` }));
}

const GLYPH = { char: 'X', fg: [0, 0, 0], bg: [-1, -1, -1] };

describe('MapEditSession violet-rect diff scope', () => {
    it('only sends cells inside the violet rect', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const canvas = new CanvasState(4, 2, false);
        const session = new MapEditSession('K0', canvas, {
            originX: 0,
            originY: 0,
            serverBounds: { col: 1, row: 0, w: 2, h: 1 },
        }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        // (1,0) is inside the rect (world 1,1); (3,0) is outside it.
        canvas.setCell(1, 0, { ...GLYPH });
        canvas.setCell(3, 0, { ...GLYPH });
        session.scheduleSync();
        vi.advanceTimersByTime(200);

        expect(holder.socket.sent.length).toBe(2);
        expect(holder.socket.sent[1]).toBe('["map_edit",["K1",1,[[1,1,"X",[0,0,0],[0,0,0],[]]]],{}]');
        session.dispose();
        vi.useRealTimers();
    });

    it('emits orphaned baseline cells as deletions after the violet rect shrinks', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const canvas = new CanvasState(2, 1, false);
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        canvas.setCell(1, 0, { ...GLYPH });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        expect(holder.socket.sent[1]).toBe('["map_edit",["K1",1,[[1,0,"X",[0,0,0],[0,0,0],[]]]],{}]');
        ack(holder.socket, 1, 'K2');

        // Shrink the rect to col 0 only, then touch col 0: the world (1,0)
        // key is unreachable by the rect loop, so the sweep deletes it.
        session.setServerBounds({ col: 0, row: 0, w: 1, h: 1 });
        canvas.setCell(0, 0, { ...GLYPH });
        session.scheduleSync();
        vi.advanceTimersByTime(200);

        const ops = JSON.parse(holder.socket.sent[2])[1][2] as Array<Array<unknown>>;
        expect(ops).toContainEqual([0, 0, 'X', [0, 0, 0], [0, 0, 0], []]);
        expect(ops).toContainEqual([1, 0, '', [204, 204, 204], [0, 0, 0], []]);
        session.dispose();
        vi.useRealTimers();
    });

    it('treats a pipe glyph as content for the outside sweep', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const canvas = new CanvasState(2, 1, false);
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        canvas.setCell(1, 0, { char: '|', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        ack(holder.socket, 1, 'K2');

        session.setServerBounds({ col: 0, row: 0, w: 1, h: 1 });
        session.scheduleSync();
        vi.advanceTimersByTime(200);

        const ops = JSON.parse(holder.socket.sent[2])[1][2] as Array<Array<unknown>>;
        expect(ops).toContainEqual([1, 0, '', [204, 204, 204], [0, 0, 0], []]);
        session.dispose();
        vi.useRealTimers();
    });

    it('left viewport growth plus rebaseOrigin produces no spurious diff', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const canvas = new CanvasState(4, 2, false);
        canvas.setCell(0, 0, { ...GLYPH });
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        const growth = canvas.ensureViewportFor(-2, 0);
        expect(growth.addedLeft).toBeGreaterThan(0);
        session.rebaseOrigin(-growth.addedLeft, 0);
        // Production onViewportShifted hook: bounds follow the canvas.
        session.setServerBounds({ ...canvas.serverBounds });
        session.scheduleSync();
        vi.advanceTimersByTime(200);

        // World coords are stable, so only the handshake went out.
        expect(holder.socket.sent.length).toBe(1);
        session.dispose();
        vi.useRealTimers();
    });

    it('bottom viewport growth plus rebaseOrigin produces no spurious diff', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const canvas = new CanvasState(4, 2, false);
        canvas.setCell(0, 0, { ...GLYPH });
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        const growth = canvas.ensureViewportFor(0, canvas.height);
        expect(growth.addedBottom).toBeGreaterThan(0);
        session.rebaseOrigin(0, -growth.addedBottom);
        session.setServerBounds({ ...canvas.serverBounds });
        session.scheduleSync();
        vi.advanceTimersByTime(200);

        expect(holder.socket.sent.length).toBe(1);
        session.dispose();
        vi.useRealTimers();
    });
});

function makeProbedRenderer(state: CanvasState, devicePixelRatio?: number) {
    const record = {
        rects: [] as Array<[number, number, number, number]>,
        strokes: 0,
        lines: [] as number[][],
        transforms: [] as number[][],
    };
    if ((globalThis as Record<string, unknown>).Path2D === undefined) {
        (globalThis as Record<string, unknown>).Path2D = class { rect() {} };
    }
    const ctx = new Proxy({}, {
        get: (_t, prop: string | symbol) => {
            if (prop === 'canvas') return undefined;
            return (...args: unknown[]) => {
                if (prop === 'strokeRect') {
                    record.rects.push(args as unknown as [number, number, number, number]);
                }
                if (prop === 'moveTo' || prop === 'lineTo') {
                    record.lines.push(args as unknown as number[]);
                }
                if (prop === 'setTransform') {
                    record.transforms.push(args as unknown as number[]);
                }
                if (prop === 'stroke') record.strokes += 1;
            };
        },
        set: () => true,
    }) as unknown as CanvasRenderingContext2D;
    const canvas = {
        width: 0,
        height: 0,
        style: {},
        getContext: () => ctx,
    } as unknown as HTMLCanvasElement;
    const renderer = new GridRenderer(canvas, state, { width: 8, height: 16, font: '16px monospace', advance: 8 }, devicePixelRatio);
    return { renderer, record, canvas };
}

describe('GridRenderer violet server-grid outline', () => {
    it('picks the outline up from the state on construct', () => {
        const state = new CanvasState(10, 8, false);
        state.setServerBounds({ col: 1, row: 2, w: 4, h: 3 });
        const { record } = makeProbedRenderer(state);
        // 8px cols, 16px rows: rect at (8, 32) size 32x48.
        expect(record.rects).toContainEqual([8, 32, 32, 48]);
    });

    it('setServerBounds copies the rect and null hides the outline', () => {
        const state = new CanvasState(10, 8, false);
        const { renderer, record } = makeProbedRenderer(state);
        const before = record.rects.length;
        // Default full-canvas bounds render one rect per render pass.
        expect(before).toBeGreaterThan(0);

        const rect = { col: 2, row: 1, w: 3, h: 3 };
        renderer.setServerBounds(rect);
        rect.col = 99;
        expect(record.rects).toContainEqual([16, 16, 24, 48]);

        renderer.setServerBounds(null);
        const hiddenCount = record.rects.length;
        state.notify();
        expect(record.rects.length).toBe(hiddenCount);
    });

    it('updateState refreshes the outline from the new state', () => {
        const first = new CanvasState(10, 8, false);
        first.setServerBounds({ col: 0, row: 0, w: 2, h: 2 });
        const { renderer, record } = makeProbedRenderer(first);
        expect(record.rects).toContainEqual([0, 0, 16, 32]);

        const second = new CanvasState(10, 8, false);
        second.setServerBounds({ col: 5, row: 5, w: 2, h: 2 });
        renderer.updateState(second);
        expect(record.rects).toContainEqual([40, 80, 16, 32]);
    });
});

describe('GridRenderer grid-line crispness', () => {
    it('draws grid lines on half-pixel boundaries across the drawable surface', () => {
        const state = new CanvasState(3, 2, false);
        const { record } = makeProbedRenderer(state);
        // 8px cols: verticals at 8.5/16.5 spanning the 32px height.
        expect(record.lines).toContainEqual([8.5, 0]);
        expect(record.lines).toContainEqual([8.5, 32]);
        expect(record.lines).toContainEqual([16.5, 0]);
        // 16px rows: horizontal at 16.5 spanning the 24px width.
        expect(record.lines).toContainEqual([0, 16.5]);
        expect(record.lines).toContainEqual([24, 16.5]);
        // No grid endpoint sits on a full pixel: integer coords would
        // straddle two pixels and smear into a dimmer, wider line.
        for (const [x, y] of record.lines) {
            expect(Number.isInteger(x) && Number.isInteger(y)).toBe(false);
        }
    });
});

describe('GridRenderer device pixel ratio', () => {
    it('scales the backing store but keeps CSS size and CSS-px drawing', () => {
        const state = new CanvasState(3, 2, false);
        const { record, canvas } = makeProbedRenderer(state, 2);
        // Backing store doubled; element stays CSS px.
        expect(canvas.width).toBe(48);
        expect(canvas.height).toBe(64);
        expect(canvas.style.width).toBe('24px');
        expect(canvas.style.height).toBe('32px');
        // Drawing runs under a 2x transform in CSS-px coords.
        expect(record.transforms).toContainEqual([2, 0, 0, 2, 0, 0]);
        // Grid endpoints are still CSS px (8.5/16.5), not device px.
        expect(record.lines).toContainEqual([8.5, 0]);
        expect(record.lines).toContainEqual([0, 16.5]);
    });

    it('defaults to a 1x backing store matching the CSS size', () => {
        const state = new CanvasState(3, 2, false);
        const { record, canvas } = makeProbedRenderer(state);
        expect(canvas.width).toBe(24);
        expect(canvas.height).toBe(32);
        expect(record.transforms).toContainEqual([1, 0, 0, 1, 0, 0]);
    });
});
