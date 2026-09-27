import { AppState, Color } from './types';
import { EditorSettings } from './editorSettings';
import { isValidColor } from './utils/colors';

/** Live editor targets an EditorSettings snapshot restores into. The widget
 * surface is structural (not the concrete classes) so restores are
 * unit-testable without a DOM. */
export interface EditorSettingsTargets {
    appState: AppState;
    fgPicker: { setHistory(colors: Color[]): void };
    bgPicker: { setHistory(colors: Color[]): void };
    gradientPicker: { setStops(stops: Color[]): void };
    charPalette: { addCustomChars(chars: string[]): void; selectChar(char: string): void };
    toolbar: { syncFromState(): void };
    setFontSize(size: number): void;
    applyFont(family: string): Promise<void> | void;
    applyRoom(color: Color, visible: boolean): void;
}

function copyColor(c: Color): Color {
    return [...c] as Color;
}

function isNonEmptyString(v: unknown): v is string {
    return typeof v === 'string' && v.length > 0;
}

/** Allowed values per tool-mode key, mirroring the AppState unions in
 * types.ts (and the server's accepted sets). Unknown values are skipped
 * so a stale or tampered grant keeps the current mode, never junk. */
type ToolStringKey = Exclude<keyof EditorSettings['tools'], 'lineDiagonal'>;
const TOOL_VALUE_SETS: { readonly [K in ToolStringKey]: readonly string[] } = {
    activeToolId: ['brush', 'erase', 'type', 'text', 'rect', 'oval', 'line', 'gradient', 'fill', 'eyedropper', 'select', 'move', 'rotate'],
    rectMode: ['light', 'rounded', 'double', 'heavy', 'custom'],
    ovalMode: ['light', 'rounded', 'double', 'heavy', 'circle', 'custom'],
    lineMode: ['light', 'rounded', 'double', 'heavy', 'custom'],
    gradientTarget: ['foreground', 'background', 'both', 'luminance', 'inverse-luminance'],
    fillMode: ['brush', 'foreground', 'background', 'gradient'],
    eyedropperTarget: ['fg-fg', 'fg-bg', 'bg-fg', 'bg-bg'],
    selectMode: ['single', 'rectangle', 'lasso', 'magic', 'color-match', 'color-fuzzy'],
    rotateMode: ['cw90', 'ccw90', '180', 'flip-h', 'flip-v', 'free'],
    typeStyle: ['regular', 'bold', 'italic', 'underline'],
};

/**
 * Restore a saved editor-settings snapshot into live state and widgets.
 * Malformed parts are skipped, never thrown: a missing piece keeps the
 * current value instead of breaking the editor open. A nullish snapshot
 * (older server, no save yet) is a no-op.
 */
export async function applyEditorSettings(
    settings: EditorSettings | undefined | null,
    deps: EditorSettingsTargets,
): Promise<void> {
    if (!settings || typeof settings !== 'object') return;
    const { appState } = deps;

    if (isValidColor(settings.fgColor)) appState.fgColor = copyColor(settings.fgColor);
    if (isValidColor(settings.bgColor)) appState.bgColor = copyColor(settings.bgColor);
    if (Array.isArray(settings.fgSlots)) deps.fgPicker.setHistory(settings.fgSlots.filter(isValidColor));
    if (Array.isArray(settings.bgSlots)) deps.bgPicker.setHistory(settings.bgSlots.filter(isValidColor));
    if (Array.isArray(settings.gradientStops)) {
        deps.gradientPicker.setStops(settings.gradientStops.filter(isValidColor));
    }

    if (Array.isArray(settings.customChars)) {
        deps.charPalette.addCustomChars(
            settings.customChars.filter((c) => typeof c === 'string' && c.length > 0),
        );
    }
    if (isNonEmptyString(settings.selectedChar)) deps.charPalette.selectChar(settings.selectedChar);

    const tools = settings.tools;
    if (tools && typeof tools === 'object') {
        const take = (key: ToolStringKey): void => {
            const v: unknown = tools[key];
            if (typeof v === 'string' && TOOL_VALUE_SETS[key].includes(v)) {
                (appState as unknown as Record<string, unknown>)[key] = v;
            }
        };
        take('activeToolId');
        take('rectMode');
        take('ovalMode');
        take('lineMode');
        if (typeof tools.lineDiagonal === 'boolean') appState.lineDiagonal = tools.lineDiagonal;
        take('gradientTarget');
        take('fillMode');
        take('eyedropperTarget');
        take('selectMode');
        take('rotateMode');
        take('typeStyle');
        deps.toolbar.syncFromState();
    }

    let fontChanged = false;
    if (isNonEmptyString(settings.fontFamily) && settings.fontFamily !== appState.fontFamily) {
        appState.fontFamily = settings.fontFamily;
        fontChanged = true;
    }
    if (typeof settings.fontSize === 'number' && Number.isFinite(settings.fontSize)) {
        const size = Math.min(256, Math.max(1, Math.round(settings.fontSize)));
        deps.setFontSize(size);
        fontChanged = true;
    }
    // Re-run the font path whenever family or size moved so metrics, the
    // CSS variable, and the palette repaint for the restored values. The
    // toolbar select already shows the family via syncFromState above.
    if (fontChanged) {
        deps.toolbar.syncFromState();
        await deps.applyFont(appState.fontFamily);
    }

    if (isValidColor(settings.roomColor)) {
        deps.applyRoom(
            copyColor(settings.roomColor),
            typeof settings.roomVisible === 'boolean' ? settings.roomVisible : true,
        );
    }
}
