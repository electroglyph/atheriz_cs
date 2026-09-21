import { CanvasState } from '../state/CanvasState';
import { UndoStack } from '../state/UndoStack';
import { GridRenderer } from './GridRenderer';
import type { SelectionSync } from '../tools/Tool';

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
): { state: CanvasState; roomCells: Set<string> } {
    if (!Number.isInteger(w) || !Number.isInteger(h) || w < 1 || h < 1 || w > 2048 || h > 2048) {
        throw new RangeError(`Invalid canvas size ${w}x${h}: width and height must be integers in 1..2048.`);
    }
    deps.undoStack.push(deps.tools.state);
    const state = new CanvasState(w, h);

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
