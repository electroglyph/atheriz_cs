import { describe, expect, it, vi } from 'vitest';
import { WebSocketLike } from '../src/webclient/connection';
import { CanvasState } from '../src/state/CanvasState';
import { MapEditSession } from '../src/mapedit';
import { buildEditorSettings, EditorSettings } from '../src/editorSettings';
import { AppState, Color } from '../src/types';

class FakeSocket implements WebSocketLike {
    readyState = 0;
    onopen: ((event: Event) => void) | null = null;
    onclose: ((event: CloseEvent) => void) | null = null;
    onerror: ((event: Event) => void) | null = null;
    onmessage: ((event: MessageEvent) => void) | null = null;
    sent: string[] = [];
    closeCalls = 0;

    send(data: string): void {
        this.sent.push(data);
    }

    close(): void {
        this.closeCalls += 1;
    }

    open(): void {
        this.readyState = 1;
        this.onopen?.(new Event('open'));
    }
}

function makeSocketHolder(): { socket: FakeSocket; createSocket: (url: string) => WebSocketLike } {
    let socket!: FakeSocket;
    return {
        get socket() {
            return socket;
        },
        createSocket: () => {
            socket = new FakeSocket();
            return socket;
        },
    };
}

function ack(socket: FakeSocket, seq: number, key: string): void {
    socket.onmessage?.(new MessageEvent('message', { data: `["map_ack",[${seq},"${key}"],{}]` }));
}

function makeAppState(): AppState {
    return {
        activeToolId: 'brush',
        rectMode: 'light',
        ovalMode: 'circle',
        lineMode: 'double',
        gradientTarget: 'both',
        typeStyle: 'bold',
        selectedChar: '█',
        fgColor: [204, 204, 204],
        bgColor: [0, 0, 0],
        fontFamily: 'KreativeSquare',
        gradientStops: [[0, 0, 0], [255, 255, 255]],
        selectMode: 'rectangle',
        rotateMode: 'cw90',
        fillMode: 'gradient',
        lineDiagonal: true,
        eyedropperTarget: 'fg-bg',
    };
}

function makeSettings(appState: AppState): EditorSettings {
    return buildEditorSettings({
        appState,
        fgSlots: [[1, 2, 3], [4, 5, 6]],
        bgSlots: [[7, 8, 9]],
        customChars: ['█', '▓'],
        fontSize: 18,
        roomColor: [0, 204, 204],
        roomVisible: true,
    });
}

function sentArgs(socket: FakeSocket, index: number): [string, unknown[], object] {
    return JSON.parse(socket.sent[index]);
}

describe('buildEditorSettings', () => {
    it('snapshots every field from the live editor state', () => {
        const settings = makeSettings(makeAppState());
        expect(settings.fgColor).toEqual([204, 204, 204]);
        expect(settings.bgColor).toEqual([0, 0, 0]);
        expect(settings.fgSlots).toEqual([[1, 2, 3], [4, 5, 6]]);
        expect(settings.gradientStops).toEqual([[0, 0, 0], [255, 255, 255]]);
        expect(settings.customChars).toEqual(['█', '▓']);
        expect(settings.selectedChar).toBe('█');
        expect(settings.fontFamily).toBe('KreativeSquare');
        expect(settings.fontSize).toBe(18);
        expect(settings.tools.activeToolId).toBe('brush');
        expect(settings.tools.ovalMode).toBe('circle');
        expect(settings.tools.lineDiagonal).toBe(true);
        expect(settings.roomColor).toEqual([0, 204, 204]);
        expect(settings.roomVisible).toBe(true);
    });

    it('copies arrays so later live-state mutation cannot corrupt the snapshot', () => {
        const appState = makeAppState();
        const fgSlots: Color[] = [[1, 2, 3]];
        const settings = buildEditorSettings({
            appState,
            fgSlots,
            bgSlots: [],
            customChars: ['█'],
            fontSize: 18,
            roomColor: [0, 204, 204],
            roomVisible: true,
        });
        appState.fgColor = [9, 9, 9];
        appState.gradientStops.push([1, 1, 1]);
        fgSlots[0] = [9, 9, 9];
        fgSlots.push([9, 9, 9]);
        expect(settings.fgColor).toEqual([204, 204, 204]);
        expect(settings.gradientStops).toEqual([[0, 0, 0], [255, 255, 255]]);
        expect(settings.fgSlots).toEqual([[1, 2, 3]]);
    });
});

describe('MapEditSession settings save', () => {
    function makeSession() {
        const holder = makeSocketHolder();
        const canvas = new CanvasState(2, 1, false);
        const session = new MapEditSession(
            'K0',
            canvas,
            { originX: 0, originY: 0, roomCells: new Set(), rooms: [] },
            holder.createSocket
        );
        holder.socket.open();
        ack(holder.socket, 0, 'K1');
        return { holder, canvas, session };
    }

    it('sends settings as the fourth map_edit arg on explicit save', () => {
        const { holder, canvas, session } = makeSession();
        const settings = makeSettings(makeAppState());
        canvas.setCell(0, 0, { char: 'X', fg: [255, 0, 0], bg: [-1, -1, -1] });
        session.saveToServer(settings);

        const [, args] = sentArgs(holder.socket, 1);
        expect((args as unknown[]).length).toBe(4);
        expect((args as unknown[])[3]).toEqual(settings);
        session.dispose();
    });

    it('sends a settings-only save when the map is unchanged', () => {
        const { holder, session } = makeSession();
        const events: string[] = [];
        session.onEvent((e) => events.push(e.type));
        const settings = makeSettings(makeAppState());
        session.saveToServer(settings);

        const [cmd, args] = sentArgs(holder.socket, 1);
        expect(cmd).toBe('map_edit');
        expect((args as unknown[])[2]).toEqual([]);
        expect((args as unknown[])[3]).toEqual(settings);
        expect(events).not.toContain('error');

        ack(holder.socket, 1, 'K2');
        expect(events).toContain('saved');
        session.dispose();
    });

    it('still reports Nothing to save when neither map nor settings changed', () => {
        const { holder, session } = makeSession();
        const events: string[] = [];
        session.onEvent((e) => events.push(e.type === 'error' ? `error:${e.message}` : e.type));
        session.saveToServer();
        expect(events).toContain('error:Nothing to save.');
        expect(holder.socket.sent.length).toBe(1);
        session.dispose();
    });

    it('sends a 3-arg map_edit when saving without settings', () => {
        vi.useFakeTimers();
        const { holder, canvas, session } = makeSession();
        canvas.setCell(0, 0, { char: 'A', fg: [0, 0, 0], bg: [-1, -1, -1] });
        session.scheduleSync();
        vi.advanceTimersByTime(200);
        const [, args] = sentArgs(holder.socket, 1);
        expect((args as unknown[]).length).toBe(3);
        session.dispose();
        vi.useRealTimers();
    });
});
