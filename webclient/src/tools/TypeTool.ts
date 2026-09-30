import { Tool, ToolContext } from './Tool';
import { Point } from '../types';
import { TypeToolModal } from '../ui/TypeToolModal';
import { ensureToolCapacity } from '../canvas/ensureCapacity';

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

            // Grow the viewport past the violet line once for the whole
            // string instead of clipping; only 2048-capped glyphs are
            // still dropped. One undo entry covers the confirm. Paint in
            // the post-growth frame (see BrushTool.onMouseDown).
            const cap = ensureToolCapacity(ctx, glyphs.map((_, i) => ({ col: anchorX + i, row: anchorY })));
            const sh = cap.shift;
            const atRow = anchorY + sh.row;

            for (let i = 0; i < glyphs.length; i++) {
                const col = anchorX + i + sh.col;
                if (cap.dropped.has(`${col},${atRow}`)) continue;
                const cellData: { char: string; fg: [number, number, number]; bg: [number, number, number]; bold?: boolean; italic?: boolean; underline?: boolean } = {
                    char: glyphs[i]!,
                    fg: [...ctx.appState.fgColor] as [number, number, number],
                    bg: [...ctx.appState.bgColor] as [number, number, number],
                };

                if (style === 'bold') cellData.bold = true;
                else if (style === 'italic') cellData.italic = true;
                else if (style === 'underline') cellData.underline = true;

                updates.push({ col, row: atRow, cell: cellData });
            }

            // One undo entry per confirm: the helper already pushed when the
            // grid grew, otherwise push when something actually paints.
            if (!cap.pushed && updates.length > 0) {
                ctx.undoStack.push(state);
            }
            if (updates.length > 0) {
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
