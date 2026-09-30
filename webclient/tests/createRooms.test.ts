import { describe, expect, it } from 'vitest';
import { CanvasState } from '../src/state/CanvasState';
import {
    planCreateRooms,
    splitCreateTargets,
    removeRoomsByCoords,
    buildCreateResend,
    buildDeleteEntries,
    computeStrippedSurvivors,
    buildDeleteUndoResend,
    buildRoomCellKeys,
    applyAcceptedMoves,
    restoreDeniedSquares,
    formatMovesDeniedMessage,
    movesEqual,
    fillCreatedDefaults,
    worldToCellKey,
    findRoomIndex,
    cloneRoom,
} from '../src/createRooms';
import { MapRoom } from '../src/mapedit';

function room(x: number, y: number, exits: MapRoom['exits'] = []): MapRoom {
    return { x, y, exits };
}

describe('planCreateRooms', () => {
    it('returns null for an empty selection', () => {
        expect(planCreateRooms([], [], 'TestArea', 0)).toBeNull();
    });

    it('returns null when every selected square is already a room', () => {
        const existing = [room(2, 3)];
        expect(planCreateRooms([{ x: 2, y: 3 }], existing, 'TestArea', 0)).toBeNull();
    });

    it('links an adjacent pair both ways with cardinal names and aliases', () => {
        const plan = planCreateRooms([{ x: 0, y: 0 }, { x: 1, y: 0 }], [], 'TestArea', 0);
        expect(plan?.rooms).toEqual([
            { x: 0, y: 0, name: null, desc: null },
            { x: 1, y: 0, name: null, desc: null },
        ]);
        expect(plan?.exits).toEqual([
            { x: 0, y: 0, exits: [{ name: 'east', aliases: ['e'], coord: ['TestArea', 1, 0, 0] }] },
            { x: 1, y: 0, exits: [{ name: 'west', aliases: ['w'], coord: ['TestArea', 0, 0, 0] }] },
        ]);
    });

    it('chains a row of three with middle-room links both ways', () => {
        const plan = planCreateRooms([{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 0 }], [], 'TestArea', 0);
        expect(plan?.rooms).toHaveLength(3);
        const middle = plan?.exits.find((e) => e.x === 1);
        expect(middle?.exits.map((e) => e.name)).toEqual(['east', 'west']);
    });

    it('links a new room to a pre-existing neighbor and adds the reciprocal exit', () => {
        const existing = [room(5, 5)];
        const plan = planCreateRooms([{ x: 5, y: 4 }], existing, 'TestArea', 0);
        expect(plan?.rooms).toEqual([{ x: 5, y: 4, name: null, desc: null }]);
        expect(plan?.exits).toEqual([
            { x: 5, y: 4, exits: [{ name: 'north', aliases: ['n'], coord: ['TestArea', 5, 5, 0] }] },
            { x: 5, y: 5, exits: [{ name: 'south', aliases: ['s'], coord: ['TestArea', 5, 4, 0] }] },
        ]);
    });

    it('keeps an isolated new room out of the exits list', () => {
        const plan = planCreateRooms([{ x: 9, y: 9 }], [], 'TestArea', 0);
        expect(plan?.rooms).toEqual([{ x: 9, y: 9, name: null, desc: null }]);
        expect(plan?.exits).toEqual([]);
    });

    it('never overwrites an existing exit name, but still links back under a free name', () => {
        const existing = [room(1, 0, [{ name: 'East', aliases: [], coord: ['Elsewhere', 7, 7, 0] }])];
        const plan = planCreateRooms([{ x: 0, y: 0 }], existing, 'TestArea', 0);
        // The pre-existing 'East' exit is untouched; the fresh room still
        // links east to its neighbor, which links back under free 'west'.
        expect(plan?.exits).toEqual([
            { x: 0, y: 0, exits: [{ name: 'east', aliases: ['e'], coord: ['TestArea', 1, 0, 0] }] },
            {
                x: 1, y: 0,
                exits: [
                    { name: 'East', aliases: [], coord: ['Elsewhere', 7, 7, 0] },
                    { name: 'west', aliases: ['w'], coord: ['TestArea', 0, 0, 0] },
                ],
            },
        ]);
    });

    it('skips a direction whose name already exists on the pre-existing room', () => {
        const existing = [room(5, 5, [{ name: 'south', aliases: ['s'], coord: ['TestArea', 5, 0, 0] }])];
        const plan = planCreateRooms([{ x: 5, y: 4 }], existing, 'TestArea', 0);
        // The fresh room still links north; the neighbor keeps its own south.
        expect(plan?.exits).toEqual([
            { x: 5, y: 4, exits: [{ name: 'north', aliases: ['n'], coord: ['TestArea', 5, 5, 0] }] },
        ]);
    });

    it('adds no second exit when the neighbor is already linked by another name', () => {
        const existing = [room(5, 5, [{ name: 'door', aliases: [], coord: ['TestArea', 5, 4, 0] }])];
        const plan = planCreateRooms([{ x: 5, y: 4 }], existing, 'TestArea', 0);
        // Fresh room links north, but the neighbor already reaches it via
        // 'door', so no reciprocal south exit is added.
        expect(plan?.exits).toEqual([
            { x: 5, y: 4, exits: [{ name: 'north', aliases: ['n'], coord: ['TestArea', 5, 5, 0] }] },
        ]);
    });

    it('does not touch links between two pre-existing rooms', () => {
        const existing = [room(0, 0), room(1, 0)];
        const plan = planCreateRooms([{ x: 9, y: 9 }], existing, 'TestArea', 0);
        expect(plan?.rooms).toEqual([{ x: 9, y: 9, name: null, desc: null }]);
        expect(plan?.exits).toEqual([]);
    });

    it('dedupes repeated coords and skips non-integers', () => {
        const plan = planCreateRooms(
            [{ x: 0, y: 0 }, { x: 0, y: 0 }, { x: 1.5, y: 0 }, { x: 1, y: 0 }],
            [],
            'TestArea',
            0,
        );
        expect(plan?.rooms).toEqual([
            { x: 0, y: 0, name: null, desc: null },
            { x: 1, y: 0, name: null, desc: null },
        ]);
    });

    it('carries the map area and z into every exit coord', () => {
        const plan = planCreateRooms([{ x: 3, y: 3 }, { x: 3, y: 4 }], [], 'Deep', 2);
        expect(plan?.exits[0].exits[0].coord).toEqual(['Deep', 3, 4, 2]);
        expect(plan?.exits[1].exits[0].coord).toEqual(['Deep', 3, 3, 2]);
    });

    it('never mutates the caller rooms or their exit lists', () => {
        const existing = [room(5, 5, [{ name: 'up', aliases: ['u'], coord: ['TestArea', 5, 5, 1] }])];
        planCreateRooms([{ x: 5, y: 4 }], existing, 'TestArea', 0);
        expect(existing[0].exits).toEqual([{ name: 'up', aliases: ['u'], coord: ['TestArea', 5, 5, 1] }]);
    });
});

