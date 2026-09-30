import { CanvasState } from '../state/CanvasState';
import { UndoStack } from '../state/UndoStack';
import { GridRenderer } from './GridRenderer';
import type { SelectionSync } from '../tools/Tool';
import type { ServerBounds } from '../types';

/**
 * Dependencies for the New command (fresh blank canvas at a given size).
 * Structural types keep this unit-testable without booting the app: the
 * real GridRenderer/SelectionTool/UndoStack satisfy these directly.
 */
export interface NewCanvasDeps {
    undoStack: UndoStack;
    renderer: GridRenderer;
    selection: SelectionSync;
    layers: { updateState(state: CanvasState): void };
    tools: { state: CanvasState };
}

/**
 * Optional layout for the New command. `w`/`h` are always the server grid
 * (what gets sent back); the viewport may be larger so the fresh grid sits
 * inset with working margin around it, and `bounds` places the violet rect
 * inside that viewport. Omitted fields keep the old behavior: viewport ==
 * grid, bounds == full canvas.
 */
export interface NewCanvasLayout {
    viewportW?: number;
    viewportH?: number;
    bounds?: ServerBounds;
}

/**
 * Reset to a fresh blank canvas (the New command). Pushes the discarded
 * canvas for undo, then clears everything that belongs to it — including
 * the mapedit room-cells overlay, whose coordinates would otherwise linger
 * as stale outlines on the new canvas. Returns the fresh state plus its
 * (empty) room set so the caller can rebind mapedit tracking.
 *
 * Transient tool state (anchors, lasso paths, previews) is cleared where it
 * lives outside the tools; per-tool gesture anchors reset on their next
 * mousedown, which unconditionally re-arms them for the new canvas.
 */
export function beginNewCanvas(
    deps: NewCanvasDeps,
    w: number,
    h: number,
    layout?: NewCanvasLayout,
): { state: CanvasState; roomCells: Set<string> } {
    if (!Number.isInteger(w) || !Number.isInteger(h) || w < 1 || h < 1 || w > 2048 || h > 2048) {
        throw new RangeError(`Invalid canvas size ${w}x${h}: width and height must be integers in 1..2048.`);
    }
    const vw = layout?.viewportW ?? w;
    const vh = layout?.viewportH ?? h;
    if (!Number.isInteger(vw) || !Number.isInteger(vh) || vw < 1 || vh < 1 || vw > 2048 || vh > 2048) {
        throw new RangeError(`Invalid canvas viewport ${vw}x${vh}: width and height must be integers in 1..2048.`);
    }
    deps.undoStack.push(deps.tools.state);
    const state = new CanvasState(vw, vh);
    if (layout?.bounds) state.setServerBounds(layout.bounds);

    const roomCells = new Set<string>();
    deps.renderer.setRoomCells(roomCells);
    deps.selection.clearSelection();
    deps.renderer.clearSelection();
    deps.renderer.clearPreview();

    deps.tools.state = state;
    deps.undoStack.setCurrentState(state);
    deps.renderer.updateState(state);
    deps.layers.updateState(state);
    return { state, roomCells };
}
