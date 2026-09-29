import { MapExitEdit, MapRoom, RoomMove } from './mapedit';

/** One room to create: null name/desc keeps the server default in place. */
export interface NewRoomSpec {
    x: number;
    y: number;
    name: string | null;
    desc: string | null;
}

/** Full exit-list replacement for one room (new or pre-existing neighbor). */
export interface RoomExitsReplacement {
    x: number;
    y: number;
    exits: MapExitEdit[];
}

/** Client-computed create payload: new rooms plus replacement exit lists
 * for every room that gains a link. Rooms whose lists would not change are
 * omitted so undo/redo journals stay minimal. */
export interface CreateRoomsPlan {
    rooms: NewRoomSpec[];
    exits: RoomExitsReplacement[];
}

interface Direction {
    dx: number;
    dy: number;
    name: string;
    alias: string;
}

// Link order matches the build command's table (north, south, east, west).
const DIRECTIONS: Direction[] = [
    { dx: 0, dy: 1, name: 'north', alias: 'n' },
    { dx: 0, dy: -1, name: 'south', alias: 's' },
    { dx: 1, dy: 0, name: 'east', alias: 'e' },
    { dx: -1, dy: 0, name: 'west', alias: 'w' },
];

const key = (x: number, y: number): string => `${x},${y}`;

const byCoord = (a: { x: number; y: number }, b: { x: number; y: number }): number => a.x - b.x || a.y - b.y;

/** Plan room creation for selected world coords: fresh squares become rooms
 * and every fresh room links to each orthogonal neighbor (fresh or
 * pre-existing) with reciprocal cardinal exits. Returns null when every
 * selected square is already a room. Never overwrites an existing exit name
 * and never adds a second exit to an already-linked target. */
export function planCreateRooms(
    selected: { x: number; y: number }[],
    existingRooms: MapRoom[],
    area: string,
    z: number,
): CreateRoomsPlan | null {
    const existing = new Map<string, MapRoom>();
    for (const room of existingRooms) {
        const k = key(room.x, room.y);
        if (!existing.has(k)) existing.set(k, room);
    }
    const fresh = new Map<string, { x: number; y: number }>();
    for (const cell of selected) {
        if (!Number.isInteger(cell.x) || !Number.isInteger(cell.y)) continue;
        const k = key(cell.x, cell.y);
        if (existing.has(k) || fresh.has(k)) continue;
        fresh.set(k, { x: cell.x, y: cell.y });
    }
    if (fresh.size === 0) return null;

    // Working exit lists, cloned from the live rooms so the caller's lists
    // are never mutated. Fresh rooms start empty.
    const working = new Map<string, MapExitEdit[]>();
    const exitsFor = (x: number, y: number): MapExitEdit[] => {
        const k = key(x, y);
        let list = working.get(k);
        if (!list) {
            const room = existing.get(k);
            list = (room?.exits ?? []).map((e) => ({
                ...e,
                aliases: [...e.aliases],
                coord: [...e.coord] as [string, number, number, number],
            }));
            working.set(k, list);
        }
        return list;
    };

    const link = (fromX: number, fromY: number, dir: Direction): void => {
        const tx = fromX + dir.dx;
        const ty = fromY + dir.dy;
        const list = exitsFor(fromX, fromY);
        // A hand-built exit keeps its name: never overwrite it. A room
        // already reaching the neighbor keeps its own link too.
        if (list.some((e) => e.name.toLowerCase() === dir.name)) return;
        if (list.some((e) => e.coord[0] === area && e.coord[1] === tx && e.coord[2] === ty && e.coord[3] === z)) return;
        list.push({ name: dir.name, aliases: [dir.alias], coord: [area, tx, ty, z] });
    };

    const freshSorted = [...fresh.values()].sort(byCoord);
    for (const room of freshSorted) {
        for (const dir of DIRECTIONS) {
            const tx = room.x + dir.dx;
            const ty = room.y + dir.dy;
            // Fresh rooms link to every orthogonal neighbor, fresh or not.
            if (!fresh.has(key(tx, ty)) && !existing.has(key(tx, ty))) continue;
            link(room.x, room.y, dir);
        }
    }
    // Affected pre-existing rooms link back, but only toward fresh rooms:
    // existing-to-existing links are out of scope for this op.
    const affected = new Map<string, { x: number; y: number }>();
    for (const room of freshSorted) {
        for (const dir of DIRECTIONS) {
            const tx = room.x + dir.dx;
            const ty = room.y + dir.dy;
            const k = key(tx, ty);
            if (!fresh.has(k) && existing.has(k) && !affected.has(k)) affected.set(k, { x: tx, y: ty });
        }
    }
    for (const room of [...affected.values()].sort(byCoord)) {
        for (const dir of DIRECTIONS) {
            const tx = room.x + dir.dx;
            const ty = room.y + dir.dy;
            if (!fresh.has(key(tx, ty))) continue;
            link(room.x, room.y, dir);
        }
    }

    const changed: RoomExitsReplacement[] = [];
    for (const room of freshSorted) {
        const list = working.get(key(room.x, room.y)) ?? [];
        if (list.length > 0) changed.push({ x: room.x, y: room.y, exits: list });
    }
    for (const room of [...affected.values()].sort(byCoord)) {
        const list = working.get(key(room.x, room.y)) ?? [];
        const before = existing.get(key(room.x, room.y))?.exits.length ?? 0;
        if (list.length > before) changed.push({ x: room.x, y: room.y, exits: list });
    }
    return {
        rooms: freshSorted.map(({ x, y }) => ({ x, y, name: null, desc: null })),
        exits: changed,
    };
}

