import { WebSocketConnection, WebSocketLike } from './webclient/connection';
import { ConnectionState, WireMessage } from './webclient/types';
import { CanvasState } from './state/CanvasState';
import { Cell, Color } from './types';
import { EditorSettings } from './editorSettings';
import { parseAnsiSymbol, stripAnsi, wrapLegendSymbol, DEFAULT_FG, TRANSPARENT } from './utils/ansiParser';

export interface MapEditExit {
    name: string;
    aliases: string[];
    coord: [string, number, number, number];
}

export interface MapRoom {
    x: number;
    y: number;
    /** Custom display name when set, else the coord-derived node name. */
    name?: string;
    desc?: string;
    exits: MapEditExit[];
}

/** One exit in a set-exits save: full replacement entry for a room link. */
export interface MapExitEdit {
    name: string;
    aliases: string[];
    coord: [string, number, number, number];
}

/** Exit-coord textbox format, matching the engine's Coord shape:
 * `(Area,x,y,z)`. Round-trips through parseExitCoord. */
export function formatExitCoord(area: string, x: number, y: number, z: number): string {
    return `(${area},${x},${y},${z})`;
}

/** Parse a `(Area,x,y,z)` coord textbox back, or null when malformed. */
export function parseExitCoord(text: string): { area: string; x: number; y: number; z: number } | null {
    const match = /^\(([^,()]+),(-?\d+),(-?\d+),(-?\d+)\)$/.exec(text.trim());
    if (!match) return null;
    return { area: match[1], x: Number(match[2]), y: Number(match[3]), z: Number(match[4]) };
}

export interface MapLegendEntry {
    symbol: string;
    desc: string | null;
    coord: [number, number] | null;
    show: boolean;
    fg?: Color | null;
    bg?: Color | null;
}

export interface MapEditPayload {
    area: string;
    z: number;
    grid: [number, number, string][];
    rooms?: MapRoom[];
    legend?: MapLegendEntry[];
    playerSymbol?: string;
    /** Saved editor chrome from a previous save; restored on open. */
    editorSettings?: EditorSettings;
}

/** One edited cell sent back to the engine:
 * `[x, y, char, fg, bg, attrs]` with fg/bg as [r,g,b] or [-1,-1,-1] (transparent)
 * and attrs a subset of ["bold", "italic", "underline"]. */
export type MapEditCell = [number, number, string, Color, Color, string[]];

/** One room-move op: `["room", fromX, fromY, toX, toY]`. */
export type MapEditOp = MapEditCell | [string, number, number, number, number];

export interface RoomMove {
    fromX: number;
    fromY: number;
    toX: number;
    toY: number;
}

export interface MapEditOrigin {
    originX: number;
    originY: number;
    roomCells: Set<string>;
    rooms: MapRoom[];
}

export type MapEditEvent =
    | { type: 'synced' }
    | { type: 'reject'; reason: string }
    | { type: 'error'; message: string }
    | { type: 'moves_denied'; moves: RoomMove[] }
    | { type: 'moves_accepted'; moves: RoomMove[] }
    | { type: 'saved' }
    | { type: 'legend_saved' }
    | { type: 'room_saved' }
    | { type: 'room_denied'; reason: string }
    | { type: 'exits_saved'; x: number; y: number }
    | { type: 'exits_denied'; x: number; y: number; reason: string }
    | { type: 'created'; rooms: { x: number; y: number }[] }
    | { type: 'create_denied'; rooms: { x: number; y: number }[]; reason: string }
    | { type: 'deleted'; rooms: { x: number; y: number }[] }
    | { type: 'delete_denied'; rooms: { x: number; y: number }[]; reason: string };

export type MapEditListener = (event: MapEditEvent) => void;

const SYNC_DELAY_MS = 200;

