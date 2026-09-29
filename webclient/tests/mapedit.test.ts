// @ts-nocheck
import { describe, expect, it, vi } from 'vitest';
import { WebSocketLike } from '../src/webclient/connection';
import { CanvasState } from '../src/state/CanvasState';
import { loadMapPayload, logRoomData, MapEditSession, MapEditPayload, formatExitCoord, parseExitCoord } from '../src/mapedit';

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

function makeCanvas(): CanvasState {
    return new CanvasState(2, 1, false);
}

function ack(socket: FakeSocket, seq: number, key: string): void {
    socket.onmessage?.(new MessageEvent('message', { data: `["map_ack",[${seq},"${key}"],{}]` }));
}

describe('loadMapPayload', () => {
    it('sizes the canvas to twice the grid bounds and places cells with 0,0 at the lower left', () => {
        const canvas = makeCanvas();
        const payload: MapEditPayload = {
            area: 'TestArea',
            z: 0,
            grid: [[-2, 3, 'X'], [5, 3, 'Y']],
        };
        const origin = loadMapPayload(canvas, payload);
        expect(origin.originX).toBe(-2);
        expect(origin.originY).toBe(3);
        expect(origin.roomCells).toEqual(new Set());
        expect(canvas.width).toBe(16);
        expect(canvas.height).toBe(2);
        expect(canvas.getCompositeCell(0, 1)?.char).toBe('X');
        expect(canvas.getCompositeCell(7, 1)?.char).toBe('Y');
    });

    it('handles negative coordinates with the lower-left corner as the origin', () => {
        const canvas = makeCanvas();
        const payload: MapEditPayload = {
            area: 'TestArea',
            z: 0,
            grid: [[-2, -2, 'X']],
        };
        const origin = loadMapPayload(canvas, payload);
        expect(origin.originX).toBe(-2);
        expect(origin.originY).toBe(-2);
        expect(canvas.width).toBe(2);
        expect(canvas.height).toBe(2);
        expect(canvas.getCompositeCell(0, 1)?.char).toBe('X');
    });

    it('marks room cells in roomCells using the flipped canvas rows', () => {
        const canvas = makeCanvas();
        const payload: MapEditPayload = {
            area: 'TestArea',
            z: 0,
            grid: [[0, 0, '℣'], [1, 0, '℣']],
            rooms: [
                { x: 0, y: 0, desc: 'Hall', exits: [] },
                { x: 1, y: 0, desc: 'Kitchen', exits: [{ name: 'West', aliases: ['w'], coord: ['TestArea', 0, 0, 0] }] },
            ],
        };
        const origin = loadMapPayload(canvas, payload);
        expect(origin.roomCells).toEqual(new Set(['0,1', '1,1']));
    });

    it('handles an empty grid', () => {
        const canvas = makeCanvas();
        const payload: MapEditPayload = { area: 'TestArea', z: 0, grid: [] };
        const origin = loadMapPayload(canvas, payload);
        expect(origin).toEqual({ originX: 0, originY: 0, roomCells: new Set(), rooms: [] });
        expect(canvas.width).toBe(1);
        expect(canvas.height).toBe(1);
    });

    it('parses ANSI-wrapped symbols into colored cells', () => {
        const canvas = makeCanvas();
        const payload: MapEditPayload = {
            area: 'TestArea',
            z: 0,
            grid: [[0, 0, '\x1b[48;2;0;0;0m\x1b[38;2;255;0;0mX\x1b[0m']],
        };
        loadMapPayload(canvas, payload);
        const cell = canvas.getCompositeCell(0, 1);
        expect(cell?.char).toBe('X');
        expect(cell?.fg).toEqual([255, 0, 0]);
    });
});

describe('logRoomData', () => {
    it('logs room descriptions and exits with destination coords', () => {
        const spy = vi.spyOn(console, 'log').mockImplementation(() => {});
        logRoomData({
            area: 'TestArea',
            z: 2,
            grid: [],
            rooms: [
                { x: 3, y: 4, desc: 'A hall.', exits: [{ name: 'North', aliases: ['n'], coord: ['TestArea', 3, 5, 2] }] },
            ],
        });
        expect(spy).toHaveBeenCalledWith('Room data for TestArea (z=2):');
        expect(spy).toHaveBeenCalledWith('(3, 4): A hall. | exits: North -> 3,5 (TestArea, z=2)');
        spy.mockRestore();
    });

    it('logs a placeholder when there are no rooms', () => {
        const spy = vi.spyOn(console, 'log').mockImplementation(() => {});
        logRoomData({ area: 'TestArea', z: 0, grid: [] });
        expect(spy).toHaveBeenCalledWith('No rooms found.');
        spy.mockRestore();
    });

    it('includes the room name when the payload carries one', () => {
        const spy = vi.spyOn(console, 'log').mockImplementation(() => {});
        logRoomData({
            area: 'TestArea',
            z: 0,
            grid: [],
            rooms: [
                { x: 0, y: 0, name: 'Hall', desc: 'A hall.', exits: [] },
            ],
        });
        expect(spy).toHaveBeenCalledWith('(0, 0) Hall: A hall. | exits: none');
        spy.mockRestore();
    });
});

