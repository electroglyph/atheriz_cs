import { Tool, ToolContext } from './Tool';
import { Point } from '../types';

export class EyedropperTool implements Tool {
    onMouseDown(ctx: ToolContext, cell: Point): void {
        this.pickColor(ctx, cell);
    }

    onDrag(ctx: ToolContext, _from: Point, to: Point): void {
        this.pickColor(ctx, to);
    }

    onMouseUp(_ctx: ToolContext, _cell: Point): void {}
    onHover(_ctx: ToolContext, _cell: Point): void {}
    onMouseLeave(_ctx: ToolContext): void {}

    private pickColor(ctx: ToolContext, cell: Point) {
        const targetCell = ctx.state.getCompositeCell(cell.x, cell.y);
        const { appState } = ctx;
        
        let pickedColor = null;
        let isSavingToFg = true;

        if (targetCell) {
            if (appState.eyedropperTarget === 'fg-fg' || appState.eyedropperTarget === 'fg-bg') {
                pickedColor = targetCell.fg;
                isSavingToFg = (appState.eyedropperTarget === 'fg-fg');
            } else if (appState.eyedropperTarget === 'bg-fg' || appState.eyedropperTarget === 'bg-bg') {
                // Transparency must be read from the ACTIVE LAYER cell, not
                // the composite: getCompositeCell resolves a missing bg to
                // opaque black, so a composite bg check for [-1,-1,-1] can
                // never be true (dead check) and transparent cells picked
                // the wrong color.
                const activeLayer = ctx.state.layers[ctx.state.activeLayerIndex];
                const direct = activeLayer
                    && cell.y >= 0 && cell.y < activeLayer.cells.length
                    && cell.x >= 0 && cell.x < activeLayer.cells[cell.y].length
                    ? activeLayer.cells[cell.y][cell.x]
                    : null;
                const bg = direct ? direct.bg : targetCell.bg;
                if (bg[0] !== -1) {
                    pickedColor = bg;
                } else {
                    pickedColor = [0, 0, 0]; // default back to black if no bg found in composite
                }
                isSavingToFg = (appState.eyedropperTarget === 'bg-fg');
            }
        }

        if (pickedColor) {
            window.dispatchEvent(new CustomEvent('colorPicked', {
                detail: {
                    isFg: isSavingToFg,
                    color: pickedColor
                }
            }));
        }
    }
}
