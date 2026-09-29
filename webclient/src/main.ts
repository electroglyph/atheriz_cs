import { measureCellMetrics } from './utils/fontMetrics';
import { CanvasState } from './state/CanvasState';
import { UndoStack } from './state/UndoStack';
import { GridRenderer } from './canvas/GridRenderer';
import { beginNewCanvas } from './canvas/newCanvas';
import { CanvasController } from './canvas/CanvasController';
import { ToolManager } from './tools/ToolManager';
import { AppState, Color } from './types';
import { RectangleTool } from './tools/RectangleTool';
import { OvalTool } from './tools/OvalTool';
import { LineTool } from './tools/LineTool';
import { TextTool } from './tools/TextTool';
import { TypeTool } from './tools/TypeTool';
import { ToolContext, Tool } from './tools/Tool';
import { GradientTool } from './tools/GradientTool';
import { FillTool } from './tools/FillTool';
import { SelectionTool } from './tools/SelectionTool';
import { EyedropperTool } from './tools/EyedropperTool';
import { MoveTool } from './tools/MoveTool';
import { MessageDialog } from './ui/MessageDialog';
import { ConfirmDialog } from './ui/ConfirmDialog';
import { RotateTool } from './tools/RotateTool';

import { CharPalette } from './ui/CharPalette';
import { ColorPicker } from './ui/ColorPicker';
import { ColorPickerModal } from './ui/ColorPickerModal';
import { cssColor } from './utils/colors';
import { Toolbar } from './ui/Toolbar';
import { SidebarResizer } from './ui/SidebarResizer';
import { NewCanvasDialog } from './ui/NewCanvasDialog';
import { ResizeCanvasDialog } from './ui/ResizeCanvasDialog';
import { ImageImportDialog } from './ui/ImageImportDialog';
import { TextToolDialog } from './ui/TextToolDialog';
import { ColorAdjustDialog } from './ui/ColorAdjustDialog';
import { applyColorAdjustments, ColorAdjustOptions } from './utils/colors';
import { convertImageToAnsi } from './utils/imageLoader';
import { parseAnsiToCells, parseAnsiToState, detectAnsiDimensions } from './utils/ansiParser';
import { AnsiExporter } from './export/AnsiExporter';
import { CharMapDialog } from './ui/CharMapDialog';
import { LayerManager } from './ui/LayerManager';
import { RoomEditor } from './ui/RoomEditor';
import { PreviewWindow } from './ui/PreviewWindow';
import { GradientPicker } from './ui/GradientPicker';
import { readDrawGrant, clearDrawGrant } from './webclient/launch';
import { loadMapPayload, MapEditSession, MapEditPayload, MapEditOrigin, MapLegendEntry, MapRoom, MapExitEdit, logRoomData } from './mapedit';
import { buildEditorSettings } from './editorSettings';
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
    fillCreatedDefaults,
    findRoomIndex,
    cloneRoom,
    CreateJournalEntry,
    DeleteJournalEntry,
    DeleteRoomEntry,
    CreateRoomsPlan,
} from './createRooms';
import { applyEditorSettings } from './applyEditorSettings';
import { toCssFontFamily } from './utils/cssFont';
import { LegendEditorDialog } from './ui/LegendEditorDialog';

document.fonts.ready.then(() => {
    void initApp();
});

