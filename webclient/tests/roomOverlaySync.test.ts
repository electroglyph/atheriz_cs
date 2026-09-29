// @ts-nocheck
// Regression pins for the room-overlay highlight: the canvas paints teal for
// exactly the keys in roomCells, so every path that mutates the rooms list
// must rebuild the overlay from that list. main.ts runs top-level DOM code on
// import and cannot load in a test, so the wiring half of this file pins the
// source structure (?raw, never executed) while the behavior half drives the
// same pure-helper sequence the handlers use (remove then rebuild).
import { describe, expect, it } from 'vitest';
import mainSrc from '../src/main.ts?raw';
import {
    buildRoomCellKeys,
    removeRoomsByCoords,
} from '../src/createRooms.ts';

function syncCalls(): string[] {
    return mainSrc.match(/^[ \t]*syncRoomCells\(\);/gm) ?? [];
}

describe('room overlay sync wiring', () => {
    it('rebuilds the overlay from the live rooms list', () => {
        expect(mainSrc).toContain('function syncRoomCells(): void');
        expect(mainSrc).toContain('buildRoomCellKeys(rooms');
    });

    it('every room-mutating path re-syncs (delete click included)', () => {
        // apply/undo/redo create, undo/redo delete, delete click, both
        // deny rollbacks, moves_accepted relocation, and moves_denied
        // revert-to-list. If a new mutating path is added it must call
        // syncRoomCells too, and this floor catches a removed call.
        expect(syncCalls().length).toBeGreaterThanOrEqual(10);
    });

    it('delete click removes before it re-syncs', () => {
        expect(mainSrc).toMatch(
            /removeRoomsByCoords\(rooms, targets\);\s*\n\s*syncRoomCells\(\);/,
        );
    });

    it('moves_accepted relocates by identity and re-syncs the overlay', () => {
        expect(mainSrc).toContain('applyAcceptedMoves(rooms, event.moves)');
        const branch = mainSrc.slice(
            mainSrc.indexOf("event.type === 'moves_accepted'"),
            mainSrc.indexOf("event.type === 'moves_denied'"),
        );
        expect(branch).toContain('syncRoomCells()');
        // The old positional rewrite assigned coords[i] blindly whenever
        // the two lists merely matched in length.
        expect(mainSrc).not.toContain('rooms.forEach((room, i)');
        expect(mainSrc).not.toContain('currentRoomCoords');
    });

    it('no world-key overlay helpers remain', () => {
        expect(mainSrc).not.toContain('pruneCreateCells');
        expect(mainSrc).not.toContain('worldToCellKey');
    });

    it('moves_denied rebuilds from the list instead of reverting keys', () => {
        const start = mainSrc.indexOf("event.type === 'moves_denied'");
        const dialog = mainSrc.indexOf('moveDeniedDialog.show(', start);
        const branch = mainSrc.slice(start, dialog);
        expect(branch).toContain('syncRoomCells()');
        // The old positional revert swapped keys across the async gap and
        // could plant teal on squares whose rooms were deleted meanwhile.
        expect(mainSrc).not.toContain('roomCellSet.add(`${fromCol},${fromRow}`)');
    });

    it('only the canvas-move key remaps touch the overlay incrementally', () => {
        // Only the optimistic onCellsMoved remap uses canvas-local keys;
        // every room-list mutation and every deny rebuilds via
        // syncRoomCells instead. Any new incremental update fails count.
        const incremental =
            mainSrc.match(/roomCellSet\.(add|delete)\(/g) ?? [];
        expect(incremental.length).toBe(2);
    });
});

describe('room overlay sync behavior', () => {
    const origin = { x: 10, y: 10, height: 20 };

    function roomsAt(coords: [number, number][]) {
        return coords.map(([x, y]) => ({ x, y, exits: [] }));
    }

    it('deleted coords leave the overlay', () => {
        const rooms = roomsAt([[4, 5], [4, 6]]);
        const removed = removeRoomsByCoords(rooms, [{ x: 4, y: 5 }]);
        expect(removed.map((r) => [r.x, r.y])).toEqual([[4, 5]]);
        const cells = buildRoomCellKeys(rooms, origin.x, origin.y, origin.height);
        expect(cells.size).toBe(1);
        for (const key of cells) {
            const [col, row] = key.split(',').map(Number);
            expect([col, row]).not.toEqual([4 - origin.x, origin.height - 1 - (5 - origin.y)]);
        }
    });

    it('empty rooms list means empty overlay', () => {
        const rooms = roomsAt([[4, 5]]);
        removeRoomsByCoords(rooms, [{ x: 4, y: 5 }]);
        expect(buildRoomCellKeys(rooms, origin.x, origin.y, origin.height).size).toBe(0);
    });
});
