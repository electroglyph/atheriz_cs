import { AppState, Color } from './types';

/** Tool-mode half of the saved editor settings (mirrors AppState tool fields). */
export interface EditorToolSettings {
    activeToolId: string;
    rectMode: string;
    ovalMode: string;
    lineMode: string;
    lineDiagonal: boolean;
    gradientTarget: string;
    fillMode: string;
    eyedropperTarget: string;
    selectMode: string;
    rotateMode: string;
    typeStyle: string;
}

/** Editor chrome sent with an explicit save-to-server and stored on the account. */
export interface EditorSettings {
    fgColor: Color;
    bgColor: Color;
    fgSlots: Color[];
    bgSlots: Color[];
    gradientStops: Color[];
    customChars: string[];
    selectedChar: string;
    fontFamily: string;
    fontSize: number;
    tools: EditorToolSettings;
    roomColor: Color;
    roomVisible: boolean;
}

export interface EditorSettingsInput {
    appState: AppState;
    fgSlots: Color[];
    bgSlots: Color[];
    customChars: string[];
    fontSize: number;
    roomColor: Color;
    roomVisible: boolean;
}

function copyColor(c: Color): Color {
    return [...c] as Color;
}

/** Snapshot the live editor state into a sendable settings object (copies every array). */
export function buildEditorSettings(input: EditorSettingsInput): EditorSettings {
    const appState = input.appState;
    return {
        fgColor: copyColor(appState.fgColor),
        bgColor: copyColor(appState.bgColor),
        fgSlots: input.fgSlots.map(copyColor),
        bgSlots: input.bgSlots.map(copyColor),
        gradientStops: appState.gradientStops.map(copyColor),
        customChars: [...input.customChars],
        selectedChar: appState.selectedChar,
        fontFamily: appState.fontFamily,
        fontSize: input.fontSize,
        tools: {
            activeToolId: appState.activeToolId,
            rectMode: appState.rectMode,
            ovalMode: appState.ovalMode,
            lineMode: appState.lineMode,
            lineDiagonal: appState.lineDiagonal,
            gradientTarget: appState.gradientTarget,
            fillMode: appState.fillMode,
            eyedropperTarget: appState.eyedropperTarget,
            selectMode: appState.selectMode,
            rotateMode: appState.rotateMode,
            typeStyle: appState.typeStyle,
        },
        roomColor: copyColor(input.roomColor),
        roomVisible: input.roomVisible,
    };
}
