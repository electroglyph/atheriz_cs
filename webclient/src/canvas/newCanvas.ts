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
 */
export function beginNewCanvas(
    deps: NewCanvasDeps,
    w: number,
    h: number,
): { state: CanvasState; roomCells: Set<string> } {
    deps.undoStack.push(deps.tools.state);
    const state = new CanvasState(w, h);

    const roomCells = new Set<string>();
    deps.renderer.setRoomCells(roomCells);
    deps.selection.clearSelection();
    deps.renderer.clearSelection();

    deps.tools.state = state;
    deps.undoStack.setCurrentState(state);
    deps.renderer.updateState(state);
    deps.layers.updateState(state);
    return { state, roomCells };
}