export function loadMapPayload(canvas: CanvasState, payload: MapEditPayload): MapEditOrigin {
    if (payload.grid.length === 0) {
        canvas.resize(1, 1);
        return { originX: 0, originY: 0, roomCells: new Set(), rooms: payload.rooms ?? [] };
    }
    let minX = payload.grid[0][0];
    let minY = payload.grid[0][1];
    let maxX = minX;
    let maxY = minY;
    for (const [x, y] of payload.grid) {
        minX = Math.min(minX, x);
        minY = Math.min(minY, y);
        maxX = Math.max(maxX, x);
        maxY = Math.max(maxY, y);
    }
    for (const room of payload.rooms ?? []) {
        minX = Math.min(minX, room.x);
        minY = Math.min(minY, room.y);
        maxX = Math.max(maxX, room.x);
        maxY = Math.max(maxY, room.y);
    }
    const mapWidth = Math.max(1, maxX - minX + 1);
    const mapHeight = Math.max(1, maxY - minY + 1);
    canvas.resize(mapWidth * 2, mapHeight * 2);
    const toRow = (y: number) => canvas.height - 1 - (y - minY);
    const batch: { col: number; row: number; cell: Cell }[] = [];
    for (const [x, y, symbol] of payload.grid) {
        if (symbol === '') continue;
        batch.push({ col: x - minX, row: toRow(y), cell: parseAnsiSymbol(symbol) });
    }
    canvas.applyBatch(batch);
    const roomCells = new Set<string>();
    for (const room of payload.rooms ?? []) {
        roomCells.add(`${room.x - minX},${toRow(room.y)}`);
    }
    return { originX: minX, originY: minY, roomCells, rooms: payload.rooms ?? [] };
}

export function logRoomData(payload: MapEditPayload): void {
    if (!(import.meta as unknown as { env?: { DEV?: boolean } }).env?.DEV) return;
    console.log(`Room data for ${payload.area} (z=${payload.z}):`);
    const rooms = payload.rooms ?? [];
    if (rooms.length === 0) {
        console.log('No rooms found.');
        return;
    }
    for (const room of rooms) {
        const exits = room.exits
            .map((e) => `${e.name} -> ${e.coord[1]},${e.coord[2]} (${e.coord[0]}, z=${e.coord[3]})`)
            .join(', ');
        const label = room.name ? `(${room.x}, ${room.y}) ${room.name}: ${room.desc ?? '(no description)'}` : `(${room.x}, ${room.y}): ${room.desc ?? '(no description)'}`;
        console.log(`${label} | exits: ${exits || 'none'}`);
    }
}

function serializeCell(cell: Cell | null): string {
    if (!cell) return '';
    return [
        cell.char,
        cell.fg.join(','),
        cell.bg.join(','),
        cell.bold ?? false,
        cell.italic ?? false,
        cell.underline ?? false,
    ].join('|');
}

function cellAttrs(cell: Cell | null): string[] {
    const attrs: string[] = [];
    if (!cell) return attrs;
    if (cell.bold) attrs.push('bold');
    if (cell.italic) attrs.push('italic');
    if (cell.underline) attrs.push('underline');
    return attrs;
}

type QueueItem =
    | { kind: 'edit'; cells: MapEditOp[]; isSave: boolean; settings?: EditorSettings }
    | { kind: 'validate'; serverMoves: RoomMove[]; clientMoves: RoomMove[]; context: RoomMove[] }
    | { kind: 'legend'; legend: MapLegendEntry[] }
    | { kind: 'room'; x: number; y: number; name: string | null; desc: string | null }
    | { kind: 'exits'; x: number; y: number; exits: MapExitEdit[] }
    | { kind: 'create'; rooms: { x: number; y: number; name: string | null; desc: string | null }[]; exits: { x: number; y: number; exits: MapExitEdit[] }[] }
    | { kind: 'delete'; rooms: { x: number; y: number; fallback: { x: number; y: number } | null }[] };

const coordKey = (x: number, y: number): string => `${x},${y}`;

export class MapEditSession {
    private conn: WebSocketConnection;
    private key: string;
    private seq = 1;
    private canvas: CanvasState;
    private originX: number;
    private originY: number;
    private baseline = new Map<string, string>();
    private queue: QueueItem[] = [];
    private inFlight: { seq: number; item: QueueItem } | null = null;
    private handshakeSent = false;
    private syncTimer: ReturnType<typeof setTimeout> | null = null;
    private stopped = false;
    private listener: MapEditListener | null = null;
    /** original world coord -> current world coord for every known room */
    private roomPositions = new Map<string, string>();
    /** validated room moves (in server coords) not yet persisted */
    private pendingMoves: RoomMove[] = [];