/** Undo/redo journal entry for one applied create plan. Rooms are matched
 * by coords at replay time because list indexes shift as rooms come and go. */
export interface CreateJournalEntry {
    depth: number;
    created: MapRoom[];
    affected: { x: number; y: number; before: MapExitEdit[]; after: MapExitEdit[] }[];
}

export function cloneRoom(room: MapRoom): MapRoom {
    return {
        ...room,
        exits: room.exits.map((e) => ({
            ...e,
            aliases: [...e.aliases],
            coord: [...e.coord] as [string, number, number, number],
        })),
    };
}

export function findRoomIndex(rooms: MapRoom[], x: number, y: number): number {
    return rooms.findIndex((r) => r.x === x && r.y === y);
}

/** Relocate rooms by move identity: each validated from-coord moves to its
 * to-coord; rooms no move starts from are untouched and list order is
 * preserved. Matching by identity (never by position) keeps unacked
 * creates/deletes in flight from scrambling the list when the accepted
 * batch merely happens to match its length. Returns the relocated count. */
export function applyAcceptedMoves(rooms: MapRoom[], moves: RoomMove[]): number {
    let relocated = 0;
    for (const m of moves) {
        const index = findRoomIndex(rooms, m.fromX, m.fromY);
        if (index < 0) continue;
        rooms[index].x = m.toX;
        rooms[index].y = m.toY;
        relocated++;
    }
    return relocated;
}

/** Split a plan against the live list: full MapRoom copies for the fresh
 * rooms (adopting the plan's exit lists) plus before/after pairs for the
 * pre-existing rooms the plan touches. Nothing here mutates the live list. */
export function splitCreateTargets(plan: CreateRoomsPlan, rooms: MapRoom[]): {
    created: MapRoom[];
    affected: { x: number; y: number; before: MapExitEdit[]; after: MapExitEdit[] }[];
} {
    const freshKeys = new Set(plan.rooms.map((r) => key(r.x, r.y)));
    const replacements = new Map(plan.exits.map((e) => [key(e.x, e.y), e.exits]));
    const created = plan.rooms.map((r) => {
        const exits = replacements.get(key(r.x, r.y)) ?? [];
        const room: MapRoom = {
            x: r.x,
            y: r.y,
            exits: exits.map((e) => ({
                ...e,
                aliases: [...e.aliases],
                coord: [...e.coord] as [string, number, number, number],
            })),
        };
        if (r.name !== null) room.name = r.name;
        if (r.desc !== null) room.desc = r.desc;
        return room;
    });
    const affected: { x: number; y: number; before: MapExitEdit[]; after: MapExitEdit[] }[] = [];
    for (const room of rooms) {
        const k = key(room.x, room.y);
        if (freshKeys.has(k)) continue;
        const after = replacements.get(k);
        if (!after) continue;
        affected.push({
            x: room.x,
            y: room.y,
            before: room.exits.map((e) => ({
                ...e,
                aliases: [...e.aliases],
                coord: [...e.coord] as [string, number, number, number],
            })),
            after: after.map((e) => ({
                ...e,
                aliases: [...e.aliases],
                coord: [...e.coord] as [string, number, number, number],
            })),
        });
    }
    return { created, affected };
}

/** Remove every room on the given coords from the live list, returning the
 * removed rooms (deny rollback and undo share this path). */
export function removeRoomsByCoords(rooms: MapRoom[], coords: { x: number; y: number }[]): MapRoom[] {
    const wanted = new Set(coords.map((c) => key(c.x, c.y)));
    const removed: MapRoom[] = [];
    for (let i = rooms.length - 1; i >= 0; i--) {
        if (wanted.has(key(rooms[i].x, rooms[i].y))) removed.push(...rooms.splice(i, 1));
    }
    return removed;
}

/** Rebuild sendable payloads from a journal entry (redo re-sends the full
 * original plan; the server skips coords that already exist). */
export function buildCreateResend(entry: CreateJournalEntry): {
    rooms: NewRoomSpec[];
    exits: RoomExitsReplacement[];
} {
    return {
        rooms: entry.created.map((r) => ({ x: r.x, y: r.y, name: r.name ?? null, desc: r.desc ?? null })),
        exits: [
            ...entry.affected.map((a) => ({ x: a.x, y: a.y, exits: a.after })),
            ...entry.created.filter((r) => r.exits.length > 0).map((r) => ({ x: r.x, y: r.y, exits: r.exits })),
        ],
    };
}

