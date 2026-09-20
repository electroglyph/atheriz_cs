// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { beginNewCanvas, type NewCanvasDeps } from '../src/canvas/newCanvas';
import { CanvasState } from '../src/state/CanvasState';
import { GridRenderer } from '../src/canvas/GridRenderer';
import { SelectionTool } from '../src/tools/SelectionTool';
import { UndoStack } from '../src/state/UndoStack';

/**
 * Pins the New-command wiring itself (not just the clearing mechanism):
 * beginNewCanvas is the exact function main.ts calls, exercised here with
 * the real GridRenderer, SelectionTool, CanvasState and UndoStack.
 */
function makeDeps() {
  const canvas = document.createElement('canvas');
  const previous = new CanvasState(8, 8);
  const renderer = new GridRenderer(canvas, previous, {
    width: 8,
    height: 16,
    font: '8px monospace',
    advance: 8,
  });
  const selection = new SelectionTool();
  const undoStack = new UndoStack();
  undoStack.setCurrentState(previous);
  const tools = { state: previous };
  const layers = { updateState: vi.fn() };
  const afterReset = vi.fn();

  // Stale overlays from the previous map: teal room outlines plus a
  // selection that includes a room cell.
  renderer.setRoomCells(new Set(['2,2', '3,3']));
  selection.setSelection(new Set(['2,2']));
  renderer.setSelection(new Set(['2,2']));

  const deps = {
    undoStack,
    renderer,
    selection,
    layers,
    tools,
    afterReset,
  } satisfies NewCanvasDeps;
  return { deps, previous, layers, afterReset };
}

describe('beginNewCanvas', () => {
  it('returns a fresh blank canvas at the requested size and rebinds the holder', () => {
    const { deps, previous } = makeDeps();

    const created = beginNewCanvas(deps, 10, 6);

    expect(created.state).not.toBe(previous);
    expect(created.state.width).toBe(10);
    expect(created.state.height).toBe(6);
    expect(deps.tools.state).toBe(created.state);
  });

  it('clears the previous map room overlay and returns an empty room set', () => {
    const { deps } = makeDeps();
    expect(deps.renderer.getRoomCells().size).toBe(2);

    const created = beginNewCanvas(deps, 10, 6);

    expect(created.roomCells.size).toBe(0);
    expect(deps.renderer.getRoomCells().size).toBe(0);
  });

  it('clears the selection in both tool and renderer', () => {
    const { deps } = makeDeps();
    expect(deps.selection.hasSelection).toBe(true);

    beginNewCanvas(deps, 10, 6);

    expect(deps.selection.hasSelection).toBe(false);
    expect(deps.renderer.getSelectedCells().size).toBe(0);
  });

  it('pushes the discarded canvas for undo and notifies dependents', () => {
    const { deps, previous, layers, afterReset } = makeDeps();

    const created = beginNewCanvas(deps, 10, 6);

    expect(deps.undoStack.canUndo()).toBe(true);
    // push() snapshots, so undo yields an 8x8 clone of the discarded
    // canvas rather than the identical object.
    const undone = deps.undoStack.undo();
    expect(undone).not.toBeNull();
    expect(undone).not.toBe(created.state);
    expect(undone!.width).toBe(8);
    expect(undone!.height).toBe(8);
    expect(layers.updateState).toHaveBeenCalledWith(created.state);
    expect(afterReset).toHaveBeenCalledTimes(1);
  });
});