    constructor(key: string, canvas: CanvasState, origin: MapEditOrigin, createSocket?: (url: string) => WebSocketLike) {
        this.key = key;
        this.canvas = canvas;
        this.originX = origin.originX;
        this.originY = origin.originY;
        for (const room of origin.rooms ?? []) {
            this.roomPositions.set(coordKey(room.x, room.y), coordKey(room.x, room.y));
        }
        this.snapshotBaseline();
        this.conn = new WebSocketConnection({
            createSocket,
            onMessage: (message) => this.handleMessage(message),
            onStateChange: (state) => this.handleStateChange(state),
        });
        this.conn.connect();
    }

    public onEvent(listener: MapEditListener): void {
        this.listener = listener;
    }

    /** Rebind the session to the live canvas after a host-side swap
     * (TextTool/undo/redo/New/import/ANSI load/color-adjust). Cheap and
     * idempotent: swaps the reference and reconciles the baseline so
     * computeDiff/saveToServer read the live object, never a discarded
     * one. Plain swaps re-baseline; pass `{ keepBaseline: true }` for a
     * deliberate clear (New) so the next diff expresses the wipe as
     * deletions against the old baseline — otherwise the cleared map
     * would never reach the server. */
    public rebindCanvas(canvas: CanvasState, opts?: { keepBaseline?: boolean }): void {
        this.canvas = canvas;
        if (opts?.keepBaseline) return;
        this.baseline.clear();
        this.snapshotBaseline();
    }

    /** Update the world origin (rare; room tracking is untouched). */
    public setOrigin(origin: MapEditOrigin): void {
        this.originX = origin.originX;
        this.originY = origin.originY;
    }

    public scheduleSync(): void {
        if (this.stopped || this.syncTimer !== null) return;
        this.syncTimer = setTimeout(() => {
            this.syncTimer = null;
            const cells = this.computeDiff();
            if (cells.length > 0) {
                this.queue.push({ kind: 'edit', cells, isSave: false });
                this.flush();
            }
        }, SYNC_DELAY_MS);
    }

    /** Queue room moves for server-side validation. Moves are expressed in
     * client (current) coords and folded back through pending moves so the
     * server — which has not yet received any save — sees its own state. */
    public validateRoomMoves(moves: RoomMove[]): void {
        if (this.stopped || moves.length === 0) return;
        const known = new Set(this.roomPositions.values());
        const clientMoves: RoomMove[] = [];
        const serverMoves: RoomMove[] = [];
        // context = pendings already validated (the moves being sent now are
        // not their own context)
        const context = this.pendingMoves.slice();
        for (const move of moves) {
            if (!known.has(coordKey(move.fromX, move.fromY))) continue;
            // Unfold the full chain back to the original server-side coord,
            // consuming each prior hop so save sends one op per room.
            let fromX = move.fromX;
            let fromY = move.fromY;
            for (;;) {
                const prior = this.pendingMoves.find((p) => p.toX === fromX && p.toY === fromY);
                if (!prior) break;
                fromX = prior.fromX;
                fromY = prior.fromY;
                this.pendingMoves = this.pendingMoves.filter((p) => p !== prior);
            }
            clientMoves.push(move);
            serverMoves.push({ fromX, fromY, toX: move.toX, toY: move.toY });
            this.pendingMoves.push({ fromX, fromY, toX: move.toX, toY: move.toY });
            // Track client-side occupancy within the batch so chained moves
            // (A->B, B->C in one stroke) fold instead of dropping the second.
            known.delete(coordKey(move.fromX, move.fromY));
            known.add(coordKey(move.toX, move.toY));
        }
        if (clientMoves.length === 0) return;
        this.queue.push({ kind: 'validate', serverMoves, clientMoves, context });
        this.flush();
    }

