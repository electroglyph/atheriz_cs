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
    applyAcceptedMoves,
    buildRoomCellKeys,
    removeRoomsByCoords,
    revertAcceptedMoves,
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

    it('delete click confirms the count (interior deletes are canvas-invisible)', () => {
        // Neighbors' shared outlines repaint the same pixels, so deleting
        // an interior room changes nothing visible; the status line is the
        // confirmation. The ack/deny overwrite it with the outcome.
        expect(mainSrc).toMatch(/roomEditor\.setStatus\(`Deleted /);
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
        expect(branch).toContain('revertOptimisticMoves(event.moves)');
        // The old positional revert swapped keys across the async gap and
        // could plant teal on squares whose rooms were deleted meanwhile.
        expect(mainSrc).not.toContain('roomCellSet.add(`${fromCol},${fromRow}`)');
    });

    it('a canvas move shifts the room list and the overlay together', () => {
        // The overlay key and the list entry must move in the same handler.
        // Shifting only the overlay left the two on different squares
        // whenever the validation never landed, and the next rebuild
        // painted the highlight back on the old square.
        const branch = mainSrc.slice(
            mainSrc.indexOf('context.onCellsMoved ='),
            mainSrc.indexOf('mapEditSession.validateRoomMoves'),
        );
        expect(branch).toContain('applyAcceptedMoves(rooms, worldMoves)');
        expect(branch).toContain('syncRoomCells()');
        expect(branch).not.toContain('roomCellSet');
        // Order matters: the list moves first, then the overlay rebuilds.
        expect(branch.indexOf('applyAcceptedMoves'))
            .toBeLessThan(branch.indexOf('syncRoomCells'));
    });

    it('moves_denied reverts the list then rebuilds the overlay', () => {
        const start = mainSrc.indexOf('const revertOptimisticMoves');
        const end = mainSrc.indexOf('mapEditSession?.onEvent', start);
        const helper = mainSrc.slice(start, end);
        expect(helper).toContain('revertAcceptedMoves(rooms, moves)');
        expect(helper.indexOf('revertAcceptedMoves'))
            .toBeLessThan(helper.indexOf('syncRoomCells'));
    });

    it('no handler updates the overlay keys by hand', () => {
        // Every room change rebuilds from the list; a hand-written key
        // swap is what stranded highlights on squares with no room.
        const incremental =
            mainSrc.match(/roomCellSet\.(add|delete)\(/g) ?? [];
        expect(incremental.length).toBe(0);
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

    it('a move that is never acked keeps list and overlay aligned', () => {
        // The reported bug: a room at (4,5) moves, the validation is
        // dropped, the user deletes it. With the list moved at send time
        // the delete removes the right entry and the rebuild clears the
        // highlight; with an overlay-only shift the list still named the
        // old square and the highlight came back on every later rebuild.
        const rooms = roomsAt([[4, 5], [7, 9]]);
        const moves = [{ fromX: 4, fromY: 5, toX: 4, toY: 6 }];
        applyAcceptedMoves(rooms, moves);
        expect(rooms.map((r) => [r.x, r.y])).toEqual([[4, 6], [7, 9]]);
        // Deleting the square the user clicked, with no ack in between.
        removeRoomsByCoords(rooms, [{ x: 4, y: 6 }]);
        const cells = buildRoomCellKeys(rooms, origin.x, origin.y, origin.height);
        expect(cells.size).toBe(1);
    });

    it('a denied move puts the room back where it started', () => {
        const rooms = roomsAt([[4, 5], [4, 6]]);
        const moves = [{ fromX: 4, fromY: 5, toX: 4, toY: 6 }];
        applyAcceptedMoves(rooms, moves);
        revertAcceptedMoves(rooms, moves);
        expect(rooms.map((r) => [r.x, r.y])).toEqual([[4, 5], [4, 6]]);
    });

    it('a deny that lands after a delete does not resurrect the square', () => {
        // Move sent, room deleted before the deny arrives. The deny must
        // not re-add a room for the deleted square.
        const rooms = roomsAt([[4, 5]]);
        const moves = [{ fromX: 4, fromY: 5, toX: 6, toY: 6 }];
        applyAcceptedMoves(rooms, moves);
        removeRoomsByCoords(rooms, [{ x: 6, y: 6 }]);
        revertAcceptedMoves(rooms, moves);
        expect(rooms).toEqual([]);
        expect(buildRoomCellKeys(rooms, origin.x, origin.y, origin.height).size).toBe(0);
    });
});