describe('splitCreateTargets', () => {
    it('builds room copies adopting the planned exit lists', () => {
        const plan = planCreateRooms([{ x: 0, y: 0 }, { x: 1, y: 0 }], [], 'TestArea', 0)!;
        const { created, affected } = splitCreateTargets(plan, []);
        expect(created).toHaveLength(2);
        expect(created[0].exits.map((e) => e.name)).toEqual(['east']);
        expect(created[1].exits.map((e) => e.name)).toEqual(['west']);
        expect(affected).toEqual([]);
    });

    it('pairs pre-existing rooms with before/after exit lists', () => {
        const existing = [room(5, 5)];
        const plan = planCreateRooms([{ x: 5, y: 4 }], existing, 'TestArea', 0)!;
        const { created, affected } = splitCreateTargets(plan, existing);
        expect(created.map((r) => [r.x, r.y])).toEqual([[5, 4]]);
        expect(affected).toHaveLength(1);
        expect(affected[0].before).toEqual([]);
        expect(affected[0].after.map((e) => e.name)).toEqual(['south']);
    });

    it('never mutates the live list when the copies change', () => {
        const existing = [room(5, 5, [{ name: 'up', aliases: ['u'], coord: ['TestArea', 5, 5, 1] }])];
        const plan = planCreateRooms([{ x: 5, y: 4 }], existing, 'TestArea', 0)!;
        const { created, affected } = splitCreateTargets(plan, existing);
        created[0].exits.push({ name: 'hack', aliases: [], coord: ['TestArea', 9, 9, 0] });
        affected[0].after.pop();
        affected[0].before.push({ name: 'hack', aliases: [], coord: ['TestArea', 9, 9, 0] });
        expect(existing).toHaveLength(1);
        expect(existing[0].exits).toEqual([{ name: 'up', aliases: ['u'], coord: ['TestArea', 5, 5, 1] }]);
    });

    it('carries non-null plan names and descriptions onto the copies', () => {
        const plan = {
            rooms: [{ x: 1, y: 2, name: 'Gate', desc: 'A gate.' }],
            exits: [],
        };
        const { created } = splitCreateTargets(plan, []);
        expect(created[0].name).toBe('Gate');
        expect(created[0].desc).toBe('A gate.');
    });
});