    /** Send all unsnapshotted glyph changes plus every validated room move
     * to the server in a single batch. The server is only updated here.
     * Editor settings ride along as a fourth map_edit arg when provided,
     * even when the map itself is unchanged (settings-only save). */
    public saveToServer(settings?: EditorSettings): void {
        if (this.stopped) return;
        const cells = this.computeDiff();
        const ops: MapEditOp[] = [
            ...cells,
            ...this.pendingMoves.map((m) => ['room', m.fromX, m.fromY, m.toX, m.toY] as MapEditOp),
        ];
        if (ops.length === 0 && settings === undefined) {
            this.listener?.({ type: 'error', message: 'Nothing to save.' });
            return;
        }
        this.queue.push({ kind: 'edit', cells: ops, isSave: true, settings });
        this.flush();
    }

    public saveLegend(legend: MapLegendEntry[]): void {
        if (this.stopped) return;
        if (legend.length > 200) {
            this.listener?.({ type: 'error', message: 'Too many legend entries (max 200).' });
            return;
        }
        for (const e of legend) {
            const vis = stripAnsi(e.symbol ?? '');
            if (!vis || vis.length === 0 || vis.length > 2) {
                this.listener?.({ type: 'error', message: `Invalid legend symbol: ${e.symbol}` });
                return;
            }
            if (e.desc == null || e.desc.trim().length === 0) {
                this.listener?.({ type: 'error', message: `Legend description required for symbol ${e.symbol}` });
                return;
            }
        }
        this.queue.push({ kind: 'legend', legend });
        this.flush();
    }

    /** Queue one room's name/description for the server. Null name/desc
     * leaves that server property unchanged; the room must already exist. */
    public saveRoom(x: number, y: number, name: string | null, desc: string | null): void {
        if (this.stopped) return;
        if (!Number.isInteger(x) || !Number.isInteger(y)) {
            this.listener?.({ type: 'error', message: 'Invalid room coordinates.' });
            return;
        }
        this.queue.push({ kind: 'room', x, y, name, desc });
        this.flush();
    }

    /** Queue a full exit-list replacement for one room. The list is the
     * room's entire link set after the edit (delete/rename/relink all
     * express as a replacement); the room must already exist. */
    public setExits(x: number, y: number, exits: MapExitEdit[]): void {
        if (this.stopped) return;
        if (!Number.isInteger(x) || !Number.isInteger(y) || !Array.isArray(exits)) {
            this.listener?.({ type: 'error', message: 'Invalid exits payload.' });
            return;
        }
        this.queue.push({ kind: 'exits', x, y, exits: exits.map((e) => ({ ...e, aliases: [...e.aliases], coord: [...e.coord] as [string, number, number, number] })) });
        this.flush();
    }

    /** Queue a room-creation batch: new rooms plus full exit-list
     * replacements for every room that gains a link (new rooms and
     * affected pre-existing neighbors alike). */
    public createRooms(
        rooms: { x: number; y: number; name: string | null; desc: string | null }[],
        exits: { x: number; y: number; exits: MapExitEdit[] }[],
    ): void {
        if (this.stopped) return;
        const validRooms = Array.isArray(rooms) && rooms.length > 0 && rooms.length <= 256
            && rooms.every((r) => r && Number.isInteger(r.x) && Number.isInteger(r.y));
        const validExits = Array.isArray(exits)
            && exits.every((e) => e && Number.isInteger(e.x) && Number.isInteger(e.y) && Array.isArray(e.exits));
        if (!validRooms || !validExits) {
            this.listener?.({ type: 'error', message: 'Invalid create payload.' });
            return;
        }
        this.queue.push({
            kind: 'create',
            rooms: rooms.map((r) => ({ x: r.x, y: r.y, name: r.name, desc: r.desc })),
            exits: exits.map((e) => ({
                x: e.x,
                y: e.y,
                exits: e.exits.map((x) => ({ ...x, aliases: [...x.aliases], coord: [...x.coord] as [string, number, number, number] })),
            })),
        });
        this.flush();
    }

