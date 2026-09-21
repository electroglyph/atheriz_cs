// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { CanvasState } from '../src/state/CanvasState';
import { UndoStack } from '../src/state/UndoStack';
import { AppState, Color } from '../src/types';
import { GridRenderer } from '../src/canvas/GridRenderer';
import { FillTool } from '../src/tools/FillTool';
import { GradientTool } from '../src/tools/GradientTool';
import { SelectionTool } from '../src/tools/SelectionTool';
import { parseCellKey } from '../src/utils/cellKeys';
import { cssColor, hexToRgb, lerpColor, rgbToHex, sampleGradient } from '../src/utils/colors';
import { asLegend, asMapPayload, asPosition } from '../src/webclient/payload';
import { wrapText } from '../src/webclient/text';

function makeAppState(overrides: Partial<AppState> = {}): AppState {
    return {
        activeToolId: 'select',
        rectMode: 'light',
        ovalMode: 'light',
        lineMode: 'light',
        gradientTarget: 'foreground',
        typeStyle: 'regular',
        selectedChar: 'x',
        fgColor: [255, 255, 255],
        bgColor: [0, 0, 0],
        fontFamily: 'monospace',
        gradientStops: [[0, 0, 0], [255, 255, 255]],
        selectMode: 'rectangle',
        rotateMode: 'cw90',
        fillMode: 'brush',
        lineDiagonal: true,
        eyedropperTarget: 'fg-fg',
        ...overrides,
    };
}

function makeRenderer(selected: Set<string> = new Set()): GridRenderer {
    let sel = selected;
    return {
        getSelectedCells: () => sel,
        setSelection: (s: Set<string>) => { sel = s; },
        clearSelection: () => { sel = new Set(); },
        setPreview: () => {},
        clearPreview: () => {},
        getRoomCells: () => new Set<string>(),
        setRoomCells: () => {},
    } as unknown as GridRenderer;
}

function makeCtx(state: CanvasState, overrides: Partial<AppState> = {}, selected?: Set<string>) {
    return {
        state,
        undoStack: new UndoStack(),
        renderer: makeRenderer(selected),
        appState: makeAppState(overrides),
        modifiers: { shiftKey: false, altKey: false, ctrlKey: false },
    };
}

describe('parseCellKey rejects malformed keys', () => {
    it('parses well-formed keys', () => {
        expect(parseCellKey('0,0')).toEqual({ col: 0, row: 0 });
        expect(parseCellKey('12,34')).toEqual({ col: 12, row: 34 });
    });

    it('returns null for malformed keys', () => {
        expect(parseCellKey('oops')).toBeNull();
        expect(parseCellKey('')).toBeNull();
        expect(parseCellKey('NaN,NaN')).toBeNull();
        expect(parseCellKey('1,')).toBeNull();
        expect(parseCellKey(',2')).toBeNull();
        expect(parseCellKey('a,b')).toBeNull();
    });
});

describe('NaN cell keys are skipped, never indexed', () => {
    it('FillTool.applyFill skips malformed keys without throwing', () => {
        const state = new CanvasState(4, 4);
        const ctx = makeCtx(state, { fillMode: 'brush' });
        const tool = new FillTool() as unknown as {
            applyFill: (ctx: unknown, targets: Set<string>) => { col: number; row: number }[];
        };
        let updates: { col: number; row: number }[] = [];
        expect(() => {
            updates = tool.applyFill(ctx as never, new Set(['oops', '1,1', 'NaN,NaN', '2,']));
        }).not.toThrow();
        expect(updates).toHaveLength(1);
        expect(updates[0]).toMatchObject({ col: 1, row: 1 });
    });

    it('SelectionTool delete skips malformed keys without throwing', () => {
        const state = new CanvasState(4, 4);
        state.setCell(0, 0, { char: 'x', fg: [255, 255, 255], bg: [-1, -1, -1] });
        const ctx = makeCtx(state);
        const tool = new SelectionTool();
        tool.setSelection(new Set(['bad-key', '0,0', 'NaN,NaN']));
        let result = false;
        expect(() => {
            result = tool.onKeyDown(ctx as never, 'Delete');
        }).not.toThrow();
        expect(result).toBe(true);
        expect(state.getCell(0, 0)?.char).toBe('');
    });
});