describe('create rollback and resend helpers', () => {
    it('removeRoomsByCoords removes and returns the matching rooms', () => {
        const rooms = [room(0, 0), room(1, 0), room(2, 0)];
        const removed = removeRoomsByCoords(rooms, [{ x: 0, y: 0 }, { x: 2, y: 0 }]);
        expect(rooms.map((r) => r.x)).toEqual([1]);
        expect(removed.map((r) => r.x).sort()).toEqual([0, 2]);
    });

    it('removeRoomsByCoords ignores unknown coords', () => {
        const rooms = [room(0, 0)];
        expect(removeRoomsByCoords(rooms, [{ x: 9, y: 9 }])).toEqual([]);
        expect(rooms).toHaveLength(1);
    });

    it('findRoomIndex returns -1 for a missing room', () => {
        expect(findRoomIndex([room(0, 0)], 4, 4)).toBe(-1);
        expect(findRoomIndex([room(0, 0)], 0, 0)).toBe(0);
    });

    it('cloneRoom deep-copies exits', () => {
        const original = room(0, 0, [{ name: 'east', aliases: ['e'], coord: ['TestArea', 1, 0, 0] }]);
        const copy = cloneRoom(original);
        copy.exits[0].aliases.push('E');
        expect(original.exits[0].aliases).toEqual(['e']);
    });

    it('buildDeleteEntries picks the nearest survivor per room', () => {
        expect(buildDeleteEntries(
            [{ x: 0, y: 0 }, { x: 10, y: 0 }],
            [{ x: 1, y: 0 }, { x: 9, y: 0 }],
        )).toEqual([
            { x: 0, y: 0, fallback: { x: 1, y: 0 } },
            { x: 10, y: 0, fallback: { x: 9, y: 0 } },
        ]);
    });

    it('buildDeleteEntries keeps survivor order on a distance tie', () => {
        expect(buildDeleteEntries(
            [{ x: 0, y: 0 }],
            [{ x: 1, y: 0 }, { x: -1, y: 0 }],
        )).toEqual([{ x: 0, y: 0, fallback: { x: 1, y: 0 } }]);
    });

    it('buildDeleteEntries yields a null fallback when nothing survives', () => {
        expect(buildDeleteEntries([{ x: 0, y: 0 }], [])).toEqual([{ x: 0, y: 0, fallback: null }]);
    });

    it('buildDeleteEntries copies fallback coords and leaves inputs alone', () => {
        const removed = [{ x: 0, y: 0 }];
        const survivors = [{ x: 3, y: 4 }];
        const entries = buildDeleteEntries(removed, survivors);
        expect(entries[0].fallback).toEqual({ x: 3, y: 4 });
        expect(entries[0].fallback).not.toBe(survivors[0]);
        expect(removed).toEqual([{ x: 0, y: 0 }]);
        expect(survivors).toEqual([{ x: 3, y: 4 }]);
    });

    it('buildCreateResend rebuilds rooms plus affected and created exits', () => {
        const plan = planCreateRooms([{ x: 0, y: 0 }, { x: 1, y: 0 }], [room(0, 1)], 'TestArea', 0)!;
        const live = [room(0, 1)];
        const { created, affected } = splitCreateTargets(plan, live);
        const resend = buildCreateResend({ depth: 1, created, affected });
        expect(resend.rooms).toEqual([
            { x: 0, y: 0, name: null, desc: null },
            { x: 1, y: 0, name: null, desc: null },
        ]);
        // Affected afters first, then the created rooms' own link lists.
        expect(resend.exits.map((e) => [e.x, e.y])).toEqual([[0, 1], [0, 0], [1, 0]]);
        expect(resend.exits[0].exits.map((e) => e.name)).toEqual(['south']);
    });

    it('fillCreatedDefaults only fills missing descriptions on listed coords', () => {
        const rooms = [room(0, 0), { x: 1, y: 0, desc: 'Kept.', exits: [] }, room(9, 9)];
        fillCreatedDefaults(rooms, [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 9, y: 9 }]);
        expect(rooms[0].desc).toBe('New room.');
        expect(rooms[1].desc).toBe('Kept.');
        expect(rooms[2].desc).toBe('New room.');
    });

    it('worldToCellKey inverts the canvas mapping', () => {
        // canvasToWorld(col, row) = col + originX, height - 1 - row + originY.
        expect(worldToCellKey(5, 7, 5, 7, 24)).toBe('0,23');
        expect(worldToCellKey(6, 7, 5, 7, 24)).toBe('1,23');
        expect(worldToCellKey(5, 8, 5, 7, 24)).toBe('0,22');
    });

    it('computeStrippedSurvivors pairs survivors linking into removed coords', () => {
        const rooms = [
            room(0, 0, [{ name: 'east', aliases: ['e'], coord: ['TestArea', 1, 0, 0] }]),
            room(1, 0, [{ name: 'west', aliases: ['w'], coord: ['TestArea', 0, 0, 0] }]),
            room(2, 0),
        ];
        const stripped = computeStrippedSurvivors([{ x: 1, y: 0 }], rooms, 'TestArea', 0);
        expect(stripped).toHaveLength(1);
        expect(stripped[0].x).toBe(0);
        expect(stripped[0].before.map((e) => e.name)).toEqual(['east']);
        expect(stripped[0].after).toEqual([]);
    });

    it('computeStrippedSurvivors keeps exits that point elsewhere', () => {
        const rooms = [
            room(0, 0, [
                { name: 'east', aliases: ['e'], coord: ['TestArea', 1, 0, 0] },
                { name: 'north', aliases: ['n'], coord: ['TestArea', 0, 1, 0] },
            ]),
        ];
        const stripped = computeStrippedSurvivors([{ x: 1, y: 0 }], rooms, 'TestArea', 0);
        expect(stripped).toHaveLength(1);
        expect(stripped[0].after.map((e) => e.name)).toEqual(['north']);
        expect(stripped[0].before).toHaveLength(2);
    });

    it('computeStrippedSurvivors ignores other areas and depths', () => {
        const rooms = [
            room(0, 0, [
                { name: 'elsewhere', aliases: [], coord: ['OtherArea', 1, 0, 0] },
                { name: 'upstairs', aliases: [], coord: ['TestArea', 1, 0, 1] },
            ]),
        ];
        expect(computeStrippedSurvivors([{ x: 1, y: 0 }], rooms, 'TestArea', 0)).toEqual([]);
    });

    it('computeStrippedSurvivors skips removed rooms and clones the lists', () => {
        const exits = [{ name: 'west', aliases: ['w'], coord: ['TestArea', 0, 0, 0] as [string, number, number, number] }];
        const rooms = [room(0, 0), room(1, 0, exits)];
        const stripped = computeStrippedSurvivors([{ x: 0, y: 0 }, { x: 1, y: 0 }], rooms, 'TestArea', 0);
        expect(stripped).toEqual([]);
        const kept = computeStrippedSurvivors([{ x: 0, y: 0 }], rooms, 'TestArea', 0);
        expect(kept).toHaveLength(1);
        expect(kept[0].before).not.toBe(exits);
        expect(kept[0].before[0].aliases).not.toBe(exits[0].aliases);
    });

    it('buildRoomCellKeys derives exactly the live rooms keys', () => {
        const rooms = [room(5, 7), room(6, 8)];
        const keys = buildRoomCellKeys(rooms, 5, 7, 24);
        expect(keys).toEqual(new Set(['0,23', '1,22']));
        // A deleted room leaves no key behind, whatever the old set held.
        const stale = new Set(keys).add('9,9');
        expect(stale.has('9,9')).toBe(true);
        expect(buildRoomCellKeys([room(5, 7)], 5, 7, 24).has('1,22')).toBe(false);
    });

    it('buildDeleteUndoResend restores removed rooms plus stripped befores', () => {
        const removed = [
            { x: 0, y: 0, name: 'Gone', desc: 'Was here.', exits: [] },
            room(1, 0, [{ name: 'west', aliases: ['w'], coord: ['TestArea', 0, 0, 0] }]),
        ];
        const stripped = [{
            x: 2, y: 0,
            before: [{ name: 'west', aliases: ['w'], coord: ['TestArea', 1, 0, 0] as [string, number, number, number] }],
            after: [],
        }];
        const deletes = [{ x: 0, y: 0, fallback: { x: 2, y: 0 } }, { x: 1, y: 0, fallback: { x: 2, y: 0 } }];
        const resend = buildDeleteUndoResend({ depth: 3, removed, stripped, deletes });
        expect(resend.rooms).toEqual([
            { x: 0, y: 0, name: 'Gone', desc: 'Was here.' },
            { x: 1, y: 0, name: null, desc: null },
        ]);
        // Removed rooms' own lists first, then the stripped befores.
        expect(resend.exits.map((e) => [e.x, e.y])).toEqual([[1, 0], [2, 0]]);
        expect(resend.exits[1].exits.map((e) => e.name)).toEqual(['west']);
    });
});