    /** Queue a room-deletion batch: every listed room is removed server-side
     * (occupants evacuate to that entry's fallback, or the entry is denied
     * when occupied without one). Missing rooms are skipped idempotently. */
    public deleteRooms(rooms: { x: number; y: number; fallback: { x: number; y: number } | null }[]): void {
        if (this.stopped) return;
        const valid = Array.isArray(rooms) && rooms.length > 0 && rooms.length <= 256
            && rooms.every((r) => r && Number.isInteger(r.x) && Number.isInteger(r.y)
                && (r.fallback == null || (Number.isInteger(r.fallback.x) && Number.isInteger(r.fallback.y))));
        if (!valid) {
            this.listener?.({ type: 'error', message: 'Invalid delete payload.' });
            return;
        }
        this.queue.push({
            kind: 'delete',
            rooms: rooms.map((r) => ({
                x: r.x,
                y: r.y,
                fallback: r.fallback ? { x: r.fallback.x, y: r.fallback.y } : null,
            })),
        });
        this.flush();
    }

    /** Current world coords of all known rooms (after validated moves). */
    public currentRoomCoords(): { x: number; y: number }[] {
        return Array.from(this.roomPositions.values()).map((key) => {
            const [x, y] = key.split(',').map(Number);
            return { x, y };
        });
    }

    /** Drop tracked rooms (create-rooms undo): the server keeps the shells,
     * but the session must mirror the local room list or move validation
     * and coord sync drift out of step with it. Matches original or current
     * coords so a validated move in between does not orphan the entry. */
    public forgetRooms(coords: { x: number; y: number }[]): void {
        const wanted = new Set(coords.map((c) => coordKey(c.x, c.y)));
        for (const [orig, current] of Array.from(this.roomPositions.entries())) {
            if (wanted.has(orig) || wanted.has(current)) this.roomPositions.delete(orig);
        }
    }

    public dispose(): void {
        this.stopped = true;
        if (this.syncTimer !== null) {
            clearTimeout(this.syncTimer);
            this.syncTimer = null;
        }
        this.queue = [];
        this.conn.close();
    }

    private snapshotBaseline(): void {
        for (let row = 0; row < this.canvas.height; row++) {
            for (let col = 0; col < this.canvas.width; col++) {
                const composite = this.canvas.getCompositeCell(col, row);
                this.baseline.set(`${col},${row}`, serializeCell(composite));
            }
        }
    }

    private computeDiff(): MapEditCell[] {
        const cells: MapEditCell[] = [];
        for (let row = 0; row < this.canvas.height; row++) {
            for (let col = 0; col < this.canvas.width; col++) {
                const composite = this.canvas.getCompositeCell(col, row);
                const key = `${col},${row}`;
                const serialized = serializeCell(composite);
                if (this.baseline.get(key) !== serialized) {
                    cells.push([
                        col + this.originX,
                        this.canvas.height - 1 - row + this.originY,
                        composite?.char ?? '',
                        composite ? [...composite.fg] : [204, 204, 204],
                        composite ? [...composite.bg] : [0, 0, 0],
                        cellAttrs(composite),
                    ]);
                    this.baseline.set(key, serialized);
                }
            }
        }
        return cells;
    }

    private flush(): void {
        if (this.stopped || this.inFlight || this.queue.length === 0) return;
        if (this.conn.getState() !== 'open') return;
        const item = this.queue.shift()!;
        this.inFlight = { seq: this.seq, item };
        this.seq += 1;
        if (!this.sendItem(this.inFlight.seq, item)) {
            // The socket dropped between the state check and the send, so
            // the bytes never hit the wire. Requeue at the front and release
            // inFlight so a later flush/reconnect resends it. The consumed
            // seq is kept (never sent, so no ack can collide with it).
            this.queue.unshift(item);
            this.inFlight = null;
            return;
        }
    }