/** Mirror the server default onto freshly acked rooms that still carry no
 * description, so the panel matches what the server stored. */
export function fillCreatedDefaults(rooms: MapRoom[], coords: { x: number; y: number }[], desc = 'New room.'): void {
    const wanted = new Set(coords.map((c) => key(c.x, c.y)));
    for (const room of rooms) {
        if (wanted.has(key(room.x, room.y)) && room.desc == null) room.desc = desc;
    }
}

/** Overlay cell key for a world coord (inverse of canvasToWorld in main). */
export function worldToCellKey(
    x: number,
    y: number,
    originX: number,
    originY: number,
    canvasHeight: number,
): string {
    return `${x - originX},${canvasHeight - 1 - (y - originY)}`;
}

/** Rebuild the room-overlay keys from the live room list. Incremental
 * add/delete can strand stale keys when the canvas geometry shifts
 * between ops; deriving the whole set from the current list cannot. */
export function buildRoomCellKeys(
    rooms: MapRoom[],
    originX: number,
    originY: number,
    canvasHeight: number,
): Set<string> {
    const keys = new Set<string>();
    for (const room of rooms) keys.add(worldToCellKey(room.x, room.y, originX, originY, canvasHeight));
    return keys;
}

/** One room queued for server-side deletion, with the occupant fallback
 * the server evacuates to (null when nothing survives: the server then
 * only deletes rooms that are already empty). */
export interface DeleteRoomEntry {
    x: number;
    y: number;
    fallback: { x: number; y: number } | null;
}

/** A surviving room whose exit list the server strips when its link
 * target is deleted: the before list restores on undo/deny rollback. */
export interface StrippedSurvivor {
    x: number;
    y: number;
    before: MapExitEdit[];
    after: MapExitEdit[];
}

const cloneExitList = (exits: MapExitEdit[]): MapExitEdit[] =>
    exits.map((e) => ({ ...e, aliases: [...e.aliases], coord: [...e.coord] as [string, number, number, number] }));

/** Find surviving rooms with exits into the removed coords and compute
 * their stripped lists (mirrors the server auto-strip, full area/z
 * qualified). Nothing here mutates the live list. */
export function computeStrippedSurvivors(
    removed: { x: number; y: number }[],
    rooms: MapRoom[],
    area: string,
    z: number,
): StrippedSurvivor[] {
    const gone = new Set(removed.map((r) => key(r.x, r.y)));
    const targets = (e: MapExitEdit): boolean =>
        e.coord[0] === area && e.coord[3] === z && gone.has(key(e.coord[1], e.coord[2]));
    const stripped: StrippedSurvivor[] = [];
    for (const room of rooms) {
        if (gone.has(key(room.x, room.y))) continue;
        if (!room.exits.some(targets)) continue;
        stripped.push({
            x: room.x,
            y: room.y,
            before: cloneExitList(room.exits),
            after: cloneExitList(room.exits.filter((e) => !targets(e))),
        });
    }
    return stripped;
}

/** Undo/redo journal entry for one applied direct delete. Stripped
 * survivors carry before/after pairs; rooms match by coords at replay
 * because list indexes shift as rooms come and go. */
export interface DeleteJournalEntry {
    depth: number;
    removed: MapRoom[];
    stripped: StrippedSurvivor[];
    deletes: DeleteRoomEntry[];
}

/** Rebuild sendable payloads that restore a deleted batch (undo of a
 * direct delete): the removed rooms with their full exit lists plus the
 * stripped survivors' before lists. Queue the create first — a before
 * list may target a re-created room. */
export function buildDeleteUndoResend(entry: DeleteJournalEntry): {
    rooms: NewRoomSpec[];
    exits: RoomExitsReplacement[];
} {
    return {
        rooms: entry.removed.map((r) => ({ x: r.x, y: r.y, name: r.name ?? null, desc: r.desc ?? null })),
        exits: [
            ...entry.removed.filter((r) => r.exits.length > 0).map((r) => ({ x: r.x, y: r.y, exits: r.exits })),
            ...entry.stripped.map((s) => ({ x: s.x, y: s.y, exits: s.before })),
        ],
    };
}

/** Build server-side delete entries for removed rooms: each room's
 * fallback is its nearest surviving room (Manhattan distance, ties keep
 * survivor order). Callers pass the post-removal room list as survivors;
 * inputs are only read, never mutated. */
export function buildDeleteEntries(
    removed: { x: number; y: number }[],
    survivors: { x: number; y: number }[],
): DeleteRoomEntry[] {
    return removed.map((r) => {
        let best: { x: number; y: number } | null = null;
        let bestDist = 0;
        for (const s of survivors) {
            const dist = Math.abs(s.x - r.x) + Math.abs(s.y - r.y);
            if (best === null || dist < bestDist) {
                best = s;
                bestDist = dist;
            }
        }
        return { x: r.x, y: r.y, fallback: best === null ? null : { x: best.x, y: best.y } };
    });
}