describe('applyAcceptedMoves', () => {
    it('relocates rooms by move identity and reports the count', () => {
        const rooms = [room(3, 3), room(10, 10)];
        const relocated = applyAcceptedMoves(rooms, [{ fromX: 3, fromY: 3, toX: 4, toY: 3 }]);
        expect(relocated).toBe(1);
        expect(rooms).toEqual([room(4, 3), room(10, 10)]);
    });

    it('leaves untracked rooms alone when the batch matches in length but not membership', () => {
        // The old positional rewrite assigned coords[i] blindly here and
        // scrambled the list, stranding teal on cells the list forgot.
        const rooms = [room(0, 0), room(1, 0), room(9, 9)];
        const relocated = applyAcceptedMoves(rooms, [{ fromX: 0, fromY: 0, toX: 5, toY: 5 }]);
        expect(relocated).toBe(1);
        expect(rooms.map((r) => [r.x, r.y])).toEqual([[5, 5], [1, 0], [9, 9]]);
    });

    it('skips moves starting from unknown coords and preserves order', () => {
        const rooms = [room(2, 2), room(7, 7)];
        const relocated = applyAcceptedMoves(rooms, [
            { fromX: 99, fromY: 99, toX: 0, toY: 0 },
            { fromX: 7, fromY: 7, toX: 8, toY: 7 },
        ]);
        expect(relocated).toBe(1);
        expect(rooms.map((r) => [r.x, r.y])).toEqual([[2, 2], [8, 7]]);
    });
});

