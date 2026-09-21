// @ts-nocheck
import { describe, expect, it, vi } from 'vitest';
import { WebSocketLike } from '../src/webclient/connection';
import { CanvasState } from '../src/state/CanvasState';
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

function parseFrame(frame: string): [string, unknown[], Record<string, unknown>] {
    return JSON.parse(frame);
}

describe('MapEditSession.rebindCanvas', () => {
    it('sends live-canvas edits after a swap, not the discarded canvas', () => {
        const holder = makeSocketHolder();
        const stale = new CanvasState(2, 1, false);
        const session = new MapEditSession('K0', stale, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        const live = new CanvasState(2, 1, false);
        session.rebindCanvas(live);

        live.setCell(0, 0, { char: 'X', fg: [0, 0, 0], bg: [-1, -1, -1] });
        stale.setCell(0, 0, { char: 'O', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.saveToServer();

        expect(holder.socket.sent.length).toBe(2);
        const [, args] = parseFrame(holder.socket.sent[1]);
        expect(args[2]).toEqual([[0, 0, 'X', [0, 0, 0], [0, 0, 0], []]]);
        expect(holder.socket.sent[1]).not.toContain('"O"');
        session.dispose();
    });

    it('rebaselines by default so an identical swap sends nothing', () => {
        const holder = makeSocketHolder();
        const events: string[] = [];
        const canvas = new CanvasState(1, 1, false);
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        session.onEvent((e) => events.push(e.type));
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        canvas.setCell(0, 0, { char: 'X', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.saveToServer();
        ack(holder.socket, 1, 'K2');

        session.rebindCanvas(canvas.clone());
        session.saveToServer();

        expect(holder.socket.sent.length).toBe(2);
        expect(events).toContain('error');
        session.dispose();
    });

    it('keeps the old baseline with keepBaseline so a New clear sends deletions', () => {
        const holder = makeSocketHolder();
        const canvas = new CanvasState(1, 1, false);
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        canvas.setCell(0, 0, { char: 'X', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.saveToServer();
        ack(holder.socket, 1, 'K2');

        // beginNewCanvas equivalent: blank canvas, old baseline retained.
        session.rebindCanvas(new CanvasState(1, 1, false), { keepBaseline: true });
        session.saveToServer();

        expect(holder.socket.sent.length).toBe(3);
        const [, args] = parseFrame(holder.socket.sent[2]);
        // The wipe is expressed as a deletion of the previously saved cell.
        expect(args[2]).toEqual([[0, 0, '', [204, 204, 204], [0, 0, 0], []]]);
        session.dispose();
    });
});

describe('MapEditSession flush send failure', () => {
    it('requeues the batch and clears inFlight when send returns false', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const canvas = new CanvasState(1, 1, false);
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        canvas.setCell(0, 0, { char: 'X', fg: [0, 0, 0], bg: [-1, -1, -1] });
        // Socket drops between the state check and the send (no close event).
        holder.socket.readyState = 0;
        session.saveToServer();

        expect(holder.socket.sent.length).toBe(1);
        expect(session.inFlight).toBeNull();
        expect(session.queue.length).toBe(1);

        // Recovery: socket back, a later flush sends the requeued batch first.
        holder.socket.readyState = 1;
        canvas.setCell(0, 0, { char: 'Y', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);

        expect(holder.socket.sent.length).toBe(2);
        expect(holder.socket.sent[1]).toBe('["map_edit",["K1",2,[[0,0,"X",[0,0,0],[0,0,0],[]]]],{}]');
        ack(holder.socket, 2, 'K2');
        expect(holder.socket.sent.length).toBe(3);
        expect(holder.socket.sent[2]).toBe('["map_edit",["K2",3,[[0,0,"Y",[0,0,0],[0,0,0],[]]]],{}]');
        session.dispose();
        vi.useRealTimers();
    });

    it('requeues instead of dropping when a reconnect resend fails', () => {
        const holder = makeSocketHolder();
        const canvas = new CanvasState(1, 1, false);
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        canvas.setCell(0, 0, { char: 'X', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.saveToServer();
        expect(holder.socket.sent.length).toBe(2);
        expect(session.inFlight).not.toBeNull();

        // Reconnect fires while the socket still cannot send: the in-flight
        // batch must be requeued, not dropped, and inFlight released.
        holder.socket.readyState = 0;
        session.handleStateChange('open');
        expect(holder.socket.sent.length).toBe(2);
        expect(session.inFlight).toBeNull();
        expect(session.queue.length).toBe(1);

        // A later open flushes the requeued batch.
        holder.socket.readyState = 1;
        session.handleStateChange('open');
        expect(holder.socket.sent.length).toBe(3);
        expect(holder.socket.sent[2]).toBe('["map_edit",["K1",2,[[0,0,"X",[0,0,0],[0,0,0],[]]]],{}]');
        session.dispose();
    });
});

describe('MapEditSession chained room moves', () => {
    function makeRoomSession() {
        const holder = makeSocketHolder();
        const canvas = new CanvasState(2, 1, false);
        const origin = {
            originX: 0,
            originY: 0,
            roomCells: new Set(['3,3']),
            rooms: [{ x: 3, y: 3, desc: 'Hall', exits: [] }],
        };
        const session = new MapEditSession('K0', canvas, origin, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        return { holder, session };
    }

    it('folds A->B,B->C in one batch to A->C and consumes the hop', () => {
        const { holder, session } = makeRoomSession();
        session.validateRoomMoves([
            { fromX: 3, fromY: 3, toX: 4, toY: 3 },
            { fromX: 4, fromY: 3, toX: 5, toY: 3 },
        ]);
        expect(holder.socket.sent[1]).toBe(
            '["map_validate_moves",["K1",1,[[3,3,4,3],[3,3,5,3]],[]],{}]'
        );
        // The intermediate hop is consumed: save sends a single room op.
        expect(session.pendingMoves).toEqual([{ fromX: 3, fromY: 3, toX: 5, toY: 3 }]);
        session.dispose();
    });

    it('unfolds multi-hop priors back to the original coord', () => {
        const { holder, session } = makeRoomSession();
        session.pendingMoves = [
            { fromX: 1, fromY: 3, toX: 2, toY: 3 },
            { fromX: 2, fromY: 3, toX: 3, toY: 3 },
        ];
        session.validateRoomMoves([{ fromX: 3, fromY: 3, toX: 4, toY: 3 }]);
        expect(holder.socket.sent[1]).toBe('["map_validate_moves",["K1",1,[[1,3,4,3]],[[1,3,2,3],[2,3,3,3]]],{}]');
        expect(session.pendingMoves).toEqual([{ fromX: 1, fromY: 3, toX: 4, toY: 3 }]);
        session.dispose();
    });

    it('drops stale duplicates when an ack lands on an occupied dest', () => {
        const { holder, session } = makeRoomSession();
        session.validateRoomMoves([{ fromX: 3, fromY: 3, toX: 4, toY: 3 }]);
        // A stale entry already points at the destination.
        session.roomPositions.set('9,9', '4,3');
        holder.socket.onmessage?.(new MessageEvent('message', { data: '["moves_ok",[1,"K2"],{}]' }));
        expect(Array.from(session.roomPositions.entries())).toEqual([['3,3', '4,3']]);
        session.dispose();
    });
});