describe('MapEditSession', () => {
    it('sends the handshake when the socket opens', () => {
        const holder = makeSocketHolder();
        const session = new MapEditSession('K0', makeCanvas(), { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        expect(holder.socket.sent).toEqual(['["map_edit",["K0",0,[]],{}]']);
        session.dispose();
    });

    it('preserves cell colors when only the character is edited', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const canvas = new CanvasState(1, 1, false);
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        canvas.setCell(0, 0, { char: 'X', fg: [255, 0, 0], bg: [0, 0, 255], bold: true });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        expect(holder.socket.sent[1]).toBe('["map_edit",["K1",1,[[0,0,"X",[255,0,0],[0,0,255],["bold"]]]],{}]');
        session.dispose();
        vi.useRealTimers();
    });

    it('emits a diff when only the color changes', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const canvas = new CanvasState(1, 1, false);
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        canvas.setCell(0, 0, { char: 'X', fg: [255, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        ack(holder.socket, 1, 'K2');
        canvas.setCell(0, 0, { char: 'X', fg: [0, 255, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        expect(holder.socket.sent[2]).toBe('["map_edit",["K2",2,[[0,0,"X",[0,255,0],[0,0,0],[]]]],{}]');
        session.dispose();
        vi.useRealTimers();
    });

    it('sends edits with the rotated key and advances seq on ack', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const canvas = makeCanvas();
        const session = new MapEditSession('K0', canvas, { originX: 10, originY: -5 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        canvas.setCell(0, 0, { char: 'X', fg: [0, 0, 0], bg: [-1, -1, -1] });
        canvas.setCell(1, 0, { char: 'Y', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);

        expect(holder.socket.sent[1]).toBe('["map_edit",["K1",1,[[10,-5,"X",[0,0,0],[0,0,0],[]],[11,-5,"Y",[0,0,0],[0,0,0],[]]]],{}]');
        ack(holder.socket, 1, 'K2');

        canvas.setCell(0, 0, { char: 'Z', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        expect(holder.socket.sent[2]).toBe('["map_edit",["K2",2,[[10,-5,"Z",[0,0,0],[0,0,0],[]]]],{}]');
        session.dispose();
        vi.useRealTimers();
    });

    it('queues edits while one is in flight', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const canvas = makeCanvas();
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        canvas.setCell(0, 0, { char: 'A', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        canvas.setCell(1, 0, { char: 'B', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        expect(holder.socket.sent.length).toBe(2);

        ack(holder.socket, 1, 'K2');
        expect(holder.socket.sent.length).toBe(3);
        expect(holder.socket.sent[2]).toBe('["map_edit",["K2",2,[[1,0,"B",[0,0,0],[0,0,0],[]]]],{}]');
        session.dispose();
        vi.useRealTimers();
    });

    it('resends the in-flight edit with the same key and seq after reconnect', () => {
        vi.useFakeTimers();
        vi.spyOn(Math, 'random').mockReturnValue(0);
        const sockets: FakeSocket[] = [];
        const canvas = makeCanvas();
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, (url) => {
            const socket = new FakeSocket();
            sockets.push(socket);
            return socket;
        });
        sockets[0].open();
        ack(sockets[0], 0, 'K1');
        canvas.setCell(0, 0, { char: 'A', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        expect(sockets[0].sent.length).toBe(2);

        sockets[0].drop();
        vi.advanceTimersByTime(600);
        expect(sockets.length).toBe(2);
        sockets[1].open();
        expect(sockets[1].sent).toEqual(['["map_edit",["K1",1,[[0,0,"A",[0,0,0],[0,0,0],[]]]],{}]']);

        ack(sockets[1], 1, 'K2');
        canvas.setCell(0, 0, { char: 'Z', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        expect(sockets[1].sent[1]).toBe('["map_edit",["K2",2,[[0,0,"Z",[0,0,0],[0,0,0],[]]]],{}]');
        session.dispose();
        vi.useRealTimers();
    });

    it('emits an error and stops when reconnect attempts are exhausted', () => {
        vi.useFakeTimers();
        vi.spyOn(Math, 'random').mockReturnValue(0);
        const sockets: FakeSocket[] = [];
        const events: string[] = [];
        const canvas = makeCanvas();
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, (url) => {
            const socket = new FakeSocket();
            sockets.push(socket);
            return socket;
        });
        session.onEvent((event) => events.push(event.type === 'error' ? `error:${event.message}` : event.type));
        sockets[0].open();
        ack(sockets[0], 0, 'K1');

        sockets[0].drop();
        vi.advanceTimersByTime(500);
        sockets[1].drop();
        vi.advanceTimersByTime(1000);
        sockets[2].drop();
        vi.advanceTimersByTime(2000);
        sockets[3].drop();

        expect(events).toEqual(['synced', 'error:Connection failed.']);

        canvas.setCell(0, 0, { char: 'X', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        expect(sockets[3].sent.length).toBe(0);
        session.dispose();
        vi.useRealTimers();
    });

    it('dispose closes the connection and stops all syncs', () => {
        const holder = makeSocketHolder();
        const canvas = makeCanvas();
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        session.dispose();

        expect(holder.socket.closeCalls).toBe(1);
        canvas.setCell(0, 0, { char: 'X', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        expect(holder.socket.sent).toEqual(['["map_edit",["K0",0,[]],{}]']);
        session.dispose();
    });

    it('emits a reject event and stops on rejection', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const events: string[] = [];
        const session = new MapEditSession('K0', makeCanvas(), { originX: 0, originY: 0 }, holder.createSocket);
        session.onEvent((event) => events.push(event.type === 'reject' ? `reject:${event.reason}` : event.type));
        holder.socket.open();
        holder.socket.onmessage?.(new MessageEvent('message', { data: '["map_edit_reject",["replay"],{}]' }));
        expect(events).toEqual(['reject:replay']);
        expect(holder.socket.closeCalls).toBe(1);
        session.dispose();
        vi.useRealTimers();
    });

    it('ignores acks that do not match the in-flight seq', () => {
        const holder = makeSocketHolder();
        const session = new MapEditSession('K0', makeCanvas(), { originX: 0, originY: 0 }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 5, 'K99');
        expect(holder.socket.sent).toEqual(['["map_edit",["K0",0,[]],{}]']);
        session.dispose();
    });
});

describe('MapEditSession room saves', () => {
    function makeRoomSession() {
        const holder = makeSocketHolder();
        const events: string[] = [];
        const session = new MapEditSession('K0', makeCanvas(), { originX: 0, originY: 0 }, holder.createSocket);
        session.onEvent((event) => events.push(
            event.type === 'room_denied' ? `room_denied:${event.reason}` : event.type === 'error' ? `error:${event.message}` : event.type
        ));
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        return { holder, events, session };
    }

    it('sends a room save with the rotated key and emits room_saved on map_ack', () => {
        const { holder, events, session } = makeRoomSession();
        session.saveRoom(2, 3, 'Hall', 'A dusty hall.');
        expect(holder.socket.sent[1]).toBe(
            '["map_edit_room",["K1",1,{"x":2,"y":3,"name":"Hall","desc":"A dusty hall."}],{}]'
        );
        ack(holder.socket, 1, 'K2');
        expect(events).toEqual(['synced', 'room_saved', 'synced']);
        session.saveRoom(2, 3, 'Hall', 'Updated.');
        expect(holder.socket.sent[2]).toBe(
            '["map_edit_room",["K2",2,{"x":2,"y":3,"name":"Hall","desc":"Updated."}],{}]'
        );
        session.dispose();
    });

    it('emits room_saved on room_ok', () => {
        const { holder, events, session } = makeRoomSession();
        session.saveRoom(2, 3, 'Hall', 'A dusty hall.');
        holder.socket.onmessage?.(new MessageEvent('message', { data: '["room_ok",[1,"K2"],{}]' }));
        expect(events).toEqual(['synced', 'room_saved', 'synced']);
        session.saveRoom(2, 3, 'Hall', 'Again.');
        expect(holder.socket.sent[2]).toBe(
            '["map_edit_room",["K2",2,{"x":2,"y":3,"name":"Hall","desc":"Again."}],{}]'
        );
        session.dispose();
    });

    it('emits room_denied, rotates the key, and keeps the session alive', () => {
        const { holder, events, session } = makeRoomSession();
        session.saveRoom(9, 9, 'Nowhere', 'No room.');
        holder.socket.onmessage?.(
            new MessageEvent('message', { data: '["room_denied",[1,"K2","No room at (9, 9)."],{}]' })
        );
        expect(events).toEqual(['synced', 'room_denied:No room at (9, 9).']);
        session.saveRoom(2, 3, 'Hall', 'Recovered.');
        expect(holder.socket.sent[2]).toBe(
            '["map_edit_room",["K2",2,{"x":2,"y":3,"name":"Hall","desc":"Recovered."}],{}]'
        );
        session.dispose();
    });

    it('rejects non-integer coords without sending', () => {
        const { holder, events, session } = makeRoomSession();
        session.saveRoom(1.5, 3, 'Hall', 'A dusty hall.');
        expect(events).toEqual(['synced', 'error:Invalid room coordinates.']);
        expect(holder.socket.sent).toEqual(['["map_edit",["K0",0,[]],{}]']);
        session.dispose();
    });

    it('queues a room save behind an in-flight edit and rotates keys in order', () => {
        vi.useFakeTimers();
        const holder = makeSocketHolder();
        const events: string[] = [];
        const canvas = makeCanvas();
        const session = new MapEditSession('K0', canvas, { originX: 0, originY: 0 }, holder.createSocket);
        session.onEvent((event) => events.push(event.type));
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        canvas.setCell(0, 0, { char: 'X', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        session.saveRoom(2, 3, 'Hall', 'A dusty hall.');
        expect(holder.socket.sent.length).toBe(2);
        ack(holder.socket, 1, 'K2');
        expect(holder.socket.sent[2]).toBe(
            '["map_edit_room",["K2",2,{"x":2,"y":3,"name":"Hall","desc":"A dusty hall."}],{}]'
        );
        ack(holder.socket, 2, 'K3');
        expect(events).toEqual(['synced', 'synced', 'room_saved', 'synced']);
        session.dispose();
        vi.useRealTimers();
    });
});

describe('exit coord text', () => {
    it('round-trips through format and parse', () => {
        expect(formatExitCoord('TestArea', 5, -3, 0)).toBe('(TestArea,5,-3,0)');
        expect(parseExitCoord('(TestArea,5,-3,0)')).toEqual({ area: 'TestArea', x: 5, y: -3, z: 0 });
        expect(parseExitCoord('  (TestArea,5,-3,0)  ')).toEqual({ area: 'TestArea', x: 5, y: -3, z: 0 });
    });

    it('rejects malformed coord text', () => {
        expect(parseExitCoord('TestArea 5,-3 (z=0)')).toBeNull();
        expect(parseExitCoord('(TestArea,5,-3)')).toBeNull();
        expect(parseExitCoord('(TestArea,5,-3,0,1)')).toBeNull();
        expect(parseExitCoord('(TestArea,five,-3,0)')).toBeNull();
        expect(parseExitCoord('')).toBeNull();
        expect(parseExitCoord('(Te,st,5,-3,0)')).toBeNull();
    });
});

describe('MapEditSession exit saves', () => {
    function makeExitsSession() {
        const holder = makeSocketHolder();
        const events: string[] = [];
        const session = new MapEditSession('K0', makeCanvas(), { originX: 0, originY: 0 }, holder.createSocket);
        session.onEvent((event) => events.push(
            event.type === 'exits_denied' ? `exits_denied:${event.x},${event.y}:${event.reason}`
                : event.type === 'exits_saved' ? `exits_saved:${event.x},${event.y}`
                : event.type === 'error' ? `error:${event.message}` : event.type
        ));
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        return { holder, events, session };
    }

    const exits = [{ name: 'east', aliases: ['e'], coord: ['TestArea', 5, 5, 0] as [string, number, number, number] }];

    it('sends a full exit replacement and emits exits_saved on map_ack', () => {
        const { holder, events, session } = makeExitsSession();
        session.setExits(2, 3, exits);
        expect(holder.socket.sent[1]).toBe(
            '["map_edit_exits",["K1",1,{"x":2,"y":3,"exits":[{"name":"east","aliases":["e"],"coord":["TestArea",5,5,0]}]}],{}]'
        );
        ack(holder.socket, 1, 'K2');
        expect(events).toEqual(['synced', 'exits_saved:2,3', 'synced']);
        session.dispose();
    });

    it('emits exits_saved on exits_ok', () => {
        const { holder, events, session } = makeExitsSession();
        session.setExits(2, 3, exits);
        holder.socket.onmessage?.(new MessageEvent('message', { data: '["exits_ok",[1,"K2"],{}]' }));
        expect(events).toEqual(['synced', 'exits_saved:2,3', 'synced']);
        session.dispose();
    });

    it('emits exits_denied with coords, rotates the key, and keeps the session alive', () => {
        const { holder, events, session } = makeExitsSession();
        session.setExits(2, 3, [{ name: 'doom', aliases: [], coord: ['TestArea', 9, 9, 0] }]);
        holder.socket.onmessage?.(
            new MessageEvent('message', { data: '["exits_denied",[1,"K2","No room at TestArea(9,9,0) for exit \'doom\'."],{}]' })
        );
        expect(events).toEqual(['synced', "exits_denied:2,3:No room at TestArea(9,9,0) for exit 'doom'."]);
        session.setExits(2, 3, exits);
        expect(holder.socket.sent[2]).toBe(
            '["map_edit_exits",["K2",2,{"x":2,"y":3,"exits":[{"name":"east","aliases":["e"],"coord":["TestArea",5,5,0]}]}],{}]'
        );
        session.dispose();
    });

    it('rejects non-integer coords without sending', () => {
        const { holder, events, session } = makeExitsSession();
        session.setExits(1.5, 3, exits);
        expect(events).toEqual(['synced', 'error:Invalid exits payload.']);
        expect(holder.socket.sent).toEqual(['["map_edit",["K0",0,[]],{}]']);
        session.dispose();
    });
});

describe('MapEditSession room moves', () => {
    interface MoveEvt { type: string; moves?: { fromX: number; fromY: number; toX: number; toY: number }[]; message?: string }

    function makeRoomSession(key = 'K0') {
        const holder = makeSocketHolder();
        const canvas = makeCanvas();
        const origin = {
            originX: 0,
            originY: 0,
            roomCells: new Set(['3,3']),
            rooms: [{ x: 3, y: 3, desc: 'Hall', exits: [] }],
        };
        const session = new MapEditSession(key, canvas, origin, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        return { holder, canvas, session };
    }

    function feed(socket: FakeSocket, wire: string): void {
        socket.onmessage?.(new MessageEvent('message', { data: wire }));
    }

    it('validates room moves and folds follow-up drags through pending moves', () => {
        const { holder, session } = makeRoomSession();
        session.validateRoomMoves([{ fromX: 3, fromY: 3, toX: 4, toY: 3 }]);
        expect(holder.socket.sent[1]).toBe('["map_validate_moves",["K1",1,[[3,3,4,3]],[]],{}]');

        feed(holder.socket, '["moves_ok",[1,"K2"],{}]');
        // the server has not received a save yet, so a second drag of the
        // same room must be expressed against its original coordinate
        session.validateRoomMoves([{ fromX: 4, fromY: 3, toX: 5, toY: 3 }]);
        expect(holder.socket.sent[2]).toBe('["map_validate_moves",["K2",2,[[3,3,5,3]],[[3,3,4,3]]],{}]');
        session.dispose();
    });

    it('sends pending moves as context so chains across drags validate', () => {
        const holder = makeSocketHolder();
        const canvas = makeCanvas();
        const origin = {
            originX: 0,
            originY: 0,
            roomCells: new Set(['3,3']),
            rooms: [
                { x: 3, y: 3, desc: 'A', exits: [] },
                { x: 10, y: 10, desc: 'B', exits: [] },
            ],
        };
        const session = new MapEditSession('K0', canvas, origin, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        // A moves onto free space (pending, unsaved)
        session.validateRoomMoves([{ fromX: 3, fromY: 3, toX: 4, toY: 3 }]);
        feed(holder.socket, '["moves_ok",[1,"K2"],{}]');
        // B then moves onto A's now-vacated origin: context tells the server
        // A's pending move vacates it, so this must not be denied
        session.validateRoomMoves([{ fromX: 10, fromY: 10, toX: 3, toY: 3 }]);
        expect(holder.socket.sent[2]).toBe(
            '["map_validate_moves",["K2",2,[[10,10,3,3]],[[3,3,4,3]]],{}]'
        );
        session.dispose();
    });

    it('ignores moves that do not start on a known room', () => {
        const { holder, session } = makeRoomSession();
        session.validateRoomMoves([{ fromX: 0, fromY: 0, toX: 1, toY: 0 }]);
        expect(holder.socket.sent.length).toBe(1);
        session.dispose();
    });

    it('emits moves_denied with the client moves and drops them from pending', () => {
        const { holder, session } = makeRoomSession();
        const events: MoveEvt[] = [];
        session.onEvent((e) => events.push(e as MoveEvt));
        session.validateRoomMoves([{ fromX: 3, fromY: 3, toX: 9, toY: 9 }]);
        feed(holder.socket, '["moves_denied",[1,"K2",[0]],{}]');

        expect(events.filter((e) => e.type === 'moves_denied')).toEqual([
            { type: 'moves_denied', moves: [{ fromX: 3, fromY: 3, toX: 9, toY: 9 }] },
        ]);

        // nothing pending and no glyph diff -> saving reports there is nothing
        session.saveToServer();
        expect(events.some((e) => e.type === 'error' && e.message === 'Nothing to save.')).toBe(true);
        expect(holder.socket.sent.length).toBe(2);
        session.dispose();
    });

    it('emits moves_accepted carrying the validated client moves', () => {
        const { holder, session } = makeRoomSession();
        const events: MoveEvt[] = [];
        session.onEvent((e) => events.push(e as MoveEvt));
        session.validateRoomMoves([{ fromX: 3, fromY: 3, toX: 4, toY: 3 }]);
        feed(holder.socket, '["moves_ok",[1,"K2"],{}]');
        expect(events.filter((e) => e.type === 'moves_accepted')).toEqual([
            { type: 'moves_accepted', moves: [{ fromX: 3, fromY: 3, toX: 4, toY: 3 }] },
        ]);
        session.dispose();
    });

    it('saves glyph diffs and validated room ops in one batch and clears them on ack', () => {
        const { holder, canvas, session } = makeRoomSession();
        const events: MoveEvt[] = [];
        session.onEvent((e) => events.push(e as MoveEvt));

        session.validateRoomMoves([{ fromX: 3, fromY: 3, toX: 4, toY: 3 }]);
        feed(holder.socket, '["moves_ok",[1,"K2"],{}]');

        // unsynced glyph change + pending move go out in one batch
        canvas.setCell(0, 0, { char: 'X', fg: [255, 0, 0], bg: [-1, -1, -1] });
        session.saveToServer();
        expect(holder.socket.sent[2]).toBe(
            '["map_edit",["K2",2,[[0,0,"X",[255,0,0],[0,0,0],[]],["room",3,3,4,3]]],{}]'
        );
        expect(events.some((e) => e.type === 'saved')).toBe(false);

        ack(holder.socket, 2, 'K3');
        expect(events.some((e) => e.type === 'saved')).toBe(true);

        // pending moves consumed: another save has nothing to send
        session.saveToServer();
        expect(events.some((e) => e.type === 'error' && e.message === 'Nothing to save.')).toBe(true);
        expect(holder.socket.sent.length).toBe(3);
        session.dispose();
    });

    it('keeps allowed moves pending when only some are denied', () => {
        const holder = makeSocketHolder();
        const canvas = makeCanvas();
        const origin = {
            originX: 0,
            originY: 0,
            roomCells: new Set(['3,3', '10,10']),
            rooms: [
                { x: 3, y: 3, desc: 'Hall', exits: [] },
                { x: 10, y: 10, desc: 'Kitchen', exits: [] },
            ],
        };
        const session = new MapEditSession('K0', canvas, origin, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');

        const events: MoveEvt[] = [];
        session.onEvent((e) => events.push(e as MoveEvt));
        session.validateRoomMoves([
            { fromX: 3, fromY: 3, toX: 4, toY: 3 },
            { fromX: 10, fromY: 10, toX: 20, toY: 20 },
        ]);
        feed(holder.socket, '["moves_denied",[1,"K2",[1]],{}]');

        expect(events.filter((e) => e.type === 'moves_denied')).toEqual([
            { type: 'moves_denied', moves: [{ fromX: 10, fromY: 10, toX: 20, toY: 20 }] },
        ]);

        // only the allowed move survives in pending
        session.saveToServer();
        expect(holder.socket.sent[2]).toBe('["map_edit",["K2",2,[["room",3,3,4,3]]],{}]');
        session.dispose();
    });
});

describe('MapEditSession ack-then-ok pairs', () => {
    it('room pair emits one saved event and keeps the pair key', () => {
        const holder = makeSocketHolder();
        const events: string[] = [];
        const session = new MapEditSession('K0', makeCanvas(), { originX: 0, originY: 0 }, holder.createSocket);
        session.onEvent((event) => events.push(event.type));
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        session.saveRoom(2, 3, 'Hall', 'A dusty hall.');
        // Production order: map_ack then room_ok with the same key.
        ack(holder.socket, 1, 'K2');
        holder.socket.onmessage?.(new MessageEvent('message', { data: '["room_ok",[1,"K2"],{}]' }));
        expect(events).toEqual(['synced', 'room_saved', 'synced']);
        session.saveRoom(2, 3, 'Hall', 'Again.');
        expect(holder.socket.sent[2]).toBe(
            '["map_edit_room",["K2",2,{"x":2,"y":3,"name":"Hall","desc":"Again."}],{}]'
        );
        session.dispose();
    });

    it('exits pair emits one saved event and keeps the pair key', () => {
        const holder = makeSocketHolder();
        const events: string[] = [];
        const session = new MapEditSession('K0', makeCanvas(), { originX: 0, originY: 0 }, holder.createSocket);
        session.onEvent((event) => events.push(
            event.type === 'exits_saved' ? `exits_saved:${event.x},${event.y}` : event.type
        ));
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        const exits = [{ name: 'east', aliases: ['e'], coord: ['TestArea', 5, 5, 0] as [string, number, number, number] }];
        session.setExits(2, 3, exits);
        ack(holder.socket, 1, 'K2');
        holder.socket.onmessage?.(new MessageEvent('message', { data: '["exits_ok",[1,"K2"],{}]' }));
        expect(events).toEqual(['synced', 'exits_saved:2,3', 'synced']);
        session.setExits(2, 3, exits);
        expect(holder.socket.sent[2]).toBe(
            '["map_edit_exits",["K2",2,{"x":2,"y":3,"exits":[{"name":"east","aliases":["e"],"coord":["TestArea",5,5,0]}]}],{}]'
        );
        session.dispose();
    });
});

describe('MapEditSession room creation', () => {
    function makeCreateSession() {
        const holder = makeSocketHolder();
        const events: string[] = [];
        const session = new MapEditSession('K0', makeCanvas(), { originX: 0, originY: 0 }, holder.createSocket);
        session.onEvent((event) => events.push(
            event.type === 'create_denied' ? `create_denied:${event.rooms.map((r) => `${r.x},${r.y}`).join(';')}:${event.reason}`
                : event.type === 'created' ? `created:${event.rooms.map((r) => `${r.x},${r.y}`).join(';')}`
                : event.type === 'error' ? `error:${event.message}` : event.type
        ));
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        return { holder, events, session };
    }

    const rooms = [
        { x: 0, y: 0, name: null, desc: null },
        { x: 1, y: 0, name: null, desc: null },
    ];
    const exits = [
        { x: 0, y: 0, exits: [{ name: 'east', aliases: ['e'], coord: ['TestArea', 1, 0, 0] as [string, number, number, number] }] },
        { x: 1, y: 0, exits: [{ name: 'west', aliases: ['w'], coord: ['TestArea', 0, 0, 0] as [string, number, number, number] }] },
    ];

    it('sends map_create_rooms and emits created plus tracking on map_ack', () => {
        const { holder, events, session } = makeCreateSession();
        session.createRooms(rooms, exits);
        expect(holder.socket.sent[1]).toBe(
            '["map_create_rooms",["K1",1,{"rooms":[{"x":0,"y":0,"name":null,"desc":null},{"x":1,"y":0,"name":null,"desc":null}],"exits":[{"x":0,"y":0,"exits":[{"name":"east","aliases":["e"],"coord":["TestArea",1,0,0]}]},{"x":1,"y":0,"exits":[{"name":"west","aliases":["w"],"coord":["TestArea",0,0,0]}]}]}],{}]'
        );
        ack(holder.socket, 1, 'K2');
        expect(events).toEqual(['synced', 'created:0,0;1,0', 'synced']);
        expect(session.currentRoomCoords()).toEqual([{ x: 0, y: 0 }, { x: 1, y: 0 }]);
        session.dispose();
    });

    it('emits created on create_ok', () => {
        const { holder, events, session } = makeCreateSession();
        session.createRooms(rooms, exits);
        holder.socket.onmessage?.(new MessageEvent('message', { data: '["create_ok",[1,"K2"],{}]' }));
        expect(events).toEqual(['synced', 'created:0,0;1,0', 'synced']);
        expect(session.currentRoomCoords()).toEqual([{ x: 0, y: 0 }, { x: 1, y: 0 }]);
        session.dispose();
    });

    it('emits create_denied, rotates the key, and keeps the session alive', () => {
        const { holder, events, session } = makeCreateSession();
        session.createRooms(rooms, [{ x: 0, y: 0, exits: [{ name: 'doom', aliases: [], coord: ['TestArea', 9, 9, 0] }] }]);
        holder.socket.onmessage?.(
            new MessageEvent('message', { data: '["create_denied",[1,"K2","No room at TestArea(9,9,0) for exit \'doom\'."],{}]' })
        );
        expect(events).toEqual(['synced', "create_denied:0,0;1,0:No room at TestArea(9,9,0) for exit 'doom'."]);
        expect(session.currentRoomCoords()).toEqual([]);
        session.createRooms(rooms, exits);
        expect(holder.socket.sent[2]).toContain('"K2",2,');
        session.dispose();
    });

    it('forgetRooms drops tracked coords without touching loaded rooms', () => {
        const holder = makeSocketHolder();
        const canvas = makeCanvas();
        const session = new MapEditSession('K0', canvas, {
            originX: 0, originY: 0,
            roomCells: new Set(),
            rooms: [{ x: 5, y: 5, exits: [] }],
        }, holder.createSocket);
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        session.createRooms(rooms, exits);
        ack(holder.socket, 1, 'K2');
        expect(session.currentRoomCoords()).toEqual([{ x: 5, y: 5 }, { x: 0, y: 0 }, { x: 1, y: 0 }]);
        session.forgetRooms([{ x: 0, y: 0 }, { x: 1, y: 0 }]);
        expect(session.currentRoomCoords()).toEqual([{ x: 5, y: 5 }]);
        session.dispose();
    });

    it('rejects an empty room list without sending', () => {
        const { holder, events, session } = makeCreateSession();
        session.createRooms([], exits);
        expect(events).toEqual(['synced', 'error:Invalid create payload.']);
        expect(holder.socket.sent).toEqual(['["map_edit",["K0",0,[]],{}]']);
        session.dispose();
    });

    it('rejects non-integer coords without sending', () => {
        const { holder, events, session } = makeCreateSession();
        session.createRooms([{ x: 1.5, y: 0, name: null, desc: null }], []);
        expect(events).toEqual(['synced', 'error:Invalid create payload.']);
        expect(holder.socket.sent).toEqual(['["map_edit",["K0",0,[]],{}]']);
        session.dispose();
    });
});

describe('MapEditSession room deletion', () => {
    function makeDeleteSession() {
        const holder = makeSocketHolder();
        const events: string[] = [];
        const session = new MapEditSession('K0', makeCanvas(), {
            originX: 0,
            originY: 0,
            roomCells: new Set(),
            rooms: [{ x: 0, y: 0, exits: [] }, { x: 5, y: 5, exits: [] }],
        }, holder.createSocket);
        session.onEvent((event) => events.push(
            event.type === 'delete_denied' ? `delete_denied:${event.rooms.map((r) => `${r.x},${r.y}`).join(';')}:${event.reason}`
                : event.type === 'deleted' ? `deleted:${event.rooms.map((r) => `${r.x},${r.y}`).join(';')}`
                : event.type === 'error' ? `error:${event.message}` : event.type
        ));
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        return { holder, events, session };
    }

    it('sends map_delete_rooms with fallbacks and forgets tracking on map_ack', () => {
        const { holder, events, session } = makeDeleteSession();
        session.deleteRooms([
            { x: 0, y: 0, fallback: { x: 5, y: 5 } },
            { x: 1, y: 0, fallback: null },
        ]);
        expect(holder.socket.sent[1]).toBe(
            '["map_delete_rooms",["K1",1,{"rooms":[{"x":0,"y":0,"fallback":{"x":5,"y":5}},{"x":1,"y":0}]}],{}]'
        );
        ack(holder.socket, 1, 'K2');
        expect(events).toEqual(['synced', 'deleted:0,0;1,0', 'synced']);
        expect(session.currentRoomCoords()).toEqual([{ x: 5, y: 5 }]);
        session.dispose();
    });

    it('emits deleted on delete_ok', () => {
        const { holder, events, session } = makeDeleteSession();
        session.deleteRooms([{ x: 0, y: 0, fallback: null }]);
        holder.socket.onmessage?.(new MessageEvent('message', { data: '["delete_ok",[1,"K2"],{}]' }));
        expect(events).toEqual(['synced', 'deleted:0,0', 'synced']);
        expect(session.currentRoomCoords()).toEqual([{ x: 5, y: 5 }]);
        session.dispose();
    });

    it('emits delete_denied, rotates the key, and keeps tracking plus the session alive', () => {
        const { holder, events, session } = makeDeleteSession();
        session.deleteRooms([{ x: 0, y: 0, fallback: null }]);
        holder.socket.onmessage?.(
            new MessageEvent('message', { data: '["delete_denied",[1,"K2","Room (0, 0) is occupied with no usable fallback."],{}]' })
        );
        expect(events).toEqual(['synced', 'delete_denied:0,0:Room (0, 0) is occupied with no usable fallback.']);
        expect(session.currentRoomCoords()).toEqual([{ x: 0, y: 0 }, { x: 5, y: 5 }]);
        session.deleteRooms([{ x: 0, y: 0, fallback: { x: 5, y: 5 } }]);
        expect(holder.socket.sent[2]).toContain('"K2",2,');
        session.dispose();
    });

    it('rejects an empty room list without sending', () => {
        const { holder, events, session } = makeDeleteSession();
        session.deleteRooms([]);
        expect(events).toEqual(['synced', 'error:Invalid delete payload.']);
        expect(holder.socket.sent).toEqual(['["map_edit",["K0",0,[]],{}]']);
        session.dispose();
    });

    it('rejects non-integer coords and fallbacks without sending', () => {
        const { holder, events, session } = makeDeleteSession();
        session.deleteRooms([{ x: 1.5, y: 0, fallback: null }]);
        session.deleteRooms([{ x: 0, y: 0, fallback: { x: 5.5, y: 5 } }]);
        expect(events).toEqual(['synced', 'error:Invalid delete payload.', 'error:Invalid delete payload.']);
        expect(holder.socket.sent).toEqual(['["map_edit",["K0",0,[]],{}]']);
        session.dispose();
    });
});
