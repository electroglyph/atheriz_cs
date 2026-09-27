// @vitest-environment jsdom
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { applyEditorSettings, EditorSettingsTargets } from '../src/applyEditorSettings';
import { buildEditorSettings, EditorSettings } from '../src/editorSettings';
import type { MapEditPayload } from '../src/mapedit';
import { AppState, Color } from '../src/types';

function makeAppState(): AppState {
    return {
        activeToolId: 'brush',
        rectMode: 'light',
        ovalMode: 'light',
        lineMode: 'light',
        gradientTarget: 'foreground',
        typeStyle: 'regular',
        selectedChar: '█',
        fgColor: [204, 204, 204],
        bgColor: [0, 0, 0],
        fontFamily: 'KreativeSquare',
        gradientStops: [[0, 0, 0], [255, 255, 255]],
        selectMode: 'rectangle',
        rotateMode: 'cw90',
        fillMode: 'brush',
        lineDiagonal: false,
        eyedropperTarget: 'fg-fg',
    };
}

function makeSettings(appState: AppState): EditorSettings {
    return buildEditorSettings({
        appState,
        fgSlots: [[10, 20, 30], [40, 50, 60]],
        bgSlots: [[70, 80, 90]],
        customChars: ['Ω', '▓'],
        fontSize: 24,
        roomColor: [0, 204, 204],
        roomVisible: false,
    });
}

function makeStubs(appState: AppState): {
    deps: EditorSettingsTargets;
    captured: {
        fgHistory: Color[][];
        bgHistory: Color[][];
        stops: Color[][];
        addedChars: string[][];
        selected: string[];
        fontSize: number;
        fontFamily: string | null;
        room: { color: Color; visible: boolean } | null;
    };
} {
    const captured = {
        fgHistory: [] as Color[][],
        bgHistory: [] as Color[][],
        stops: [] as Color[][],
        addedChars: [] as string[][],
        selected: [] as string[],
        fontSize: 18,
        fontFamily: null as string | null,
        room: null as { color: Color; visible: boolean } | null,
    };
    const deps: EditorSettingsTargets = {
        appState,
        fgPicker: { setHistory: vi.fn((c: Color[]) => { captured.fgHistory.push(c); }) },
        bgPicker: { setHistory: vi.fn((c: Color[]) => { captured.bgHistory.push(c); }) },
        gradientPicker: { setStops: vi.fn((s: Color[]) => {
            captured.stops.push(s);
            // The real GradientPicker writes valid stops through to appState
            // and ignores anything shorter than two stops.
            if (s.length >= 2) appState.gradientStops = s.map((c) => [...c] as Color);
        }) },
        charPalette: {
            addCustomChars: vi.fn((c: string[]) => { captured.addedChars.push(c); }),
            selectChar: vi.fn((c: string) => { captured.selected.push(c); appState.selectedChar = c; }),
        },
        toolbar: { syncFromState: vi.fn(() => {}) },
        setFontSize: vi.fn((size: number) => { captured.fontSize = size; }),
        applyFont: vi.fn(async (family: string) => { captured.fontFamily = family; }),
        applyRoom: vi.fn((color: Color, visible: boolean) => { captured.room = { color, visible }; }),
    };
    return { deps, captured };
}

