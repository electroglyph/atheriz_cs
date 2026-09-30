import { describe, expect, it } from 'vitest';
import { roomBoundaryEdges } from '../src/canvas/GridRenderer.ts';

function edgeSet(cells: Set<string>): Set<string> {
    return new Set(
        roomBoundaryEdges(cells).map(([x1, y1, x2, y2]) => {
            const a = `${x1},${y1}`;
            const b = `${x2},${y2}`;
            return a < b ? `${a}|${b}` : `${b}|${a}`;
        }),
    );
}

describe('roomBoundaryEdges', () => {
    it('empty set has no boundary', () => {
        expect(roomBoundaryEdges(new Set())).toEqual([]);
    });

    it('single cell returns its four sides', () => {
        const edges = edgeSet(new Set(['2,3']));
        expect(edges.size).toBe(4);
        expect(edges).toContain('2,3|2,4');
        expect(edges).toContain('2,3|3,3');
        expect(edges).toContain('2,4|3,4');
        expect(edges).toContain('3,3|3,4');
    });

    it('shared edges are interior and excluded', () => {
        const edges = edgeSet(new Set(['0,0', '1,0']));
        expect(edges.size).toBe(6);
        expect(edges).not.toContain('1,0|1,1');
    });

    it('2x2 block has eight unit outer edges and no interior ones', () => {
        const edges = edgeSet(new Set(['0,0', '1,0', '0,1', '1,1']));
        expect(edges.size).toBe(8);
        expect(edges).not.toContain('1,0|1,1');
        expect(edges).not.toContain('0,1|1,1');
        expect(edges).toContain('0,0|1,0');
        expect(edges).toContain('1,0|2,0');
        expect(edges).toContain('0,2|1,2');
        expect(edges).toContain('1,2|2,2');
        expect(edges).toContain('0,0|0,1');
        expect(edges).toContain('0,1|0,2');
        expect(edges).toContain('2,0|2,1');
        expect(edges).toContain('2,1|2,2');
    });

    it('L-shape excludes both shared edges', () => {
        const edges = edgeSet(new Set(['0,0', '1,0', '0,1']));
        expect(edges.size).toBe(8);
        expect(edges).not.toContain('1,0|1,1');
        expect(edges).not.toContain('0,1|1,1');
    });

    it('disconnected cells each contribute four edges', () => {
        expect(edgeSet(new Set(['0,0', '5,5'])).size).toBe(8);
    });

    it('malformed keys are skipped', () => {
        const edges = edgeSet(new Set(['0,0', 'nope', '1,', ',2', '']));
        expect(edges.size).toBe(4);
    });

    it('a hole is bounded: removing the center of a ring adds its four sides', () => {
        const ring = new Set(['0,0', '1,0', '2,0', '0,1', '2,1', '0,2', '1,2', '2,2']);
        const full = new Set([...ring, '1,1']);
        // Ring boundary: outer 12 + hole 4 = 16; full 3x3: outer 12.
        expect(edgeSet(ring).size).toBe(16);
        expect(edgeSet(full).size).toBe(12);
    });
});
