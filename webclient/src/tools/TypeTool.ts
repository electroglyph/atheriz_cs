import { Tool, ToolContext } from './Tool';
import { Point } from '../types';
import { TypeToolModal } from '../ui/TypeToolModal';

export class TypeTool implements Tool {
    private modal: TypeToolModal;

    constructor() {
        this.modal = new TypeToolModal();
    }

    onMouseDown(ctx: ToolContext, cell: Point): void {
        // Capture only coordinates here; ctx.state is read fresh at confirm
        // time so a New/undo/load landing while the modal is open paints onto
        // the live canvas, not a discarded one.
        const anchorX = cell.x;
        const anchorY = cell.y;
        this.modal.open().then(text => {
            if (text === null || text.length === 0) return;

            // Read the live canvas at confirm time (see above).
            const state = ctx.state;
            // Iterate code points so surrogate pairs (emoji) stay intact.
            const glyphs = Array.from(text);
            const style = ctx.appState.typeStyle;
            const updates: { col: number; row: number; cell: { char: string; fg: [number, number, number]; bg: [number, number, number]; bold?: boolean; italic?: boolean; underline?: boolean } }[] = [];

            for (let i = 0; i < glyphs.length; i++) {
                const col = anchorX + i;
                // Clip both axes against the live canvas; fully off-canvas
                // typing must not reach applyBatch (or the undo stack).
                if (col < 0 || col >= state.width || anchorY < 0 || anchorY >= state.height) continue;
                const cellData: { char: string; fg: [number, number, number]; bg: [number, number, number]; bold?: boolean; italic?: boolean; underline?: boolean } = {
                    char: glyphs[i]!,
                    fg: [...ctx.appState.fgColor] as [number, number, number],
                    bg: [...ctx.appState.bgColor] as [number, number, number],
                };

                if (style === 'bold') cellData.bold = true;
                else if (style === 'italic') cellData.italic = true;
                else if (style === 'underline') cellData.underline = true;

                updates.push({ col, row: anchorY, cell: cellData });
            }

            // Push undo only when something actually paints (contrast the old
            // push-before-clip, which left a spurious entry for off-canvas
            // typing like RectangleTool's guard avoids).
            if (updates.length > 0) {
                ctx.undoStack.push(state);
                state.applyBatch(updates);
            }
        });
    }

    onDrag(_ctx: ToolContext, _from: Point, _to: Point): void {}
    onMouseUp(_ctx: ToolContext, _cell: Point): void {}

    onHover(ctx: ToolContext, cell: Point): void {
        ctx.renderer.setPreview([{
            col: cell.x,
            row: cell.y,
            cell: {
                char: ctx.appState.selectedChar,
                fg: ctx.appState.fgColor,
                bg: ctx.appState.bgColor,
            }
        }]);
    }

    onMouseLeave(ctx: ToolContext): void {
        ctx.renderer.clearPreview();
    }
}