describe('restoreDeniedSquares', () => {
    function glyph(char: string) {
        return { char, fg: [204, 204, 204] as [number, number, number], bg: [-1, -1, -1] as [number, number, number] };
    }

    // Origin (0,0), height 10: world (x,y) -> viewport (x, 9-y).
    // Move (2,2)->(5,5): from-square (2,7), to-square (5,4).
    function preMoveSnapshot(): CanvasState {
        const s = new CanvasState(10, 10, false);
        s.setCell(2, 7, glyph('R'));
        s.setCell(5, 4, glyph('.'));
        return s;
    }

    function postMoveLive(): CanvasState {
        const live = new CanvasState(10, 10, false);
        live.setCell(2, 7, glyph(''));
        live.setCell(5, 4, glyph('R'));
        // Later stroke elsewhere (must survive the restore).
        live.setCell(0, 0, glyph('X'));
        return live;
    }

    const move = { fromX: 2, fromY: 2, toX: 5, toY: 5 };

    it('restores denied squares from the snapshot and keeps later strokes', () => {
        const live = postMoveLive();
        const batch = restoreDeniedSquares(live, preMoveSnapshot(), [move], 0, 0);
        expect(batch).toHaveLength(2);
        live.applyBatch(batch);
        expect(live.getCell(2, 7)?.char).toBe('R');
        expect(live.getCell(5, 4)?.char).toBe('.');
        expect(live.getCell(0, 0)?.char).toBe('X');
    });

    it('restores each square once when moves share it', () => {
        const live = postMoveLive();
        const batch = restoreDeniedSquares(live, preMoveSnapshot(), [
            move,
            { fromX: 5, fromY: 5, toX: 8, toY: 8 },
        ], 0, 0);
        const keys = batch.map((u) => `${u.col},${u.row}`);
        expect(new Set(keys).size).toBe(keys.length);
        // (5,4) shared by both moves: single entry.
        expect(keys.filter((k) => k === '5,4')).toHaveLength(1);
    });

    it('maps world squares with the current origin (growth rebase safe)', () => {
        // Same squares as world (12,-3)->(15,1) under origin (10,-5).
        const live = postMoveLive();
        const batch = restoreDeniedSquares(live, preMoveSnapshot(), [
            { fromX: 12, fromY: -3, toX: 15, toY: 1 },
        ], 10, -5);
        expect(batch.map((u) => [u.col, u.row])).toEqual([[2, 7], [5, 3]]);
    });

    it('restores a square the snapshot never stored as cleared', () => {
        const live = new CanvasState(10, 10, false);
        const snapshot = new CanvasState(4, 4, false);
        // World (8,8) -> viewport (8,1): outside the 4x4 snapshot storage.
        const batch = restoreDeniedSquares(live, snapshot, [
            { fromX: 8, fromY: 8, toX: 8, toY: 8 },
        ], 0, 0);
        expect(batch).toHaveLength(1);
        expect(batch[0]).toMatchObject({
            col: 8, row: 1,
            cell: { char: '', fg: [204, 204, 204], bg: [-1, -1, -1] },
        });
    });

    it('returns no batch for no moves', () => {
        const live = postMoveLive();
        expect(restoreDeniedSquares(live, preMoveSnapshot(), [], 0, 0)).toEqual([]);
    });
});