async function initApp() {
    const canvasEl = document.getElementById('main-canvas') as HTMLCanvasElement;
    if (!canvasEl) throw new Error("Canvas missing");

    const appState: AppState = {
        activeToolId: 'brush',
        rectMode: 'light',
        ovalMode: 'light',
        lineMode: 'light',
        gradientTarget: 'foreground',
        typeStyle: 'regular',
        selectedChar: '█',
        fgColor: [204, 204, 204],
        bgColor: [0, 0, 0],
        fontFamily: 'KreativeSquare',
        gradientStops: [[0, 0, 0] as Color, [255, 255, 255] as Color],
        selectMode: 'rectangle',
        rotateMode: 'cw90',
        fillMode: 'brush',
        lineDiagonal: false,
        eyedropperTarget: 'fg-fg'
    };

    const undoStack = new UndoStack();

    let canvasState = new CanvasState(24, 24);

    const grant = readDrawGrant();
    let mapEditSession: MapEditSession | null = null;
    let mapEditOrigin: MapEditOrigin | null = null;
    let mapPayload: MapEditPayload | null = null;
    let roomCellSet: Set<string> | null = null;
    let legendEntries: MapLegendEntry[] = [];
    let playerSymbol: string = 'X';
    if (grant) {
        const payload = grant.payload as MapEditPayload;
        mapPayload = payload;
        mapEditOrigin = loadMapPayload(canvasState, payload);
        mapEditSession = new MapEditSession(grant.key, canvasState, mapEditOrigin);
        roomCellSet = new Set(mapEditOrigin.roomCells);
        legendEntries = (payload.legend ?? []).map((e) => ({ ...e }));
        if (payload.playerSymbol) playerSymbol = payload.playerSymbol;
        clearDrawGrant();
    }

    undoStack.setCurrentState(canvasState);

    let currentFontSize = 18;
    if (document.fonts) {
        const family = toCssFontFamily(appState.fontFamily);
        try { await document.fonts.load(`${currentFontSize}px ${family}`, ''); } catch {}
        try { await document.fonts.load(`${currentFontSize}px ${family}`, 'M'); } catch {}
        try { await document.fonts.ready; } catch {}
    }
    let metrics = measureCellMetrics(appState.fontFamily, currentFontSize);
    const renderer = new GridRenderer(canvasEl, canvasState, metrics);

    if (mapEditOrigin) {
        renderer.setRoomCells(roomCellSet ?? mapEditOrigin.roomCells);
    }
    if (mapPayload) {
        logRoomData(mapPayload);
    }

    const context: ToolContext = {
        state: canvasState,
        undoStack,
        renderer,
        appState,
        modifiers: { shiftKey: false, altKey: false, ctrlKey: false }
    };

    // Undo checkpoints for in-flight move validations (FIFO: the server
    // answers validations in order). Each entry is the undo depth before the
    // move's own stroke, so a deny reverts that stroke plus anything painted
    // after it instead of a single unrelated entry.
    const pendingMoveCheckpoints: number[] = [];

    context.onCellsMoved = (moves) => {
        if (!mapEditSession || !mapEditOrigin) return;
        pendingMoveCheckpoints.push(Math.max(0, undoStack.depth - 1));
        const worldMoves = moves.map((m) => ({
            fromX: m.fromCol + mapEditOrigin!.originX,
            fromY: canvasState.height - 1 - m.fromRow + mapEditOrigin!.originY,
            toX: m.toCol + mapEditOrigin!.originX,
            toY: canvasState.height - 1 - m.toRow + mapEditOrigin!.originY,
        }));
        if (roomCellSet) {
            for (const m of moves) {
                const key = `${m.fromCol},${m.fromRow}`;
                if (roomCellSet.has(key)) {
                    roomCellSet.delete(key);
                    roomCellSet.add(`${m.toCol},${m.toRow}`);
                }
            }
            renderer.setRoomCells(roomCellSet);
        }
        mapEditSession.validateRoomMoves(worldMoves);
    };


    // The dialog never holds a canvas reference: it reads the live state
    // through the getter on every open/confirm, so New/undo/load swaps
    // cannot leave it pointing at a discarded map.
    const textToolDialog = new TextToolDialog(appState, () => canvasState, (newState) => {
        canvasState = newState;
        context.state = canvasState;
        undoStack.setCurrentState(canvasState);
        renderer.updateState(canvasState);
        // The dialog swaps in a fresh canvas: keep the mapedit session on
        // the live object or save would diff a discarded one.
        mapEditSession?.rebindCanvas(canvasState);
        clearRoomEditHistory();
    }, () => metrics, undoStack);

    const toolManager = new ToolManager(context);
    toolManager.addTool('rect', new RectangleTool());
    toolManager.addTool('oval', new OvalTool());
    toolManager.addTool('line', new LineTool());
    toolManager.addTool('type', new TypeTool());
    toolManager.addTool('text', new TextTool(() => textToolDialog.open()));
    toolManager.addTool('gradient', new GradientTool());
    toolManager.addTool('fill', new FillTool());
    toolManager.addTool('eyedropper', new EyedropperTool());
    const selectionTool = new SelectionTool();
    toolManager.addTool('select', selectionTool);
    context.selectionSync = selectionTool;
    toolManager.addTool('move', new MoveTool());
    const rotateTool = new RotateTool();
    toolManager.addTool('rotate', rotateTool);
    // Link-target picking for room exits: a click resolves to a room
    // through pickLinkTarget (no-op unless a pick is in progress).
    const linkPickTool: Tool = {
        onMouseDown: (_ctx, cell) => pickLinkTarget(cell),
        onDrag: () => {},
        onMouseUp: () => {},
        onHover: () => {},
        onMouseLeave: () => {},
    };
    toolManager.addTool('linkpick', linkPickTool);

    const controller = new CanvasController(canvasEl, metrics, toolManager);
    if (document.fonts && !document.fonts.check(`${currentFontSize}px ${toCssFontFamily(appState.fontFamily)}`)) {
        void document.fonts.ready.then(() => {
            const refreshed = measureCellMetrics(appState.fontFamily, currentFontSize);
            controller.updateMetrics(refreshed);
            renderer.updateMetrics(refreshed);
        });
    }

    canvasEl.addEventListener('mousedown', (e) => {
        if (e.button === 2) {
            selectionTool.clearSelection(context);
            renderer.clearSelection();
            endLinkPick();
        }
    });

    let charMapDialog: CharMapDialog;

    const charPalette = new CharPalette('char-palette', appState, () => {
        if (appState.activeToolId === 'erase') appState.activeToolId = 'brush'; // switch back
    }, () => {
        charMapDialog.open(appState.fontFamily);
    });

    charMapDialog = new CharMapDialog((chars: string[]) => {
        charPalette.addCustomChars(chars);
    });

    const fgPicker = new ColorPicker('fg-picker-container', true, appState, () => {
        if (appState.activeToolId === 'erase') appState.activeToolId = 'brush';
    });

    const bgPicker = new ColorPicker('bg-picker-container', false, appState, () => {
        if (appState.activeToolId === 'erase') appState.activeToolId = 'brush';
    });

    const gradientPicker = new GradientPicker('gradient-picker-container', appState);

    const layerManager = new LayerManager('layer-manager-container', canvasState, undoStack);

    // Authoritative room list for the panel (panel holds its own copy for
    // typing; every committed exit edit adopts into this list first).
    let rooms: MapRoom[] = (mapEditOrigin?.rooms ?? mapPayload?.rooms ?? []).map((r) => ({
        ...r,
        exits: r.exits.map((e) => ({ ...e, aliases: [...e.aliases], coord: [...e.coord] as [string, number, number, number] })),
    }));

    interface ExitJournalEntry {
        depth: number;
        index: number;
        before: MapExitEdit[];
        after: MapExitEdit[];
    }
    // Ctrl+Z journals for exit edits: each applied op pushes an identical
    // canvas clone as its checkpoint (depth-keyed) plus an undo entry.
    // The onStateRestore hook below replays the inverse/forward list, so
    // exit edits interleave with paint strokes in one undo history.
    const exitUndo: ExitJournalEntry[] = [];
    const exitRedo: ExitJournalEntry[] = [];
    // Every setExits send waits on an ack/deny matched by room coords.
    const unackedExits: { x: number; y: number; index: number; before: MapExitEdit[] }[] = [];
    // Create-rooms undo rides a parallel journal: the undo stack holds
    // canvas clones only, so each applied plan pushes a room entry plus an
    // unacked-create record that a deny rolls back. Undoing a create sends
    // the rooms back for genuine server-side deletion (tracked below so a
    // deny re-appends the journaled copies).
    const createUndo: CreateJournalEntry[] = [];
    const createRedo: CreateJournalEntry[] = [];
    const unackedCreates: { coords: { x: number; y: number }[]; affected: { x: number; y: number; before: MapExitEdit[] }[] }[] = [];
    const unackedDeletes: { coords: { x: number; y: number }[]; affected: { x: number; y: number; before: MapExitEdit[] }[]; removed: MapRoom[] }[] = [];
    // Direct deletes ride their own journal so Ctrl+Z restores the batch:
    // each entry carries the removed copies plus the stripped survivors'
    // before/after pairs (matched by coords at replay, like creates).
    const deleteUndo: DeleteJournalEntry[] = [];
    const deleteRedo: DeleteJournalEntry[] = [];
    // A canvas push clears the redo stack: drop exit/create/delete redo
    // entries in lockstep so a later redo can never false-match a stale depth.
    undoStack.onPush(() => { exitRedo.length = 0; createRedo.length = 0; deleteRedo.length = 0; });

    const cloneExits = (exits: MapExitEdit[]): MapExitEdit[] =>
        exits.map((e) => ({ ...e, aliases: [...e.aliases], coord: [...e.coord] as [string, number, number, number] }));

    function clearRoomEditHistory(): void {
        exitUndo.length = 0;
        exitRedo.length = 0;
        unackedExits.length = 0;
        createUndo.length = 0;
        createRedo.length = 0;
        unackedCreates.length = 0;
        unackedDeletes.length = 0;
        deleteUndo.length = 0;
        deleteRedo.length = 0;
    }

    function canvasToWorld(col: number, row: number): { x: number; y: number } {
        const originX = mapEditOrigin?.originX ?? 0;
        const originY = mapEditOrigin?.originY ?? 0;
        return { x: col + originX, y: canvasState.height - 1 - row + originY };
    }

    function refreshRoomPanel(): void {
        if (!mapEditOrigin) return;
        const matched: MapRoom[] = [];
        for (const key of selectionTool.getSelectedCells()) {
            const [col, row] = key.split(',').map(Number);
            if (!Number.isInteger(col) || !Number.isInteger(row)) continue;
            const world = canvasToWorld(col, row);
            const room = rooms.find((r) => r.x === world.x && r.y === world.y);
            if (room && !matched.includes(room)) matched.push(room);
        }
        if (matched.length === 1) roomEditor.selectRoom(matched[0].x, matched[0].y);
        else roomEditor.showNotice();
    }

    function adoptExits(index: number, exits: MapExitEdit[]): void {
        const room = rooms[index];
        if (!room) return;
        room.exits = cloneExits(exits);
        roomEditor.setRooms(rooms);
    }

    function sendExits(roomX: number, roomY: number, index: number, before: MapExitEdit[], after: MapExitEdit[]): void {
        if (!mapEditSession) return;
        unackedExits.push({ x: roomX, y: roomY, index, before: cloneExits(before) });
        mapEditSession.setExits(roomX, roomY, cloneExits(after));
    }

    function requireSession(): boolean {
        if (mapEditSession) return true;
        const msg = 'No map edit session — re-run mapedit in-game.';
        console.warn(msg);
        mapErrorDialog.show(msg);
        return false;
    }

    // Full-list exit save with an undo checkpoint (rename/coord/pick/delete
    // all funnel here; reciprocal deletes call once per room).
    function applyExitsAtIndex(index: number, after: MapExitEdit[]): void {
        const room = rooms[index];
        if (!room || !requireSession()) return;
        const before = cloneExits(room.exits);
        undoStack.push(canvasState);
        exitUndo.push({ depth: undoStack.depth, index, before, after: cloneExits(after) });
        exitRedo.length = 0;
        adoptExits(index, after);
        sendExits(room.x, room.y, index, before, after);
        roomEditor.setStatus('');
        refreshRoomPanel();
    }

    function dropUnackedExits(x: number, y: number): void {
        const at = unackedExits.findIndex((u) => u.x === x && u.y === y);
        if (at >= 0) unackedExits.splice(at, 1);
    }

    const btnCreateRooms = document.getElementById('btn-create-rooms');
    // The button only makes sense with a live edit session and a selection.
    function updateCreateRoomsButton(): void {
        if (!btnCreateRooms) return;
        btnCreateRooms.style.display = (mapEditSession && selectionTool.getSelectedCells().size > 0) ? '' : 'none';
    }

    const btnDeleteRooms = document.getElementById('btn-delete-rooms');
    // Selected cells that are known rooms (world coords resolve into the
    // room list), deduped. Drives both the button visibility and the click.
    function selectedRoomCoords(): { x: number; y: number }[] {
        const found: { x: number; y: number }[] = [];
        if (!mapEditOrigin) return found;
        for (const cellKey of selectionTool.getSelectedCells()) {
            const [col, row] = cellKey.split(',').map(Number);
            if (!Number.isInteger(col) || !Number.isInteger(row)) continue;
            const world = canvasToWorld(col, row);
            if (findRoomIndex(rooms, world.x, world.y) < 0) continue;
            if (found.every((c) => c.x !== world.x || c.y !== world.y)) found.push(world);
        }
        return found;
    }
    // Deleting only makes sense with a live edit session and at least one
    // selected room; empties in the selection are ignored, not deleted.
    function updateDeleteRoomsButton(): void {
        if (!btnDeleteRooms) return;
        btnDeleteRooms.style.display = (mapEditSession && selectedRoomCoords().length > 0) ? '' : 'none';
    }

    // The room overlay is always rebuilt from the live room list:
    // incremental add/delete strands stale keys when the canvas geometry
    // shifts between ops (deleted rooms staying highlighted).
    function syncRoomCells(): void {
        if (roomCellSet && mapEditOrigin) {
            roomCellSet = buildRoomCellKeys(rooms, mapEditOrigin.originX, mapEditOrigin.originY, canvasState.height);
            renderer.setRoomCells(roomCellSet);
        }
    }

    // Optimistic create: append the planned rooms and adopt the planned
    // exit lists locally, then queue the batch. The ack drops the unacked
    // record; a deny rolls the local list back to the journaled befores.
    function applyCreateRooms(plan: CreateRoomsPlan): void {
        if (!mapEditSession || !mapPayload || !mapEditOrigin || !requireSession()) return;
        const { created, affected } = splitCreateTargets(plan, rooms);
        undoStack.push(canvasState);
        for (const room of created) rooms.push(room);
        for (const a of affected) {
            const index = findRoomIndex(rooms, a.x, a.y);
            if (index >= 0) adoptExits(index, a.after);
        }
        unackedCreates.push({
            coords: created.map(({ x, y }) => ({ x, y })),
            affected: affected.map(({ x, y, before }) => ({ x, y, before })),
        });
        mapEditSession.createRooms(plan.rooms, plan.exits);
        createUndo.push({ depth: undoStack.depth, created, affected });
        createRedo.length = 0;
        syncRoomCells();
        // The squares are rooms now, not a selection: drop the yellow
        // selection outline so the room-color outline (when shown) is what
        // remains. This also hides the button via onSelectionChange.
        selectionTool.clearSelection(context);
        roomEditor.setRooms(rooms);
        roomEditor.setStatus('');
        refreshRoomPanel();
        updateCreateRoomsButton();
    updateDeleteRoomsButton();
    }

    function undoCreateRooms(entry: CreateJournalEntry): void {
        for (const a of entry.affected) {
            const index = findRoomIndex(rooms, a.x, a.y);
            if (index >= 0) {
                adoptExits(index, a.before);
                sendExits(a.x, a.y, index, a.after, a.before);
            }
        }
        // Third-party survivors linking into the created rooms are
        // stripped by the server delete too: adopt the stripped lists
        // locally (rooms the create already touched restored their
        // pre-create befores above, which cannot target new rooms, so
        // they never appear here). The ack forgets the session
        // tracking; a deny re-appends the journaled copies below.
        const stripped = mapPayload
            ? computeStrippedSurvivors(entry.created, rooms, mapPayload.area, mapPayload.z)
            : [];
        for (const s of stripped) {
            const index = findRoomIndex(rooms, s.x, s.y);
            if (index >= 0) adoptExits(index, s.after);
        }
        const removed = removeRoomsByCoords(rooms, entry.created);
        syncRoomCells();
        const deletes = buildDeleteEntries(removed, rooms);
        unackedDeletes.push({
            coords: removed.map(({ x, y }) => ({ x, y })),
            affected: [
                ...entry.affected.map(({ x, y, before }) => ({ x, y, before })),
                ...stripped.map((s) => ({ x: s.x, y: s.y, before: s.before })),
            ],
            removed: removed.map(cloneRoom),
        });
        mapEditSession?.deleteRooms(deletes);
        roomEditor.setRooms(rooms);
        roomEditor.setStatus('Undid room creation.');
        refreshRoomPanel();
        updateCreateRoomsButton();
    updateDeleteRoomsButton();
    }

    function redoCreateRooms(entry: CreateJournalEntry): void {
        for (const room of entry.created) {
            if (findRoomIndex(rooms, room.x, room.y) < 0) rooms.push(cloneRoom(room));
        }
        for (const a of entry.affected) {
            const index = findRoomIndex(rooms, a.x, a.y);
            if (index >= 0) adoptExits(index, a.after);
        }
        const resend = buildCreateResend(entry);
        unackedCreates.push({
            coords: entry.created.map(({ x, y }) => ({ x, y })),
            affected: entry.affected.map(({ x, y, before }) => ({ x, y, before })),
        });
        // A redo re-sends the full original plan: coords that survived as
        // server-side shells are skipped there and just regain their links.
        mapEditSession?.createRooms(resend.rooms, resend.exits);
        syncRoomCells();
        roomEditor.setRooms(rooms);
        roomEditor.setStatus('Redid room creation.');
        refreshRoomPanel();
        updateCreateRoomsButton();
    updateDeleteRoomsButton();
    }

    const roomEditor = new RoomEditor('room-editor-container', (room) => {
        if (!requireSession()) return;
        mapEditSession?.saveRoom(room.x, room.y, room.name, room.desc);
    }, {
        onExitsChange: (change) => {
            const index = rooms.findIndex((r) => r.x === change.roomX && r.y === change.roomY);
            if (index >= 0) applyExitsAtIndex(index, change.exits);
        },
        onDeleteExit: (del) => { void requestDeleteExit(del.roomX, del.roomY, del.index); },
        onPickLink: (pick) => startLinkPick(pick.roomX, pick.roomY, pick.index),
    });
    roomEditor.setRooms(rooms);
    selectionTool.onSelectionChange(() => { refreshRoomPanel(); updateCreateRoomsButton(); updateDeleteRoomsButton(); });
    updateCreateRoomsButton();
    updateDeleteRoomsButton();

    // Link-target picking: the next canvas click resolves to a room whose
    // coords become the exit target. Hoisted (the linkpick tool above
    // references it) — runs only after roomEditor exists.
    let linkPick: { roomX: number; roomY: number; index: number; prevTool: string } | null = null;
    let pickEscapeHandler: ((e: KeyboardEvent) => void) | null = null;

    function startLinkPick(roomX: number, roomY: number, index: number): void {
        if (!requireSession() || !mapPayload) return;
        if (linkPick) {
            linkPick = { roomX, roomY, index, prevTool: linkPick.prevTool };
        } else {
            linkPick = { roomX, roomY, index, prevTool: appState.activeToolId };
        }
        appState.activeToolId = 'linkpick';
        roomEditor.setPicking(true);
        if (!pickEscapeHandler && typeof window !== 'undefined') {
            pickEscapeHandler = (e: KeyboardEvent) => {
                if (e.key === 'Escape' && linkPick) {
                    endLinkPick();
                    e.preventDefault();
                }
            };
            window.addEventListener('keydown', pickEscapeHandler);
        }
    }

    function endLinkPick(): void {
        if (linkPick) appState.activeToolId = linkPick.prevTool;
        linkPick = null;
        roomEditor.setPicking(false);
    }

    function pickLinkTarget(cell: { x: number; y: number }): void {
        if (!linkPick || !mapPayload) return;
        const world = canvasToWorld(cell.x, cell.y);
        const target = rooms.find((r) => r.x === world.x && r.y === world.y);
        if (!target) {
            roomEditor.setStatus('Click a room for the exit to lead to…');
            return;
        }
        const { roomX, roomY, index } = linkPick;
        const room = rooms.find((r) => r.x === roomX && r.y === roomY);
        const exit = room?.exits[index];
        endLinkPick();
        if (!room || !exit || !requireSession()) return;
        const after = cloneExits(room.exits);
        after[index] = { ...after[index], coord: [mapPayload.area, target.x, target.y, mapPayload.z] };
        applyExitsAtIndex(rooms.indexOf(room), after);
    }

    async function requestDeleteExit(roomX: number, roomY: number, index: number): Promise<void> {
        const roomIndex = rooms.findIndex((r) => r.x === roomX && r.y === roomY);
        const room = roomIndex >= 0 ? rooms[roomIndex] : undefined;
        const exit = room?.exits[index];
        if (!room || !exit || !requireSession() || !mapPayload) return;
        // A return exit back to this room prompts: delete it too?
        const [tArea, tx, ty, tz] = exit.coord;
        const target = (tArea === mapPayload.area && tz === mapPayload.z)
            ? rooms.find((r) => r.x === tx && r.y === ty)
            : undefined;
        const backIndex = target?.exits.findIndex((e) =>
            e.coord[0] === mapPayload!.area && e.coord[1] === roomX && e.coord[2] === roomY && e.coord[3] === mapPayload!.z) ?? -1;
        if (target && backIndex >= 0) {
            const confirmed = await confirmDialog.confirm(
                `Delete the return exit '${target.exits[backIndex].name}' in (${tx}, ${ty}) too?`);
            if (confirmed) {
                const targetIndex = rooms.indexOf(target);
                applyExitsAtIndex(targetIndex, target.exits.filter((_, i) => i !== backIndex));
            }
        }
        applyExitsAtIndex(roomIndex, room.exits.filter((_, i) => i !== index));
    }

    const moveDeniedDialog = new MessageDialog('move-denied-modal');
    const mapErrorDialog = new MessageDialog('map-error-modal');
    const confirmDialog = new ConfirmDialog('confirm-modal');
    const legendEditor = new LegendEditorDialog((legend) => {
        legendEntries = legend.map((e) => ({ ...e }));
        if (!mapEditSession) {
            const msg = 'No map edit session — re-run mapedit in-game.';
            console.warn(msg);
            mapErrorDialog.show(msg);
            return;
        }
        mapEditSession.saveLegend(legendEntries);
    }, charMapDialog, () => appState.fontFamily);
    document.getElementById('btn-edit-legend')?.addEventListener('click', () => {
        if (!mapEditSession) {
            const msg = 'No map edit session — re-run mapedit in-game.';
            console.warn(msg);
            mapErrorDialog.show(msg);
            return;
        }
        legendEditor.open(legendEntries, playerSymbol);
    });
    btnCreateRooms?.addEventListener('click', () => {
        if (!mapEditSession || !mapPayload || !mapEditOrigin || !requireSession()) return;
        const selected: { x: number; y: number }[] = [];
        for (const cellKey of selectionTool.getSelectedCells()) {
            const [col, row] = cellKey.split(',').map(Number);
            if (!Number.isInteger(col) || !Number.isInteger(row)) continue;
            selected.push(canvasToWorld(col, row));
        }
        const plan = planCreateRooms(selected, rooms, mapPayload.area, mapPayload.z);
        if (!plan) {
            roomEditor.setStatus('Selected squares are already rooms.');
            return;
        }
        applyCreateRooms(plan);
    });

    // Undo of a direct delete: re-append the journaled copies, restore
    // the stripped survivors' before lists, and re-queue the full restore
    // (create first — a before list may target a re-created room) so a
    // deny rolls back through the create journal below.
    function undoDeleteRooms(entry: DeleteJournalEntry): void {
        for (const room of entry.removed) {
            if (findRoomIndex(rooms, room.x, room.y) < 0) rooms.push(cloneRoom(room));
        }
        for (const s of entry.stripped) {
            const index = findRoomIndex(rooms, s.x, s.y);
            if (index >= 0) {
                adoptExits(index, s.before);
                sendExits(s.x, s.y, index, s.after, s.before);
            }
        }
        const resend = buildDeleteUndoResend(entry);
        unackedCreates.push({
            coords: entry.removed.map(({ x, y }) => ({ x, y })),
            affected: entry.stripped.map((s) => ({ x: s.x, y: s.y, before: s.after })),
        });
        mapEditSession?.createRooms(resend.rooms, resend.exits);
        syncRoomCells();
        roomEditor.setRooms(rooms);
        roomEditor.setStatus('Undid room deletion.');
        refreshRoomPanel();
        updateCreateRoomsButton();
        updateDeleteRoomsButton();
    }

    // Redo of a direct delete: drop the rooms again and re-queue the
    // original delete entries (the server strips the survivors again).
    function redoDeleteRooms(entry: DeleteJournalEntry): void {
        const redone = removeRoomsByCoords(rooms, entry.removed);
        syncRoomCells();
        for (const s of entry.stripped) {
            const index = findRoomIndex(rooms, s.x, s.y);
            if (index >= 0) adoptExits(index, s.after);
        }
        unackedDeletes.push({
            coords: entry.removed.map(({ x, y }) => ({ x, y })),
            affected: entry.stripped.map((s) => ({ x: s.x, y: s.y, before: s.before })),
            removed: redone.map(cloneRoom),
        });
        mapEditSession?.deleteRooms(entry.deletes);
        roomEditor.setRooms(rooms);
        roomEditor.setStatus('Redid room deletion.');
        refreshRoomPanel();
        updateCreateRoomsButton();
        updateDeleteRoomsButton();
    }

    btnDeleteRooms?.addEventListener('click', () => {
        if (!mapEditSession || !mapPayload || !mapEditOrigin || !requireSession()) return;
        const targets = selectedRoomCoords();
        if (targets.length === 0) {
            roomEditor.setStatus('No rooms selected.');
            return;
        }
        // Direct delete: strip the survivors' inbound links (the server
        // strips the same lists), drop the rooms locally, and queue the
        // batch. Fallbacks go to the nearest surviving room, so occupants
        // stay on the map; with no survivor an occupied room is denied and
        // rolled back below. The ack forgets the session tracking; a deny
        // re-appends the journaled copies and restores the before lists.
        // The op pushes a canvas checkpoint so Ctrl+Z restores the batch.
        const stripped = computeStrippedSurvivors(targets, rooms, mapPayload.area, mapPayload.z);
        undoStack.push(canvasState);
        for (const s of stripped) {
            const index = findRoomIndex(rooms, s.x, s.y);
            if (index >= 0) adoptExits(index, s.after);
        }
        const removed = removeRoomsByCoords(rooms, targets);
        syncRoomCells();
        const deletes: DeleteRoomEntry[] = buildDeleteEntries(removed, rooms);
        unackedDeletes.push({
            coords: removed.map(({ x, y }) => ({ x, y })),
            affected: stripped.map((s) => ({ x: s.x, y: s.y, before: s.before })),
            removed: removed.map(cloneRoom),
        });
        mapEditSession.deleteRooms(deletes);
        deleteUndo.push({ depth: undoStack.depth, removed: removed.map(cloneRoom), stripped, deletes });
        deleteRedo.length = 0;
        roomEditor.setRooms(rooms);
        roomEditor.setStatus('');
        refreshRoomPanel();
        updateDeleteRoomsButton();
    });

    // Safe to register here: websocket events are async and cannot fire
    // before the synchronous setup below this point has completed.
    mapEditSession?.onEvent((event) => {
        if (event.type === 'reject') {
            console.warn(`Map edit rejected (${event.reason}). Re-run 'mapedit' in-game.`);
            mapErrorDialog.show(`Map edit rejected: ${event.reason}. Re-run 'mapedit' in-game.`);
        } else if (event.type === 'error') {
            console.warn(`Map edit failed: ${event.message}.`);
            mapErrorDialog.show(event.message);
        } else if (event.type === 'legend_saved') {
            if ((import.meta as unknown as { env?: { DEV?: boolean } }).env?.DEV) console.log('Legend saved.');
        } else if (event.type === 'room_saved') {
            if ((import.meta as unknown as { env?: { DEV?: boolean } }).env?.DEV) console.log('Room saved.');
            roomEditor.setStatus('Saved to server.');
        } else if (event.type === 'room_denied') {
            console.warn(`Room save denied: ${event.reason}.`);
            mapErrorDialog.show(event.reason);
            roomEditor.setStatus(`Denied: ${event.reason}`);
        } else if (event.type === 'exits_saved') {
            dropUnackedExits(event.x, event.y);
            if ((import.meta as unknown as { env?: { DEV?: boolean } }).env?.DEV) console.log('Exits saved.');
            roomEditor.setStatus('Saved to server.');
        } else if (event.type === 'exits_denied') {
            console.warn(`Exits save denied: ${event.reason}.`);
            mapErrorDialog.show(event.reason);
            // Roll back to the pre-save list so the panel never shows
            // exits the server refused.
            const at = unackedExits.findIndex((u) => u.x === event.x && u.y === event.y);
            const entry = at >= 0 ? unackedExits[at] : undefined;
            if (at >= 0) unackedExits.splice(at, 1);
            if (entry) {
                adoptExits(entry.index, entry.before);
                refreshRoomPanel();
            }
            roomEditor.setStatus(`Denied: ${event.reason}`);
        } else if (event.type === 'created') {
            unackedCreates.shift();
            fillCreatedDefaults(rooms, event.rooms);
            roomEditor.setRooms(rooms);
            if ((import.meta as unknown as { env?: { DEV?: boolean } }).env?.DEV) console.log('Rooms created.');
            roomEditor.setStatus('Saved to server.');
            refreshRoomPanel();
        } else if (event.type === 'create_denied') {
            console.warn(`Room creation denied: ${event.reason}.`);
            mapErrorDialog.show(event.reason);
            // Roll back the optimistic apply so the panel never shows rooms
            // the server refused. The deny carries the planned coords, so a
            // second queued create is never rolled back by mistake.
            const wanted = new Set(event.rooms.map((c) => `${c.x},${c.y}`));
            const at = unackedCreates.findIndex((u) =>
                u.coords.length === event.rooms.length
                && u.coords.every((c) => wanted.has(`${c.x},${c.y}`)));
            const pending = at >= 0 ? unackedCreates.splice(at, 1)[0] : unackedCreates.shift();
            if (pending) {
                removeRoomsByCoords(rooms, pending.coords);
                for (const a of pending.affected) {
                    const index = findRoomIndex(rooms, a.x, a.y);
                    if (index >= 0) adoptExits(index, a.before);
                }
                syncRoomCells();
                const undoneAt = createUndo.findIndex((e) =>
                    e.created.length === pending.coords.length
                    && e.created.every((c) => wanted.has(`${c.x},${c.y}`)));
                if (undoneAt >= 0) createUndo.splice(undoneAt, 1);
                roomEditor.setRooms(rooms);
                refreshRoomPanel();
                updateCreateRoomsButton();
                updateDeleteRoomsButton();
            }
            roomEditor.setStatus(`Denied: ${event.reason}`);
        } else if (event.type === 'deleted') {
            unackedDeletes.shift();
            roomEditor.setRooms(rooms);
            if ((import.meta as unknown as { env?: { DEV?: boolean } }).env?.DEV) console.log('Rooms deleted.');
            roomEditor.setStatus('Saved to server.');
            refreshRoomPanel();
            updateCreateRoomsButton();
            updateDeleteRoomsButton();
        } else if (event.type === 'delete_denied') {
            console.warn(`Room deletion denied: ${event.reason}.`);
            mapErrorDialog.show(event.reason);
            // Roll back the optimistic removal: the server refused, so the
            // rooms are still there. A create-undo delete keeps its journal
            // entry in createRedo (the toolbar moved it there when undoing),
            // so it stays put — a later redo re-sends the create plan over
            // the survivors. A direct delete drops its deleteUndo entry:
            // the batch is back with its original lists, so there is
            // nothing left to undo.
            const wanted = new Set(event.rooms.map((c) => `${c.x},${c.y}`));
            const at = unackedDeletes.findIndex((u) =>
                u.coords.length === event.rooms.length
                && u.coords.every((c) => wanted.has(`${c.x},${c.y}`)));
            const pending = at >= 0 ? unackedDeletes.splice(at, 1)[0] : unackedDeletes.shift();
            if (pending) {
                for (const room of pending.removed) {
                    if (findRoomIndex(rooms, room.x, room.y) < 0) rooms.push(cloneRoom(room));
                }
                for (const a of pending.affected) {
                    const index = findRoomIndex(rooms, a.x, a.y);
                    if (index >= 0) adoptExits(index, a.before);
                }
                syncRoomCells();
                const undoneDeleteAt = deleteUndo.findIndex((e) =>
                    e.removed.length === pending.coords.length
                    && e.removed.every((c) => wanted.has(`${c.x},${c.y}`)));
                if (undoneDeleteAt >= 0) deleteUndo.splice(undoneDeleteAt, 1);
                roomEditor.setRooms(rooms);
                refreshRoomPanel();
                updateCreateRoomsButton();
                updateDeleteRoomsButton();
            }
            roomEditor.setStatus(`Denied: ${event.reason}`);
        } else if (event.type === 'saved') {
            if ((import.meta as unknown as { env?: { DEV?: boolean } }).env?.DEV) console.log('Saved to server.');
        } else if (event.type === 'moves_accepted') {
            pendingMoveCheckpoints.shift();
            // Relocate by move identity, not list position: the old code
            // assigned session coords[i] onto rooms[i] whenever the two
            // lists merely matched in length, so an accept landing while
            // unacked creates/deletes were in flight scrambled the list —
            // stranding teal on cells the list no longer pointed at, with
            // the panel unable to match them. Identity matching leaves
            // untracked rooms alone and preserves list order.
            applyAcceptedMoves(rooms, event.moves);
            roomEditor.setRooms(rooms);
            syncRoomCells();
            refreshRoomPanel();
        } else if (event.type === 'moves_denied') {
            console.warn('Room move denied by server — snapping back.');
            const checkpoint = pendingMoveCheckpoints.shift() ?? Math.max(0, undoStack.depth - 1);
            const restored = undoStack.undoTo(checkpoint);
            // The popped canvas states are gone: drop exit-undo entries
            // pointing past the checkpoint and clear exit redo (the redo
            // stack now holds foreign states). Exit data itself is
            // untouched — only glyph strokes revert here.
            while (exitUndo.length > 0 && exitUndo[exitUndo.length - 1].depth > checkpoint) exitUndo.pop();
            exitRedo.length = 0;
            while (createUndo.length > 0 && createUndo[createUndo.length - 1].depth > checkpoint) createUndo.pop();
            createRedo.length = 0;
            while (deleteUndo.length > 0 && deleteUndo[deleteUndo.length - 1].depth > checkpoint) deleteUndo.pop();
            deleteRedo.length = 0;
            if (restored) {
                canvasState = restored;
                context.state = restored;
                renderer.updateState(restored);
                layerManager.updateState(restored);
                // Undo restored a different canvas object: rebind the
                // session so save diffs the restored map, not the denied one.
                mapEditSession?.rebindCanvas(restored);
            }
            // The rooms list never moves optimistically — only the overlay
            // does — so a denied move leaves the list as the truth. Rebuild
            // the overlay from it instead of surgically reverting keys: the
            // revert could swap keys belonging to other rooms when creates,
            // deletes, or later moves landed between send and deny, planting
            // teal on squares with no room while the panel stayed empty.
            syncRoomCells();
            const maxListed = 5;
            const listed = event.moves
                .slice(0, maxListed)
                .map((m) => `(${m.toX}, ${m.toY})`)
                .join(', ');
            const extra = event.moves.length > maxListed ? ` and ${event.moves.length - maxListed} more` : '';
            moveDeniedDialog.show(
                `The server rejected moving ${event.moves.length === 1 ? 'a room' : `${event.moves.length} rooms`} `
                + `to ${listed}${extra} — destination occupied. The canvas was reverted to before the rejected move`
                + ` (strokes painted after it were reverted as well).`
            );
        }
    });

    // Shared font-change path: the toolbar select and settings-restore
    // both funnel through here so metrics, CSS var, and palette repaint.
    const handleFontChange = async (fontFamily: string) => {
        if (document.fonts) {
            const fam = toCssFontFamily(fontFamily);
            try { await document.fonts.load(`${currentFontSize}px ${fam}`, ' '); } catch {}
            try { await document.fonts.load(`${currentFontSize}px ${fam}`, 'M'); } catch {}
        }
        metrics = measureCellMetrics(fontFamily, currentFontSize);
        controller.updateMetrics(metrics);
        renderer.updateMetrics(metrics);

        document.documentElement.style.setProperty('--main-font', toCssFontFamily(fontFamily));
        charPalette.reRender();
    };

    const toolbarInst = new Toolbar(appState, undoStack, () => {
        AnsiExporter.download(canvasState, 'art.ans');
    }, (newState: CanvasState) => {
        // Exit-edit undo/redo replay (depth-keyed against the checkpoint
        // pushed with each op): a restored canvas at depth-1 of a checkpoint
        // replays the pre-edit list; at the checkpoint depth it replays the
        // edit. Either way the replayed list is queued to the server, so
        // Ctrl+Z genuinely reverts exit edits in history order.
        const depth = undoStack.depth;
        const undone = exitUndo.length > 0 ? exitUndo[exitUndo.length - 1] : undefined;
        const undoneCreate = createUndo.length > 0 ? createUndo[createUndo.length - 1] : undefined;
        const undoneDelete = deleteUndo.length > 0 ? deleteUndo[deleteUndo.length - 1] : undefined;
        if (undone && undone.depth === depth + 1) {
            exitUndo.pop();
            const room = rooms[undone.index];
            if (room) {
                adoptExits(undone.index, undone.before);
                sendExits(room.x, room.y, undone.index, undone.after, undone.before);
                roomEditor.setStatus('Undid exit change.');
                refreshRoomPanel();
            }
            exitRedo.push(undone);
        } else if (undoneCreate && undoneCreate.depth === depth + 1) {
            createUndo.pop();
            undoCreateRooms(undoneCreate);
            createRedo.push(undoneCreate);
        } else if (undoneDelete && undoneDelete.depth === depth + 1) {
            deleteUndo.pop();
            undoDeleteRooms(undoneDelete);
            deleteRedo.push(undoneDelete);
        } else {
            const redone = exitRedo.length > 0 ? exitRedo[exitRedo.length - 1] : undefined;
            const redoneCreate = createRedo.length > 0 ? createRedo[createRedo.length - 1] : undefined;
            const redoneDelete = deleteRedo.length > 0 ? deleteRedo[deleteRedo.length - 1] : undefined;
            if (redone && redone.depth === depth) {
                exitRedo.pop();
                const room = rooms[redone.index];
                if (room) {
                    adoptExits(redone.index, redone.after);
                    sendExits(room.x, room.y, redone.index, redone.before, redone.after);
                    roomEditor.setStatus('Redid exit change.');
                    refreshRoomPanel();
                }
                exitUndo.push(redone);
            } else if (redoneCreate && redoneCreate.depth === depth) {
                createRedo.pop();
                redoCreateRooms(redoneCreate);
                createUndo.push(redoneCreate);
            } else if (redoneDelete && redoneDelete.depth === depth) {
                deleteRedo.pop();
                redoDeleteRooms(redoneDelete);
                deleteUndo.push(redoneDelete);
            }
        }
        canvasState = newState;
        context.state = canvasState;
        renderer.updateState(canvasState);
        layerManager.updateState(canvasState);
        // Undo/redo swaps the canvas object: keep the session bound to it.
        mapEditSession?.rebindCanvas(canvasState);
    }, async (fontFamily: string) => {
        await handleFontChange(fontFamily);
    }, () => {
        textToolDialog.open();
    });
    toolbarInst.clearSelectionCallback = () => selectionTool.clearSelection();
    toolbarInst.onRotateAction = (mode) => {
        rotateTool.applyTransform(context, mode);
    };

    const leftResizer = new SidebarResizer('sidebar', 'sidebar-resizer');
    const rightResizer = new SidebarResizer('right-sidebar', 'right-sidebar-resizer', true);

    const previewWindow = new PreviewWindow(
        () => canvasState,
        () => appState.fontFamily
    );
    document.getElementById('btn-preview')?.addEventListener('click', () => {
        previewWindow.open();
    });

    window.addEventListener('beforeunload', () => {
        leftResizer.destroy();
        rightResizer.destroy();
        previewWindow.destroy();
        toolbarInst.destroy();
    });

    let roomVisible = true;
    let roomColor: [number, number, number] = [0, 204, 204];
    const btnRoomToggle = document.getElementById('btn-room-toggle');
    const roomColorSwatch = document.getElementById('room-color-swatch');
    const applyRoomColor = () => {
        if (roomColorSwatch) roomColorSwatch.style.backgroundColor = cssColor(roomColor);
        renderer.setRoomColor(cssColor(roomColor));
    };
    btnRoomToggle?.addEventListener('click', () => {
        roomVisible = !roomVisible;
        renderer.setRoomVisible(roomVisible);
        btnRoomToggle.textContent = roomVisible ? 'Hide Room Color' : 'Show Room Color';
    });
    document.getElementById('btn-room-color')?.addEventListener('click', () => {
        ColorPickerModal.getInstance().open(roomColor).then((result) => {
            if (result) {
                roomColor = result;
                applyRoomColor();
            }
        });
    });
    applyRoomColor();

    // Settings saved by an earlier Save-to-server ride the launch_draw
    // grant: restore them now that every widget exists. Older servers send
    // no settings, which is a no-op here.
    if (mapPayload?.editorSettings) {
        await applyEditorSettings(mapPayload.editorSettings, {
            appState,
            fgPicker,
            bgPicker,
            gradientPicker,
            charPalette,
            toolbar: toolbarInst,
            setFontSize: (size) => { currentFontSize = size; },
            applyFont: (family) => handleFontChange(family),
            applyRoom: (color, visible) => {
                roomColor = [...color] as Color;
                roomVisible = visible;
                if (btnRoomToggle) {
                    btnRoomToggle.textContent = roomVisible ? 'Hide Room Color' : 'Show Room Color';
                }
                applyRoomColor();
                renderer.setRoomVisible(roomVisible);
            },
        });
    }

    new NewCanvasDialog((w, h) => {
        // New means a cleared map; the reset helper owns the full sequence
        // (undo push, room/selection overlay clears, state rebind) so the
        // wiring itself stays unit-tested. The mapedit session stays bound
        // so Save still targets the same map.
        const created = beginNewCanvas({
            undoStack,
            renderer,
            selection: selectionTool,
            layers: layerManager,
            tools: context,
        }, w, h);
        canvasState = created.state;
        roomCellSet = created.roomCells;
        // New is a deliberate clear: keep the old baseline so the next
        // save expresses the wipe as deletions (a re-baseline here would
        // make the cleared map unsendable).
        mapEditSession?.rebindCanvas(created.state, { keepBaseline: true });
        clearRoomEditHistory();
    });

    new ResizeCanvasDialog(() => canvasState, (w, h) => {
        undoStack.push(canvasState);
        canvasState.resize(w, h);
        
        renderer.updateState(canvasState);
        layerManager.updateState(canvasState);
    });

    new ImageImportDialog(async (buffer, w, h, config) => {
        try {
            const ansi = await convertImageToAnsi(buffer, w, h, config);
            const cells = await parseAnsiToCells(ansi, w, h);
            
            undoStack.push(canvasState);
            
            canvasState = new CanvasState(w, h);
            context.state = canvasState;
            undoStack.setCurrentState(canvasState);
            layerManager.updateState(canvasState);
            
            // Map flat parsed cells array to batch format for CanvasState
            const batch = [];
            for (let i = 0; i < cells.length; i++) {
                const col = i % w;
                const row = Math.floor(i / w);
                if (row < h) {
                    batch.push({ col, row, cell: cells[i] });
                }
            }
            canvasState.applyBatch(batch);
            renderer.updateState(canvasState);
            // Image import replaces the canvas object: rebind the session.
            mapEditSession?.rebindCanvas(canvasState);
            clearRoomEditHistory();
        } catch (e) {
            console.error("Failed to load image:", e);
        }
    });

    const btnLoadImage = document.getElementById('btn-load-image');
    const imageUpload = document.getElementById('image-upload');
    btnLoadImage?.addEventListener('click', () => {
        imageUpload?.click();
    });

    const btnLoadAnsi = document.getElementById('btn-load-ansi');
    const ansiUpload = document.getElementById('ansi-upload') as HTMLInputElement;
    btnLoadAnsi?.addEventListener('click', () => ansiUpload?.click());

    const btnSaveServer = document.getElementById('btn-save-server');
    btnSaveServer?.addEventListener('click', () => {
        if (!mapEditSession) {
            const msg = 'No map edit session — re-run mapedit in-game.';
            console.warn(msg);
            mapErrorDialog.show(msg);
            return;
        }
        mapEditSession.saveToServer(buildEditorSettings({
            appState,
            fgSlots: fgPicker.getHistory(),
            bgSlots: bgPicker.getHistory(),
            customChars: charPalette.getCustomChars(),
            fontSize: currentFontSize,
            roomColor: [...roomColor] as Color,
            roomVisible,
        }));
    });

    ansiUpload?.addEventListener('change', () => {
        const file = ansiUpload.files?.[0];
        if (!file) return;
        const reader = new FileReader();
        reader.onload = async (e) => {
            const text = e.target?.result as string;
            if (!text) return;
            try {
                // Detect the file's true dimensions so the canvas is never clipped
                const { width, height } = detectAnsiDimensions(text);
                const newState = await parseAnsiToState(text, width, height);
                undoStack.push(canvasState);
                canvasState = newState;
                context.state = canvasState;
                undoStack.setCurrentState(canvasState);
                renderer.updateState(canvasState);
                layerManager.updateState(canvasState);
                // ANSI load replaces the canvas object: rebind the session.
                mapEditSession?.rebindCanvas(canvasState);
            } catch (err) {
                console.error('Failed to load ANSI file:', err);
            }
        };
        reader.readAsText(file);
        ansiUpload.value = '';
    });

    let previewState: CanvasState | null = null;
    
    const applyAdjustmentsToState = (state: CanvasState, opts: ColorAdjustOptions, applyToAll: boolean) => {
        const startIdx = applyToAll ? 0 : state.activeLayerIndex;
        const endIdx = applyToAll ? state.layers.length - 1 : state.activeLayerIndex;
        for (let i = startIdx; i <= endIdx; i++) {
            const layer = state.layers[i];
            for (let r = 0; r < state.height; r++) {
                for (let c = 0; c < state.width; c++) {
                    const cell = layer.cells[r][c];
                    cell.fg = applyColorAdjustments(cell.fg, opts);
                    if (cell.bg[0] !== -1) {
                        cell.bg = applyColorAdjustments(cell.bg, opts);
                    }
                }
            }
            if (layer.overflowCells) {
                for (const [, cell] of layer.overflowCells.entries()) {
                    cell.fg = applyColorAdjustments(cell.fg, opts);
                    if (cell.bg[0] !== -1) {
                        cell.bg = applyColorAdjustments(cell.bg, opts);
                    }
                }
            }
        }
    };

    const colorAdjustDialog = new ColorAdjustDialog(
        (opts, all) => {
            // On Preview
            if (!previewState) previewState = canvasState.clone();
            const tempState = previewState.clone();
            applyAdjustmentsToState(tempState, opts, all);
            renderer.updateState(tempState);
        },
        (opts, all) => {
            // On Apply
            if (!previewState) previewState = canvasState.clone();
            applyAdjustmentsToState(previewState, opts, all);
            
            undoStack.push(canvasState);
            canvasState = previewState;
            previewState = null;
            
            context.state = canvasState;
            undoStack.setCurrentState(canvasState);
            renderer.updateState(canvasState);
            layerManager.updateState(canvasState);
            // Color-adjust apply swaps in the preview clone: rebind it.
            mapEditSession?.rebindCanvas(canvasState);
        },
        () => {
            previewState = null;
            renderer.updateState(canvasState);
        }
    );

    document.getElementById('btn-color-adjust')?.addEventListener('click', () => {
        previewState = canvasState.clone();
        colorAdjustDialog.open();
    });

    const btnZoomIn = document.getElementById('btn-zoom-in');
    const btnZoomOut = document.getElementById('btn-zoom-out');

    const updateFontMetrics = async () => {
        if (document.fonts) {
            const fam = toCssFontFamily(appState.fontFamily);
            try { await document.fonts.load(`${currentFontSize}px ${fam}`, ''); } catch {}
            try { await document.fonts.load(`${currentFontSize}px ${fam}`, 'M'); } catch {}
        }
        metrics = measureCellMetrics(appState.fontFamily, currentFontSize);
        controller.updateMetrics(metrics);
        renderer.updateMetrics(metrics);
        charPalette.reRender();
    };

    btnZoomIn?.addEventListener('click', () => {
        if (currentFontSize < 72) {
            currentFontSize += 2;
            void updateFontMetrics();
        }
    });

    btnZoomOut?.addEventListener('click', () => {
        if (currentFontSize > 6) {
            currentFontSize -= 2;
            void updateFontMetrics();
        }
    });
}