describe('applyEditorSettings restores a saved snapshot', () => {
    it('applies every field to state, widgets, font, and room', async () => {
        const saved = makeAppState();
        saved.rectMode = 'heavy';
        saved.lineDiagonal = true;
        saved.fillMode = 'gradient';
        saved.fontFamily = 'Fira Code';
        saved.selectedChar = 'Ω';
        const settings = makeSettings(saved);

        const appState = makeAppState();
        const { deps, captured } = makeStubs(appState);
        await applyEditorSettings(settings, deps);

        expect(appState.fgColor).toEqual([204, 204, 204]);
        expect(appState.rectMode).toBe('heavy');
        expect(appState.lineDiagonal).toBe(true);
        expect(appState.fillMode).toBe('gradient');
        expect(appState.fontFamily).toBe('Fira Code');
        expect(captured.fgHistory).toEqual([[[10, 20, 30], [40, 50, 60]]]);
        expect(captured.bgHistory).toEqual([[[70, 80, 90]]]);
        expect(captured.stops).toEqual([[[0, 0, 0], [255, 255, 255]]]);
        expect(captured.addedChars).toEqual([['Ω', '▓']]);
        expect(captured.selected).toEqual(['Ω']);
        expect(vi.mocked(deps.toolbar.syncFromState).mock.calls.length).toBeGreaterThanOrEqual(1);
        expect(captured.fontSize).toBe(24);
        expect(captured.fontFamily).toBe('Fira Code');
        expect(captured.room).toEqual({ color: [0, 204, 204], visible: false });
    });

    it('copies colors instead of aliasing the snapshot', async () => {
        const settings = makeSettings(makeAppState());
        const appState = makeAppState();
        const { deps, captured } = makeStubs(appState);
        await applyEditorSettings(settings, deps);

        expect(appState.fgColor).not.toBe(settings.fgColor);
        expect(captured.room!.color).not.toBe(settings.roomColor);
        settings.fgColor[0] = 1;
        expect(appState.fgColor[0]).toBe(204);
    });

    it('round-trips save output back into default state', async () => {
        const saved = makeAppState();
        saved.activeToolId = 'line';
        saved.rectMode = 'double';
        saved.ovalMode = 'circle';
        saved.gradientTarget = 'both';
        saved.typeStyle = 'bold';
        saved.selectMode = 'lasso';
        saved.rotateMode = 'free';
        saved.eyedropperTarget = 'bg-bg';
        saved.fgColor = [1, 2, 3];
        saved.bgColor = [4, 5, 6];
        saved.selectedChar = 'Ω';
        saved.fontFamily = 'Unifont';
        saved.gradientStops = [[9, 9, 9], [8, 8, 8], [7, 7, 7]];
        const settings = makeSettings(saved);

        const appState = makeAppState();
        const { deps, captured } = makeStubs(appState);
        await applyEditorSettings(settings, deps);

        for (const key of Object.keys(saved) as (keyof AppState)[]) {
            expect(appState[key]).toEqual(saved[key]);
        }
        expect(captured.fontSize).toBe(24);
        expect(captured.room).toEqual({ color: [0, 204, 204], visible: false });
    });

    it('ignores nullish snapshots without touching anything', async () => {
        const appState = makeAppState();
        const before = structuredClone(appState);
        const { deps, captured } = makeStubs(appState);
        await applyEditorSettings(undefined, deps);
        await applyEditorSettings(null, deps);
        expect(appState).toEqual(before);
        expect(deps.fgPicker.setHistory).not.toHaveBeenCalled();
        expect(deps.toolbar.syncFromState).not.toHaveBeenCalled();
        expect(deps.applyFont).not.toHaveBeenCalled();
        expect(deps.applyRoom).not.toHaveBeenCalled();
    });

    it('skips malformed parts and keeps current values', async () => {
        const appState = makeAppState();
        const before = structuredClone(appState);
        const { deps, captured } = makeStubs(appState);
        const garbage = {
            fgColor: 'red',
            bgColor: [0, 0],
            fgSlots: 'nope',
            gradientStops: [[1, 2, 3]],
            customChars: [42],
            selectedChar: '',
            fontFamily: 7,
            fontSize: NaN,
            tools: { rectMode: 'wavy', lineDiagonal: 'yes', typeStyle: 'cursive' },
            roomColor: [300, 0, 0],
            roomVisible: 'x',
        } as unknown as EditorSettings;
        await applyEditorSettings(garbage, deps);

        expect(appState).toEqual(before);
        expect(deps.setFontSize).not.toHaveBeenCalled();
        expect(deps.applyFont).not.toHaveBeenCalled();
        expect(deps.applyRoom).not.toHaveBeenCalled();
    });
});