describe('formatMovesDeniedMessage', () => {
    it('names a single room without asserting a cause', () => {
        const text = formatMovesDeniedMessage([{ fromX: 0, fromY: 0, toX: 3, toY: 4 }]);
        expect(text).toContain('a room');
        expect(text).toContain('(3, 4)');
        expect(text).not.toContain('occupied');
        expect(text).not.toContain('reverted');
    });

    it('lists up to five destinations then counts the rest', () => {
        const moves = Array.from({ length: 7 }, (_, i) => ({ fromX: i, fromY: 0, toX: i, toY: 1 }));
        const text = formatMovesDeniedMessage(moves);
        expect(text).toContain('7 rooms');
        expect(text).toContain('(4, 1)');
        expect(text).not.toContain('(5, 1)');
        expect(text).toContain('and 2 more');
    });
});

describe('movesEqual', () => {
    it('matches identical batches by value, not identity', () => {
        const a = [{ fromX: 1, fromY: 2, toX: 3, toY: 4 }];
        const b = [{ fromX: 1, fromY: 2, toX: 3, toY: 4 }];
        expect(movesEqual(a, b)).toBe(true);
        expect(movesEqual(a, [])).toBe(false);
        expect(movesEqual(a, [{ fromX: 1, fromY: 2, toX: 3, toY: 5 }])).toBe(false);
        expect(movesEqual(a, [...b, ...b])).toBe(false);
    });
});