describe('CanvasState guards', () => {
    it('constructor throws RangeError on non-finite dimensions', () => {
        expect(() => new CanvasState(NaN, 5)).toThrow(RangeError);
        expect(() => new CanvasState(5, Infinity)).toThrow(RangeError);
        expect(() => new CanvasState(-Infinity, 5)).toThrow(RangeError);
    });

    it('constructor coerces dimensions to integers >= 1 and caps at 2048', () => {
        expect(new CanvasState(0, 0).width).toBe(1);
        expect(new CanvasState(2.9, 3.7).width).toBe(2);
        expect(new CanvasState(2.9, 3.7).height).toBe(3);
        const capped = new CanvasState(5000, 2);
        expect(capped.width).toBe(CanvasState.MAX_DIMENSION);
        expect(capped.height).toBe(2);
    });

    it('getActiveLayer lazily recreates the background when layers are empty', () => {
        const state = new CanvasState(3, 3);
        state.layers = [];
        const layer = state.getActiveLayer();
        expect(state.layers).toHaveLength(1);
        expect(layer.name).toBe('Background');
    });

    it('getActiveLayer clamps a stale index into range', () => {
        const state = new CanvasState(3, 3);
        state.addLayer('Top');
        state.activeLayerIndex = 99;
        expect(state.getActiveLayer().name).toBe('Top');
        state.activeLayerIndex = -5;
        expect(state.getActiveLayer().name).toBe('Background');
    });
});

describe('payload validation', () => {
    it('asPosition rejects NaN/Infinity but keeps floats', () => {
        expect(asPosition([NaN, 1])).toBeUndefined();
        expect(asPosition([1, Infinity])).toBeUndefined();
        expect(asPosition([-Infinity, -Infinity])).toBeUndefined();
        expect(asPosition(['1', 2])).toBeUndefined();
        expect(asPosition([1])).toBeUndefined();
        expect(asPosition([1.5, 2.5])).toEqual([1.5, 2.5]);
    });

    it('asMapPayload defaults non-finite min_x/max_y to 0', () => {
        expect(asMapPayload({ map: 'm', min_x: NaN }).min_x).toBe(0);
        expect(asMapPayload({ map: 'm', max_y: Infinity }).max_y).toBe(0);
        expect(asMapPayload({ map: 'm', min_x: 3.5 }).min_x).toBe(3.5);
    });

    it('asLegend truncates overlong symbols and descriptions', () => {
        const longDesc = 'd'.repeat(300);
        const longSymbol = 's'.repeat(100);
        const [obj] = asLegend([{ symbol: longSymbol, desc: longDesc }]);
        expect(obj.desc).toHaveLength(256);
        expect(obj.symbol).toHaveLength(64);
        const [arr] = asLegend([[longSymbol, longDesc]]);
        expect(arr.desc).toHaveLength(256);
        expect(arr.symbol).toHaveLength(64);
    });
});

describe('wrapText NaN width', () => {
    it('returns text unchanged for NaN width', () => {
        expect(wrapText('hello world', NaN)).toBe('hello world');
    });
});