describe('restore widget setters against real widgets', () => {
    beforeEach(() => {
        localStorage.clear();
        document.body.innerHTML = `
            <div id="rs-fg"></div><div id="rs-bg"></div><div id="rs-grad"></div>
            <div id="rs-palette"></div>
            <button id="tool-brush"></button><button id="tool-erase"></button><button id="tool-type"></button><button id="tool-text"></button><button id="tool-rect"></button><button id="tool-oval"></button><button id="tool-line"></button><button id="tool-gradient"></button><button id="tool-fill"></button><button id="tool-eyedropper"></button><button id="tool-select"></button><button id="tool-move"></button><button id="tool-rotate"></button>
            <select id="type-style-select"><option>regular</option><option>bold</option></select>
            <select id="rect-mode-select"><option>light</option><option>heavy</option></select>
            <select id="oval-mode-select"><option>light</option><option>circle</option></select>
            <select id="line-mode-select"><option>light</option><option>double</option></select>
            <select id="gradient-target-select"><option>foreground</option><option>both</option></select>
            <select id="fill-mode-select"><option>brush</option><option>gradient</option></select>
            <select id="eyedropper-target-select"><option>fg-fg</option><option>bg-bg</option></select>
            <select id="select-mode-select"><option>rectangle</option><option>lasso</option></select>
            <select id="rotate-mode-select"><option>cw90</option><option>free</option></select>
            <button id="btn-undo"></button><button id="btn-redo"></button><button id="btn-export"></button>
            <select id="font-select"></select>
            <input type="checkbox" id="line-diagonal-checkbox" />`;
    });

    afterEach(() => {
        document.body.innerHTML = '';
        vi.restoreAllMocks();
    });

    it('restores colors, slots, stops, palette, tools, font, and room end to end', async () => {
        const { ColorPicker } = await import('../src/ui/ColorPicker');
        const { GradientPicker } = await import('../src/ui/GradientPicker');
        const { CharPalette } = await import('../src/ui/CharPalette');
        const { Toolbar } = await import('../src/ui/Toolbar');
        const { UndoStack } = await import('../src/state/UndoStack');

        const saved = makeAppState();
        saved.fgColor = [11, 22, 33];
        saved.rectMode = 'heavy';
        saved.lineDiagonal = true;
        saved.fontFamily = 'RestoredFamily';
        saved.selectedChar = 'Ω';
        const settings = buildEditorSettings({
            appState: saved,
            fgSlots: [[11, 22, 33], [44, 55, 66]],
            bgSlots: [[77, 88, 99]],
            customChars: ['Ω'],
            fontSize: 22,
            roomColor: [9, 9, 9],
            roomVisible: true,
        });

        const appState = makeAppState();
        const fgPicker = new ColorPicker('rs-fg', true, appState, () => {});
        const bgPicker = new ColorPicker('rs-bg', false, appState, () => {});
        const gradientPicker = new GradientPicker('rs-grad', appState);
        const palette = new CharPalette('rs-palette', appState, () => {});
        const toolbar = new Toolbar(appState, new UndoStack(), () => {}, () => {}, () => {});

        let fontSize = 18;
        let fontFamily: string | null = null;
        let room: { color: Color; visible: boolean } | null = null;
        await applyEditorSettings(settings, {
            appState,
            fgPicker,
            bgPicker,
            gradientPicker,
            charPalette: palette,
            toolbar,
            setFontSize: (n) => { fontSize = n; },
            applyFont: async (f) => { fontFamily = f; },
            applyRoom: (c, v) => { room = { color: c, visible: v }; },
        });

        expect(appState.fgColor).toEqual([11, 22, 33]);
        expect(fgPicker.getHistory()[0]).toEqual([11, 22, 33]);
        expect(fgPicker.getHistory()).toHaveLength(8);
        expect(bgPicker.getHistory()[0]).toEqual([77, 88, 99]);
        expect(appState.rectMode).toBe('heavy');
        expect((document.getElementById('rect-mode-select') as HTMLSelectElement).value).toBe('heavy');
        expect((document.getElementById('line-diagonal-checkbox') as HTMLInputElement).checked).toBe(true);
        expect(document.getElementById('tool-rect')!.classList.contains('active')).toBe(false);
        expect(document.getElementById('tool-brush')!.classList.contains('active')).toBe(true);
        expect(palette.getCustomChars()).toContain('Ω');
        expect(appState.selectedChar).toBe('Ω');
        const fontSelect = document.getElementById('font-select') as HTMLSelectElement;
        expect(fontSelect.value).toBe('RestoredFamily');
        expect(fontSize).toBe(22);
        expect(fontFamily).toBe('RestoredFamily');
        expect(room).toEqual({ color: [9, 9, 9], visible: true });

        fgPicker.destroy();
        bgPicker.destroy();
        toolbar.destroy();
    });

    it('setHistory pads short slot lists to 8 and drops invalid entries', async () => {
        const { ColorPicker } = await import('../src/ui/ColorPicker');
        const appState = makeAppState();
        const picker = new ColorPicker('rs-fg', true, appState, () => {});
        picker.setHistory([[1, 2, 3], [300, 0, 0] as unknown as Color, [4, 5, 6]]);
        const history = picker.getHistory();
        expect(history).toHaveLength(8);
        expect(history[0]).toEqual([1, 2, 3]);
        expect(history[1]).toEqual([4, 5, 6]);
        picker.destroy();
    });

    it('setStops ignores lists shorter than two stops', async () => {
        const { GradientPicker } = await import('../src/ui/GradientPicker');
        const appState = makeAppState();
        const picker = new GradientPicker('rs-grad', appState);
        picker.setStops([[1, 2, 3]]);
        expect(appState.gradientStops).toEqual([[0, 0, 0], [255, 255, 255]]);
        picker.setStops([[1, 2, 3], [4, 5, 6], [7, 8, 9]]);
        expect(appState.gradientStops).toEqual([[1, 2, 3], [4, 5, 6], [7, 8, 9]]);
    });

    it('round-trips through real wire JSON: server bytes in, restored state out', async () => {
        // Byte shape the server emits for launch_draw: [cmd, [key, payload], kwargs].
        // The settings key must survive JSON serialization exactly as written here.
        const wire = '["launch_draw", ["grant-key", {"area": "WireArea", "z": 0, "grid": [], '
            + '"editorSettings": {"fgColor": [11, 22, 33], "bgColor": [4, 5, 6], '
            + '"fgSlots": [[11, 22, 33]], "bgSlots": [[4, 5, 6]], '
            + '"gradientStops": [[0, 0, 0], [255, 255, 255]], "customChars": ["Ω"], '
            + '"selectedChar": "Ω", "fontFamily": "WireFamily", "fontSize": 21, '
            + '"tools": {"activeToolId": "rect", "rectMode": "heavy", "ovalMode": "circle", '
            + '"lineMode": "double", "lineDiagonal": true, "gradientTarget": "both", '
            + '"fillMode": "gradient", "eyedropperTarget": "fg-bg", "selectMode": "rectangle", '
            + '"rotateMode": "cw90", "typeStyle": "bold"}, '
            + '"roomColor": [0, 204, 204], "roomVisible": false}}], {}]';
        const parsed = JSON.parse(wire) as unknown[];
        const grant = (parsed[1] as unknown[])[1] as MapEditPayload;
        expect(Object.keys(grant)).toContain('editorSettings');

        const appState = makeAppState();
        const { deps, captured } = makeStubs(appState);
        // Same guard shape as the draw tab boot path.
        if (grant.editorSettings) {
            await applyEditorSettings(grant.editorSettings, deps);
        }
        expect(appState.fgColor).toEqual([11, 22, 33]);
        expect(appState.fontFamily).toBe('WireFamily');
        expect(appState.rectMode).toBe('heavy');
        expect(appState.lineDiagonal).toBe(true);
        expect(appState.selectedChar).toBe('Ω');
        expect(captured.fontSize).toBe(21);
        expect(captured.room).toEqual({ color: [0, 204, 204], visible: false });
        expect(captured.addedChars).toEqual([['Ω']]);
    });

    it('restores from the editorSettings key the server embeds in the grant', async () => {
        const saved = makeAppState();
        saved.fontFamily = 'GrantFamily';
        saved.rectMode = 'heavy';
        // Wire shape from DrawCommand: the settings ride the launch_draw
        // grant payload under editorSettings. Typed as MapEditPayload so a
        // rename of the field fails compilation here.
        const grant: MapEditPayload = {
            area: 'A',
            z: 0,
            grid: [],
            editorSettings: makeSettings(saved),
        };
        const appState = makeAppState();
        const { deps, captured } = makeStubs(appState);
        if (grant.editorSettings) {
            await applyEditorSettings(grant.editorSettings, deps);
        }
        expect(appState.fontFamily).toBe('GrantFamily');
        expect(appState.rectMode).toBe('heavy');
        expect(captured.fontFamily).toBe('GrantFamily');
    });
});