    /** Serialize one queue item onto the wire. Returns conn.send()'s
     * boolean so callers can requeue when the socket is no longer open. */
    private sendItem(seq: number, item: QueueItem): boolean {
        if (item.kind === 'validate') {
            return this.conn.send(
                'map_validate_moves',
                [
                    this.key,
                    seq,
                    item.serverMoves.map((m) => [m.fromX, m.fromY, m.toX, m.toY]),
                    item.context.map((m) => [m.fromX, m.fromY, m.toX, m.toY]),
                ]
            );
        } else if (item.kind === 'legend') {
            return this.conn.send('map_edit_legend', [this.key, seq, item.legend.map((e) => {
                const fg = (Array.isArray(e.fg) && e.fg.length === 3 ? e.fg as Color : DEFAULT_FG);
                const bg = (Array.isArray(e.bg) && e.bg.length === 3 ? e.bg as Color : TRANSPARENT);
                const vis = stripAnsi(e.symbol ?? '');
                const ch = vis || e.symbol || 'X';
                const wrapped = wrapLegendSymbol(ch, fg, bg);
                return {
                    symbol: wrapped,
                    desc: e.desc,
                    coord: e.coord ? [...e.coord] : null,
                    show: e.show,
                    fg: null,
                    bg: null,
                };
            })]);
        } else if (item.kind === 'room') {
            return this.conn.send('map_edit_room', [
                this.key,
                seq,
                { x: item.x, y: item.y, name: item.name, desc: item.desc },
            ]);
        } else if (item.kind === 'exits') {
            return this.conn.send('map_edit_exits', [
                this.key,
                seq,
                {
                    x: item.x,
                    y: item.y,
                    exits: item.exits.map((e) => ({ name: e.name, aliases: [...e.aliases], coord: [...e.coord] })),
                },
            ]);
        } else if (item.kind === 'create') {
            return this.conn.send('map_create_rooms', [
                this.key,
                seq,
                {
                    rooms: item.rooms.map((r) => ({ x: r.x, y: r.y, name: r.name, desc: r.desc })),
                    exits: item.exits.map((e) => ({
                        x: e.x,
                        y: e.y,
                        exits: e.exits.map((x) => ({ name: x.name, aliases: [...x.aliases], coord: [...x.coord] })),
                    })),
                },
            ]);
        } else if (item.kind === 'delete') {
            return this.conn.send('map_delete_rooms', [
                this.key,
                seq,
                {
                    rooms: item.rooms.map((r) => r.fallback
                        ? { x: r.x, y: r.y, fallback: { x: r.fallback.x, y: r.fallback.y } }
                        : { x: r.x, y: r.y }),
                },
            ]);
        } else {
            const args: unknown[] = item.kind === 'edit' && item.settings !== undefined
                ? [this.key, seq, item.cells, item.settings]
                : [this.key, seq, item.cells];
            return this.conn.send('map_edit', args);
        }
    }

    private handleStateChange(state: ConnectionState): void {
        if (state === 'open') {
            if (!this.handshakeSent) {
                this.handshakeSent = true;
                this.inFlight = { seq: 0, item: { kind: 'edit', cells: [], isSave: false } };
                if (!this.sendItem(0, this.inFlight.item)) {
                    this.queue.unshift(this.inFlight.item);
                    this.inFlight = null;
                }
            } else if (this.inFlight) {
                const { seq, item } = this.inFlight;
                if (!this.sendItem(seq, item)) {
                    // Resend never hit the wire: requeue at the front and
                    // release inFlight so a later open flushes it. Not dropped.
                    this.queue.unshift(item);
                    this.inFlight = null;
                }
            } else {
                this.flush();
            }
        } else if (state === 'failed') {
            this.stopped = true;
            this.listener?.({ type: 'error', message: 'Connection failed.' });
        }
    }

