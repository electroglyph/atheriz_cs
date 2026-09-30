import { describe, expect, it } from 'vitest';
import { CanvasState } from '../src/state/CanvasState';
import { AnsiExporter } from '../src/export/AnsiExporter';
import { buildCompositeAnsiPreview } from '../src/export/AnsiPreview';
import type { Cell } from '../src/types';

const ink = (char: string): Cell => ({ char, fg: [255, 255, 255], bg: [0, 0, 0] });

function croppedCanvas(): CanvasState {
    // 4x4 viewport, violet grid inset to the middle 2x2.
    const state = new CanvasState(4, 4);
    state.applyBatch([
        { col: 0, row: 0, cell: ink('O') },
        { col: 1, row: 1, cell: ink('I') },
    ]);
    state.setServerBounds({ col: 1, row: 1, w: 2, h: 2 });
    return state;
}

describe('exporter server-grid crop', () => {
    it('exports the full viewport when bounds are full (default)', () => {
        const state = new CanvasState(2, 2);
        state.applyBatch([{ col: 0, row: 0, cell: ink('A') }]);
        const out = AnsiExporter.export(state);
        expect(out.startsWith('\x1b[8;2;2t')).toBe(true);
        expect(out).toContain('A');
        expect(out).toContain('\x1b[1;1H');
    });

    it('crops the background raster to the violet rect', () => {
        const out = AnsiExporter.export(croppedCanvas());
        expect(out.startsWith('\x1b[8;2;2t')).toBe(true);
        expect(out).toContain('I');
        expect(out).not.toContain('O');
        // Only two raster rows, 1-based within the crop.
        expect(out).toContain('\x1b[1;1H');
        expect(out).toContain('\x1b[2;1H');
        expect(out).not.toContain('\x1b[3;1H');
    });

    it('rebases overlay cursor addresses into the crop', () => {
        const state = croppedCanvas();
        state.addLayer('overlay');
        // Viewport (2,1) is crop-local (1,0), so 1-based address 1;2.
        state.applyBatch([{ col: 2, row: 1, cell: ink('X') }]);
        const out = AnsiExporter.export(state);
        expect(out).toContain('\x1b[1;2H');
        expect(out).not.toContain('\x1b[2;3H');
    });

    it('composites the preview from the violet rect only', () => {
        const out = buildCompositeAnsiPreview(croppedCanvas());
        expect(out).toContain('I');
        expect(out).not.toContain('O');
        expect(out).toContain('\x1b[2;1H');
        expect(out).not.toContain('\x1b[3;1H');
    });
});