describe('color validation and clamping', () => {
    it('rgbToHex clamps channels so output is always valid CSS', () => {
        expect(rgbToHex([300, -20, 128])).toBe('#ff0080');
        expect(rgbToHex([NaN, Infinity, 16])).toBe('#000010');
    });

    it('hexToRgb strictly parses 3/6-digit hex and returns null otherwise', () => {
        expect(hexToRgb('#fff')).toEqual([255, 255, 255]);
        expect(hexToRgb('ff0000')).toEqual([255, 0, 0]);
        expect(hexToRgb('#FF00aa')).toEqual([255, 0, 170]);
        expect(hexToRgb('zzz')).toBeNull();
        expect(hexToRgb('#ff')).toBeNull();
        expect(hexToRgb('#11223344')).toBeNull();
        expect(hexToRgb('')).toBeNull();
        expect(hexToRgb('#gggggg')).toBeNull();
    });

    it('lerpColor treats non-finite t as 0 and clamps outputs', () => {
        expect(lerpColor([0, 0, 0], [255, 255, 255], NaN)).toEqual([0, 0, 0]);
        expect(lerpColor([0, 0, 0], [255, 255, 255], 2)).toEqual([255, 255, 255]);
    });

    it('sampleGradient treats non-finite t as 0 and clamps single stops', () => {
        expect(sampleGradient([], 0.5)).toEqual([0, 0, 0]);
        expect(sampleGradient([[300, -1, 0] as Color], 0)).toEqual([255, 0, 0]);
        expect(sampleGradient([[0, 0, 0], [255, 255, 255]], NaN)).toEqual([0, 0, 0]);
    });

    it('cssColor clamps channels', () => {
        expect(cssColor([-1, -1, -1])).toBe('rgb(0, 0, 0)');
        expect(cssColor([300, 0, 128])).toBe('rgb(255, 0, 128)');
    });
});

describe('GridRenderer selection copy and transparent preview', () => {
    function makeProbedRenderer(state: CanvasState) {
        const record = { fills: [] as string[], moves: [] as Array<[number, number]> };
        if ((globalThis as Record<string, unknown>).Path2D === undefined) {
            (globalThis as Record<string, unknown>).Path2D = class { rect() {} };
        }
        const ctx = new Proxy({}, {
            get: (_t, prop: string | symbol) => {
                if (prop === 'canvas') return undefined;
                return (...args: unknown[]) => {
                    if ((prop === 'moveTo' || prop === 'lineTo') && typeof args[0] === 'number' && typeof args[1] === 'number') {
                        record.moves.push([args[0], args[1]]);
                    }
                };
            },
            set: (_t, prop: string | symbol, value: unknown) => {
                if (prop === 'fillStyle' && typeof value === 'string') record.fills.push(value);
                return true;
            },
        }) as unknown as CanvasRenderingContext2D;
        const canvas = {
            width: 0,
            height: 0,
            style: {},
            getContext: () => ctx,
        } as unknown as HTMLCanvasElement;
        const renderer = new GridRenderer(canvas, state, { width: 8, height: 16, font: '16px monospace', advance: 8 });
        return { renderer, record };
    }

    it('setSelection copies the caller set instead of aliasing it', () => {
        const state = new CanvasState(2, 2);
        const { renderer } = makeProbedRenderer(state);
        const caller = new Set(['0,0']);
        renderer.setSelection(caller);
        caller.add('1,1');
        caller.add('poison');
        expect(renderer.getSelectedCells().has('1,1')).toBe(false);
        expect(renderer.getSelectedCells().has('poison')).toBe(false);
        expect(renderer.getSelectedCells().has('0,0')).toBe(true);
    });

    it('malformed outline keys are skipped (no NaN coordinates)', () => {
        const state = new CanvasState(2, 2);
        const { renderer, record } = makeProbedRenderer(state);
        expect(() => renderer.setSelection(new Set(['bad', 'NaN,NaN', '0,0']))).not.toThrow();
        expect(record.moves.length).toBeGreaterThan(0);
        for (const [x, y] of record.moves) {
            expect(Number.isFinite(x)).toBe(true);
            expect(Number.isFinite(y)).toBe(true);
        }
    });

    it('transparent preview cells resolve instead of emitting rgb(-1,-1,-1)', () => {
        const state = new CanvasState(2, 2);
        const { renderer, record } = makeProbedRenderer(state);
        expect(() => renderer.setPreview([
            { col: 0, row: 0, cell: { char: '', fg: [204, 204, 204], bg: [-1, -1, -1] } },
        ])).not.toThrow();
        for (const fill of record.fills) {
            expect(fill).not.toContain('-1');
        }
    });
});