    private handleMessage(message: WireMessage): void {
        const args: unknown[] = Array.isArray(message.args) ? message.args : [];
        if (message.command === 'map_ack') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string') return;
            if (this.inFlight && args[0] === this.inFlight.seq) {
                if (this.inFlight.item.kind === 'legend' || this.inFlight.item.kind === 'room' || this.inFlight.item.kind === 'exits') {
                    const kind = this.inFlight.item.kind;
                    const saved = kind === 'legend' ? { type: 'legend_saved' } as const
                        : kind === 'room' ? { type: 'room_saved' } as const
                        : { type: 'exits_saved', x: this.inFlight.item.x, y: this.inFlight.item.y } as const;
                    this.key = args[1];
                    this.inFlight = null;
                    this.listener?.(saved);
                    this.listener?.({ type: 'synced' });
                    this.flush();
                    return;
                }
                if (this.inFlight.item.kind === 'create') {
                    const created = this.inFlight.item.rooms.map((r) => ({ x: r.x, y: r.y }));
                    this.key = args[1];
                    this.inFlight = null;
                    // The rooms are server-side now: track them like loaded
                    // rooms so moves validate and coord sync stays aligned.
                    for (const c of created) this.roomPositions.set(coordKey(c.x, c.y), coordKey(c.x, c.y));
                    this.listener?.({ type: 'created', rooms: created });
                    this.listener?.({ type: 'synced' });
                    this.flush();
                    return;
                }
                if (this.inFlight.item.kind === 'delete') {
                    const deleted = this.inFlight.item.rooms.map((r) => ({ x: r.x, y: r.y }));
                    this.key = args[1];
                    this.inFlight = null;
                    // The rooms are gone server-side: stop tracking them so
                    // moves validate and coord sync stay aligned.
                    this.forgetRooms(deleted);
                    this.listener?.({ type: 'deleted', rooms: deleted });
                    this.listener?.({ type: 'synced' });
                    this.flush();
                    return;
                }
                const isSave = this.inFlight.item.kind === 'edit' && this.inFlight.item.isSave;
                this.key = args[1];
                this.inFlight = null;
                if (isSave) {
                    this.pendingMoves = [];
                    this.listener?.({ type: 'saved' });
                }
                this.listener?.({ type: 'synced' });
                this.flush();
            }
        } else if (message.command === 'legend_ok') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string') return;
            if (this.inFlight && this.inFlight.item.kind === 'legend' && args[0] === this.inFlight.seq) {
                this.key = args[1];
                this.inFlight = null;
                this.listener?.({ type: 'legend_saved' });
                this.listener?.({ type: 'synced' });
                this.flush();
            }
        } else if (message.command === 'room_ok') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string') return;
            if (this.inFlight && this.inFlight.item.kind === 'room' && args[0] === this.inFlight.seq) {
                this.key = args[1];
                this.inFlight = null;
                this.listener?.({ type: 'room_saved' });
                this.listener?.({ type: 'synced' });
                this.flush();
            }
        } else if (message.command === 'room_denied') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string' || typeof args[2] !== 'string') return;
            if (this.inFlight && this.inFlight.item.kind === 'room' && args[0] === this.inFlight.seq) {
                this.key = args[1];
                const reason = args[2];
                this.inFlight = null;
                this.listener?.({ type: 'room_denied', reason });
                this.flush();
            }
        } else if (message.command === 'exits_ok') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string') return;
            if (this.inFlight && this.inFlight.item.kind === 'exits' && args[0] === this.inFlight.seq) {
                const { x, y } = this.inFlight.item;
                this.key = args[1];
                this.inFlight = null;
                this.listener?.({ type: 'exits_saved', x, y });
                this.listener?.({ type: 'synced' });
                this.flush();
            }
        } else if (message.command === 'exits_denied') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string' || typeof args[2] !== 'string') return;
            if (this.inFlight && this.inFlight.item.kind === 'exits' && args[0] === this.inFlight.seq) {
                const { x, y } = this.inFlight.item;
                this.key = args[1];
                const reason = args[2];
                this.inFlight = null;
                this.listener?.({ type: 'exits_denied', x, y, reason });
                this.flush();
            }
        } else if (message.command === 'create_ok') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string') return;
            if (this.inFlight && this.inFlight.item.kind === 'create' && args[0] === this.inFlight.seq) {
                const created = this.inFlight.item.rooms.map((r) => ({ x: r.x, y: r.y }));
                this.key = args[1];
                this.inFlight = null;
                for (const c of created) this.roomPositions.set(coordKey(c.x, c.y), coordKey(c.x, c.y));
                this.listener?.({ type: 'created', rooms: created });
                this.listener?.({ type: 'synced' });
                this.flush();
            }
        } else if (message.command === 'create_denied') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string' || typeof args[2] !== 'string') return;
            if (this.inFlight && this.inFlight.item.kind === 'create' && args[0] === this.inFlight.seq) {
                this.key = args[1];
                const reason = args[2];
                const deniedRooms = this.inFlight.item.rooms.map((r) => ({ x: r.x, y: r.y }));
                this.inFlight = null;
                this.listener?.({ type: 'create_denied', rooms: deniedRooms, reason });
                this.flush();
            }
        } else if (message.command === 'delete_ok') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string') return;
            if (this.inFlight && this.inFlight.item.kind === 'delete' && args[0] === this.inFlight.seq) {
                const deleted = this.inFlight.item.rooms.map((r) => ({ x: r.x, y: r.y }));
                this.key = args[1];
                this.inFlight = null;
                this.forgetRooms(deleted);
                this.listener?.({ type: 'deleted', rooms: deleted });
                this.listener?.({ type: 'synced' });
                this.flush();
            }
        } else if (message.command === 'delete_denied') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string' || typeof args[2] !== 'string') return;
            if (this.inFlight && this.inFlight.item.kind === 'delete' && args[0] === this.inFlight.seq) {
                this.key = args[1];
                const reason = args[2];
                const deniedRooms = this.inFlight.item.rooms.map((r) => ({ x: r.x, y: r.y }));
                this.inFlight = null;
                this.listener?.({ type: 'delete_denied', rooms: deniedRooms, reason });
                this.flush();
            }
        } else if (message.command === 'moves_ok') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string') return;
            if (this.inFlight && this.inFlight.item.kind === 'validate' && args[0] === this.inFlight.seq) {
                const { clientMoves } = this.inFlight.item;
                this.key = args[1];
                this.inFlight = null;
                for (const move of clientMoves) {
                    let original: string | null = null;
                    for (const [orig, current] of this.roomPositions.entries()) {
                        if (current === coordKey(move.fromX, move.fromY)) {
                            original = orig;
                            break;
                        }
                    }
                    if (original === null) original = coordKey(move.fromX, move.fromY);
                    const dest = coordKey(move.toX, move.toY);
                    // Keep values unique: drop any other entry already
                    // pointing at the destination so overlapping acks cannot
                    // leave two rooms on one coord.
                    for (const [orig, current] of Array.from(this.roomPositions.entries())) {
                        if (orig !== original && current === dest) this.roomPositions.delete(orig);
                    }
                    this.roomPositions.set(original, dest);
                }
                this.listener?.({ type: 'moves_accepted', moves: clientMoves });
                this.flush();
            }
        } else if (message.command === 'moves_denied') {
            if (typeof args[0] !== 'number' || typeof args[1] !== 'string' || !Array.isArray(args[2])) return;
            if (this.inFlight && this.inFlight.item.kind === 'validate' && args[0] === this.inFlight.seq) {
                const { serverMoves, clientMoves } = this.inFlight.item;
                this.key = args[1];
                this.inFlight = null;
                // roll back pending entries for denied moves; allowed ones stay
                const deniedIdx = new Set(args[2].filter((i): i is number => typeof i === 'number'));
                for (let i = 0; i < serverMoves.length; i++) {
                    if (!deniedIdx.has(i)) continue;
                    const denied = serverMoves[i];
                    this.pendingMoves = this.pendingMoves.filter(
                        (p) => !(p.fromX === denied.fromX && p.fromY === denied.fromY && p.toX === denied.toX && p.toY === denied.toY)
                    );
                }
                const deniedClientMoves = clientMoves.filter((_, i) => deniedIdx.has(i));
                if (deniedClientMoves.length > 0) {
                    this.listener?.({ type: 'moves_denied', moves: deniedClientMoves });
                }
                this.flush();
            }
        } else if (message.command === 'map_edit_reject') {
            this.dispose();
            const reason = typeof args[0] === 'string' ? args[0] : 'unknown';
            this.listener?.({ type: 'reject', reason });
        }
    }
}