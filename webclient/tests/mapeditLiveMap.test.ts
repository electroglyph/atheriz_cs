// @ts-nocheck
// Live-map simulation: a dense 18-room map at negative coords (mirroring
// a real InitialSetup-style map, not a 2-room toy) driven through session
// scripts the way a user drives the editor — whole-map shifts, saves,
// undo-during-flight, queued-then-denied batches. Catches wire shapes
// that only emerge with history, not with single drags.
import { describe, expect, it, vi } from 'vitest';
import { WebSocketLike } from '../src/webclient/connection';
import { CanvasState } from '../src/state/CanvasState';
import { loadMapPayload, MapEditSession, MapEditPayload } from '../src/mapedit';

class FakeSocket implements WebSocketLike {
    readyState = 0;
    onopen: ((event: Event) => void) | null = null;
    onclose: ((event: CloseEvent) => void) | null = null;
    onerror: ((event: Event) => void) | null = null;
    onmessage: ((event: MessageEvent) => void) | null = null;
    sent: string[] = [];
    send(data: string): void { this.sent.push(data); }
    close(): void {}
    open(): void { this.readyState = 1; this.onopen?.(new Event('open')); }
}

function makeHolder() {
    let socket!: FakeSocket;
    return {
        get socket() { return socket; },
        createSocket: () => { socket = new FakeSocket(); return socket; },
    };
}

function ack(socket: FakeSocket, seq: number, key: string): void {
    socket.onmessage?.(new MessageEvent('message', { data: `["map_ack",[${seq},"${key}"],{}]` }));
}

function feed(socket: FakeSocket, wire: string): void {
    socket.onmessage?.(new MessageEvent('message', { data: wire }));
}

/** 18 rooms at x -2..3, y -10..-8 through the real load path. */
function makeLiveMap(): { canvas: CanvasState; origin: ReturnType<typeof loadMapPayload> } {
    const canvas = new CanvasState(4, 4, false);
    const grid: [number, number, string][] = [];
    const rooms: { x: number; y: number; desc: string; exits: never[] }[] = [];
    for (let x = -2; x <= 3; x++) {
        for (let y = -10; y <= -8; y++) {
            grid.push([x, y, 'R']);
            rooms.push({ x, y, desc: `R${x},${y}`, exits: [] });
        }
    }
    const payload: MapEditPayload = { area: 'limbo', z: 4, grid, rooms };
    const origin = loadMapPayload(canvas, payload);
    return { canvas, origin };
}

function shiftAll(origin: { rooms: { x: number; y: number }[] }, dx: number, dy: number) {
    return origin.rooms.map((r) => ({ fromX: r.x, fromY: r.y, toX: r.x + dx, toY: r.y + dy }));
}

describe('live map room moves', () => {
    it('seeds 18 rooms at negative coords with unknown origin', () => {
        const { canvas, origin } = makeLiveMap();
        expect(origin.rooms.length).toBe(18);
        expect(origin.originX).toBe(-2);
        expect(origin.originY).toBe(-10);
        expect(canvas.width).toBe(12);
        expect(canvas.height).toBe(6);
    });

    it('whole-map shift twice, then save, stays consistent', () => {
        const { canvas, origin } = makeLiveMap();
        const holder = makeHolder();
        const events: string[] = [];
        const session = new MapEditSession('K0', canvas, origin, holder.createSocket);
        session.onEvent((e) => events.push(e.type));
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        // Drag 1: shift everything by (+1,+1). No pendings: empty context.
        const batch1 = shiftAll(origin, 1, 1);
        session.validateRoomMoves(batch1);
        const sent1 = JSON.parse(holder.socket.sent[1]);
        expect(sent1[1][3]).toEqual([]);
        console.log('PENDINGS_AFTER_BATCH1:', (session as unknown as { pendingMoves: unknown[] }).pendingMoves.length);
        feed(holder.socket, '["moves_ok",[1,"K2"],{}]');
        feed(holder.socket, '["moves_ok",[1,"K2"],{}]');

        // Drag 2: shift everything again. All hops fold: empty context,
        // sources unfolded to the original coords.
        const moved = { rooms: origin.rooms.map((r) => ({ x: r.x + 1, y: r.y + 1 })) };
        const batch2 = shiftAll(moved, 1, 1);
        session.validateRoomMoves(batch2);
        const sent2 = JSON.parse(holder.socket.sent[2]);
        expect(sent2[1][3]).toEqual([]);
        expect(sent2[1][2].length).toBe(18);
        // Every source is an original coord (unfolded through the pending).
        for (const [fx, fy] of sent2[1][2].map((m: unknown[]) => [m[0], m[1]])) {
            expect(origin.rooms.some((r) => r.x === fx && r.y === fy)).toBe(true);
        }
        feed(holder.socket, '["moves_ok",[2,"K3"],{}]');

        // Save: one folded op per room, original -> final.
        session.saveToServer();
        const saveMsg = JSON.parse(holder.socket.sent[holder.socket.sent.length - 1]);
        const ops = saveMsg[1][2].filter((op: unknown[]) => op[0] === 'room');
        expect(ops.length).toBe(18);
        feed(holder.socket, `["map_ack",[3,"K4"],{}]`);
        expect(events).toContain('saved');
        session.dispose();
    });

    it('observes undo-during-flight and queued-cascade wires', () => {
        const { canvas, origin } = makeLiveMap();
        const holder = makeHolder();
        const events: string[] = [];
        const session = new MapEditSession('K0', canvas, origin, holder.createSocket);
        session.onEvent((e) => events.push(e.type));
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        // Batch 1: shift all, left in flight (no ack).
        session.validateRoomMoves(shiftAll(origin, 1, 1));
        expect(holder.socket.sent.length).toBe(2);

        // Batch 2 chained while batch 1 is in flight: queued, nothing sent.
        const moved = { rooms: origin.rooms.map((r) => ({ x: r.x + 1, y: r.y + 1 })) };
        const batch2 = shiftAll(moved, 1, 1);
        session.validateRoomMoves(batch2);
        expect(holder.socket.sent.length).toBe(2);

        // Deny batch 1 wholesale: batch 2 (built on its optimistic canvas)
        // is stale and dropped, never sent. No cascade of doomed retries.
        feed(holder.socket, `["moves_denied",[1,"K2",[${Array.from({ length: 18 }, (_, i) => i).join(',')}]],{}]`);
        expect(holder.socket.sent.length).toBe(2);
        expect(events.filter((e) => e === 'moves_dropped').length).toBe(1);
        expect(events.filter((e) => e === 'moves_denied').length).toBe(1);

        // Nothing pending anymore: a save has nothing to send ...
        session.saveToServer();
        expect(events.some((e) => e === 'error')).toBe(true);
        // ... and a fresh re-drag from the restored canvas validates clean.
        session.validateRoomMoves(shiftAll(origin, 5, 5));
        const retry = JSON.parse(holder.socket.sent[holder.socket.sent.length - 1]);
        expect(retry[0]).toBe('map_validate_moves');
        expect(retry[1][2].length).toBe(18);
        expect(retry[1][3]).toEqual([]);
        session.dispose();
    });
});