describe('gradient both-target, empty stops, and black-is-ink', () => {
    const stops: Color[] = [[0, 0, 0], [255, 255, 255]];

    function inkBgCtx(state: CanvasState, target: AppState['gradientTarget'], gradientStops: Color[]) {
        return makeCtx(state, { fillMode: 'gradient', gradientTarget: target, gradientStops });
    }

    it("FillTool 'both' sets both fg and bg to the gradient color", () => {
        const state = new CanvasState(3, 3);
        state.setCell(1, 1, { char: '', fg: [10, 10, 10], bg: [200, 0, 0] });
        const tool = new FillTool() as unknown as {
            fillCells: Set<string>;
            applyGradientFill: (ctx: unknown, start: { x: number; y: number }, end: { x: number; y: number }) => { cell: { fg: Color; bg: Color } }[];
        };
        tool.fillCells = new Set(['1,1']);
        const ctx = inkBgCtx(state, 'both', stops);
        const [update] = tool.applyGradientFill(ctx as never, { x: 0, y: 0 }, { x: 2, y: 0 });
        expect(update.cell.fg).toEqual([128, 128, 128]);
        expect(update.cell.bg).toEqual([128, 128, 128]);
    });

    it("GradientTool 'both' sets both fg and bg to the gradient color", () => {
        const state = new CanvasState(3, 3);
        state.setCell(1, 1, { char: '', fg: [10, 10, 10], bg: [200, 0, 0] });
        const tool = new GradientTool() as unknown as {
            getGradientUpdates: (ctx: unknown, start: { x: number; y: number }, end: { x: number; y: number }) => { col: number; row: number; cell: { fg: Color; bg: Color } }[];
        };
        const ctx = inkBgCtx(state, 'both', stops);
        const updates = tool.getGradientUpdates(ctx as never, { x: 0, y: 0 }, { x: 2, y: 0 });
        const target = updates.find((u) => u.col === 1 && u.row === 1);
        expect(target).toBeDefined();
        expect(target?.cell.fg).toEqual([128, 128, 128]);
        expect(target?.cell.bg).toEqual([128, 128, 128]);
    });

    it('empty gradient stops fall back to the black-white default', () => {
        const state = new CanvasState(3, 3);
        state.setCell(1, 1, { char: 'x', fg: [10, 10, 10], bg: [-1, -1, -1] });
        const tool = new GradientTool() as unknown as {
            getGradientUpdates: (ctx: unknown, start: { x: number; y: number }, end: { x: number; y: number }) => { col: number; row: number; cell: { fg: Color } }[];
        };
        const ctx = makeCtx(state, { gradientTarget: 'foreground', gradientStops: [] });
        let updates: { col: number; row: number; cell: { fg: Color } }[] = [];
        expect(() => {
            updates = tool.getGradientUpdates(ctx as never, { x: 0, y: 0 }, { x: 2, y: 0 });
        }).not.toThrow();
        const target = updates.find((u) => u.col === 1 && u.row === 1);
        expect(target?.cell.fg).toEqual([128, 128, 128]);
    });

    it('opaque black bg counts as ink (blocks flood, kept by gradient)', () => {
        const state = new CanvasState(5, 5);
        state.setCell(2, 2, { char: '', fg: [204, 204, 204], bg: [0, 0, 0] });
        const fill = new FillTool() as unknown as {
            floodFill: (ctx: unknown, p: { x: number; y: number }) => Set<string>;
        };
        const ctx = makeCtx(state, { fillMode: 'brush' });
        const visited = fill.floodFill(ctx as never, { x: 0, y: 0 });
        expect(visited.has('2,2')).toBe(false);

        const gradient = new GradientTool() as unknown as {
            getGradientUpdates: (ctx: unknown, start: { x: number; y: number }, end: { x: number; y: number }) => { col: number; row: number }[];
        };
        const gUpdates = gradient.getGradientUpdates(ctx as never, { x: 0, y: 0 }, { x: 4, y: 0 });
        expect(gUpdates.some((u) => u.col === 2 && u.row === 2)).toBe(true);
    });
});
