import { MapRoom, MapExitEdit, formatExitCoord, parseExitCoord } from '../mapedit';
import { parseDescRuns, stripAnsi, wrapLegendSymbol, DEFAULT_FG, TRANSPARENT } from '../utils/ansiParser';
import { cssColor } from '../utils/colors';
import { Color } from '../types';
import { ColorPickerModal } from './ColorPickerModal';

export interface RoomSave {
    x: number;
    y: number;
    name: string;
    desc: string;
}

export interface ExitsChange {
    roomX: number;
    roomY: number;
    exits: MapExitEdit[];
}

export interface ExitDelete {
    roomX: number;
    roomY: number;
    index: number;
}

export interface ExitLinkPick {
    roomX: number;
    roomY: number;
    index: number;
}

export interface RoomEditorCallbacks {
    onExitsChange?: (change: ExitsChange) => void;
    onDeleteExit?: (del: ExitDelete) => void;
    onPickLink?: (pick: ExitLinkPick) => void;
}

/** Room-property panel for the map editor sidebar. Shows the room the
 * canvas selection points at (no room list: the host calls showRoom as
 * the selection changes). Name/description Save and every exit edit go
 * to the host callbacks for a server roundtrip.
 *
 * Name and Description work like a legend-editor row: the inputs hold
 * plain white text, the swatches own the field color (decoded from the
 * stored escapes on select), and Save wraps the whole field in 24-bit
 * control codes. */
export class RoomEditor {
    private container: HTMLElement;
    private rooms: MapRoom[] = [];
    private selectedKey: string | null = null;
    private notice: string = 'Select a single room to edit it.';
    private picking: boolean = false;
    private onSave: ((room: RoomSave) => void) | null;
    private callbacks: RoomEditorCallbacks;
    private openPicker: (initial: Color) => Promise<Color | null>;
    /** Swatch state per field (bg null = terminal default). Decoded
     * from the stored text on selection; picking never writes escapes
     * into the inputs, only Save wraps. */
    private nameFg: Color = [...DEFAULT_FG] as Color;
    private nameBg: Color = [...TRANSPARENT] as Color;
    private descFg: Color = [...DEFAULT_FG] as Color;
    private descBg: Color = [...TRANSPARENT] as Color;
    /** Room key the swatch state was decoded for: re-renders (exit
     * edits, status changes) must not wipe a picked-but-unsaved color. */
    private colorKey: string | null = null;

    constructor(
        containerId: string,
        onSave?: (room: RoomSave) => void,
        callbacks?: RoomEditorCallbacks,
        openPicker?: (initial: Color) => Promise<Color | null>,
    ) {
        const container = document.getElementById(containerId);
        if (!container) throw new Error(`Missing #${containerId}`);
        this.container = container;
        this.onSave = onSave ?? null;
        this.callbacks = callbacks ?? {};
        this.openPicker = openPicker ?? ((initial) => ColorPickerModal.getInstance().open(initial));
    }

    public setRooms(rooms: MapRoom[]): void {
        // Blank drafts (added via Add, never committed) are panel-local:
        // stash them per room so a host replace (e.g. after a commit)
        // keeps them as a suffix instead of wiping them.
        const drafts = new Map<string, MapExitEdit[]>();
        for (const r of this.rooms) {
            const blank = r.exits.filter((e) => e.name === '' || e.coord[0] === '');
            if (blank.length > 0) {
                drafts.set(RoomEditor.keyOf(r), blank.map((e) => ({
                    ...e,
                    aliases: [...e.aliases],
                    coord: [...e.coord] as [string, number, number, number],
                })));
            }
        }
        this.rooms = rooms.map((r) => ({
            x: r.x,
            y: r.y,
            name: r.name,
            desc: r.desc,
            exits: r.exits.map((e) => ({ ...e, aliases: [...e.aliases], coord: [...e.coord] as [string, number, number, number] })),
        }));
        for (const r of this.rooms) {
            const kept = drafts.get(RoomEditor.keyOf(r));
            if (kept) r.exits.push(...kept);
        }
        if (this.selectedKey !== null && !this.rooms.some((r) => RoomEditor.keyOf(r) === this.selectedKey)) {
            this.selectedKey = null;
        }
        this.render();
    }

    /** Show one room from the list (host-driven, follows canvas selection). */
    public selectRoom(x: number, y: number): void {
        const key = `${x},${y}`;
        if (!this.rooms.some((r) => RoomEditor.keyOf(r) === key)) return;
        this.selectedKey = key;
        this.render();
    }

    /** Show a notice instead of a room (no selection, or several rooms). */
    public showNotice(text: string = 'Select a single room to edit it.'): void {
        this.selectedKey = null;
        this.notice = text;
        this.render();
    }

    /** Re-render after the host mutated the room list in place (undo). */
    public refresh(): void {
        this.render();
    }

    /** Link-target picking banner (host sets while awaiting a canvas click). */
    public setPicking(active: boolean): void {
        this.picking = active;
        this.render();
    }

    public get selected(): MapRoom | null {
        return this.rooms.find((r) => RoomEditor.keyOf(r) === this.selectedKey) ?? null;
    }

    public get pickingActive(): boolean {
        return this.picking;
    }

    private static keyOf(room: MapRoom): string {
        return `${room.x},${room.y}`;
    }

    /** Last operation feedback; re-rendered every render so a panel
     * refresh (e.g. notice after delete) never swallows it. */
    private statusText = '';

    /** Save feedback shown under the Save button (_saved to server_, denial reason). */
    public setStatus(text: string): void {
        this.statusText = text;
        const status = this.container.querySelector('.room-editor-status');
        if (status) status.textContent = text;
    }

    private render(): void {
        this.container.innerHTML = '';
        if (this.picking) {
            const banner = document.createElement('div');
            banner.className = 'room-editor-picking';
            banner.textContent = 'Click a room for the exit to lead to… (Esc cancels)';
            this.container.appendChild(banner);
        }
        if (this.rooms.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'room-editor-empty';
            empty.textContent = 'No rooms — re-run mapedit in-game.';
            this.container.appendChild(empty);
            this.container.appendChild(this.renderStatus());
            return;
        }
        const room = this.selected;
        if (!room) {
            const empty = document.createElement('div');
            empty.className = 'room-editor-empty';
            empty.textContent = this.notice;
            this.container.appendChild(empty);
            this.container.appendChild(this.renderStatus());
            return;
        }

        // Decode the stored field colors into the swatches once per
        // selection, like the legend editor decodes a symbol's wrapping
        // escapes on open. Later re-renders keep a picked-but-unsaved
        // color instead of wiping it.
        const key = `${room.x},${room.y}`;
        if (this.colorKey !== key) {
            this.colorKey = key;
            const nameColors = RoomEditor.decodeFieldColors(room.name);
            this.nameFg = nameColors.fg;
            this.nameBg = nameColors.bg;
            const descColors = RoomEditor.decodeFieldColors(room.desc);
            this.descFg = descColors.fg;
            this.descBg = descColors.bg;
        }

        const nameLabel = document.createElement('label');
        nameLabel.className = 'room-editor-label';
        nameLabel.textContent = 'Name';
        nameLabel.appendChild(this.renderFieldTools('name'));
        this.container.appendChild(nameLabel);
        const nameInput = document.createElement('input');
        nameInput.type = 'text';
        nameInput.className = 'room-editor-name';
        nameInput.value = stripAnsi(room.name ?? '');
        nameInput.addEventListener('input', () => {
            // Pasted escapes never stick: the box holds plain text, the
            // swatches own the color. Only rewrite while escapes are
            // present so normal typing never jumps the cursor.
            const clean = stripAnsi(nameInput.value);
            if (clean !== nameInput.value) nameInput.value = clean;
            room.name = clean;
            this.updateFieldPreview('name');
        });
        this.container.appendChild(nameInput);
        const namePreview = document.createElement('div');
        namePreview.className = 'room-editor-name-preview';
        namePreview.title = 'How the name looks with its colors';
        this.container.appendChild(namePreview);

        const descLabel = document.createElement('label');
        descLabel.className = 'room-editor-label';
        descLabel.textContent = 'Description';
        descLabel.appendChild(this.renderFieldTools('desc'));
        this.container.appendChild(descLabel);
        const descInput = document.createElement('textarea');
        descInput.className = 'room-editor-desc';
        descInput.rows = 4;
        descInput.value = stripAnsi(room.desc ?? '');
        descInput.addEventListener('input', () => {
            const clean = stripAnsi(descInput.value);
            if (clean !== descInput.value) descInput.value = clean;
            room.desc = clean;
            this.updateFieldPreview('desc');
        });
        this.container.appendChild(descInput);
        const descPreview = document.createElement('div');
        descPreview.className = 'room-editor-desc-preview';
        descPreview.title = 'How the description looks with its colors';
        this.container.appendChild(descPreview);
        this.updateFieldPreview('name');
        this.updateFieldPreview('desc');

        const exitsTitle = document.createElement('div');
        exitsTitle.className = 'room-editor-label';
        exitsTitle.textContent = `Exits (${room.exits.length})`;
        this.container.appendChild(exitsTitle);
        const exitsList = document.createElement('div');
        exitsList.className = 'room-editor-exits';
        if (room.exits.length === 0) {
            const none = document.createElement('div');
            none.className = 'room-editor-no-exits';
            none.textContent = 'none';
            exitsList.appendChild(none);
        }
        room.exits.forEach((exit, index) => {
            exitsList.appendChild(this.renderExit(room, exit, index));
        });
        this.container.appendChild(exitsList);

        const addButton = document.createElement('button');
        addButton.type = 'button';
        addButton.className = 'room-editor-exit-add';
        addButton.textContent = 'Add exit';
        addButton.title = 'Add a blank exit (name it and set its target before it can save)';
        addButton.addEventListener('click', () => {
            room.exits.push({ name: '', aliases: [], coord: ['', 0, 0, 0] });
            this.render();
            this.setStatus('Blank exit added — name it and set its target.');
        });
        this.container.appendChild(addButton);

        const saveButton = document.createElement('button');
        saveButton.type = 'button';
        saveButton.className = 'room-editor-save';
        saveButton.textContent = 'Save';
        saveButton.addEventListener('click', () => {
            // Whole-field 24-bit wrap, like the legend editor wraps its
            // symbols on save. Default colors stay plain text; anything
            // decodable (256-color, basic SGR) was already normalized
            // into the swatch state on select.
            const name = wrapLegendSymbol(stripAnsi(nameInput.value), this.nameFg, this.nameBg);
            const desc = wrapLegendSymbol(stripAnsi(descInput.value), this.descFg, this.descBg);
            room.name = name;
            room.desc = desc;
            nameInput.value = stripAnsi(name);
            descInput.value = stripAnsi(desc);
            this.updateFieldPreview('name');
            this.updateFieldPreview('desc');
            this.setStatus('');
            this.onSave?.({ x: room.x, y: room.y, name, desc });
        });
        this.container.appendChild(saveButton);

        const status = this.renderStatus();
        this.container.appendChild(status);
    }

    private renderStatus(): HTMLElement {
        const status = document.createElement('div');
        status.className = 'room-editor-status';
        status.textContent = this.statusText;
        return status;
    }

    /** A stored field's swatch colors: its first colored run's fg/bg,
     * default when the field carries no color — the field-level twin of
     * the legend editor decoding a symbol's wrapping escapes. */
    private static decodeFieldColors(raw: string | null | undefined): { fg: Color; bg: Color } {
        const runs = parseDescRuns(raw ?? '');
        const fg = runs.find((r) => r.fg)?.fg;
        const bg = runs.find((r) => r.bg)?.bg;
        return {
            fg: fg ? ([...fg] as Color) : ([...DEFAULT_FG] as Color),
            bg: bg ? ([...bg] as Color) : ([...TRANSPARENT] as Color),
        };
    }

    private fieldColors(field: 'name' | 'desc'): { fg: Color; bg: Color } {
        return field === 'name'
            ? { fg: this.nameFg, bg: this.nameBg }
            : { fg: this.descFg, bg: this.descBg };
    }

    private setFieldColors(field: 'name' | 'desc', fg: Color, bg: Color): void {
        if (field === 'name') {
            this.nameFg = fg;
            this.nameBg = bg;
        } else {
            this.descFg = fg;
            this.descBg = bg;
        }
    }

    private renderFieldTools(field: 'name' | 'desc'): HTMLElement {
        const tools = document.createElement('span');
        tools.className = 'room-editor-field-tools';
        tools.append(this.renderSwatch(field, true), this.renderSwatch(field, false));
        return tools;
    }

    /** FG/BG swatch, mirroring the legend editor's color buttons:
     * click picks the field color, right-click resets it. Picking never
     * touches the input text — the preview shows the color and Save
     * wraps the whole field. */
    private renderSwatch(field: 'name' | 'desc', isFg: boolean): HTMLButtonElement {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = field === 'name'
            ? (isFg ? 'room-editor-name-fg' : 'room-editor-name-bg')
            : (isFg ? 'room-editor-desc-fg' : 'room-editor-desc-bg');
        const paint = (): void => {
            const color = isFg ? this.fieldColors(field).fg : this.fieldColors(field).bg;
            if (!isFg && color[0] === -1) {
                btn.style.background = 'repeating-conic-gradient(#666 0% 25%, #333 0% 50%) 50% / 8px 8px';
                btn.style.border = '1px dashed #888';
                btn.style.boxShadow = '';
            } else {
                btn.style.background = cssColor(color);
                btn.style.border = '1px solid #fff';
                btn.style.boxShadow = '0 0 0 1px #000 inset';
            }
        };
        paint();
        btn.title = isFg
            ? 'Text color for this field (right-click to reset)'
            : 'Background color for this field (right-click to clear)';
        btn.setAttribute('aria-label', isFg ? 'Field text color' : 'Field background color');
        btn.addEventListener('click', () => {
            void this.pickFieldColor(field, isFg, paint);
        });
        btn.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            const colors = this.fieldColors(field);
            if (isFg) this.setFieldColors(field, [...DEFAULT_FG] as Color, colors.bg);
            else this.setFieldColors(field, colors.fg, [...TRANSPARENT] as Color);
            paint();
            this.updateFieldPreview(field);
        });
        return btn;
    }

    private async pickFieldColor(field: 'name' | 'desc', isFg: boolean, repaint: () => void): Promise<void> {
        const colors = this.fieldColors(field);
        const seed = isFg ? colors.fg : (colors.bg[0] === -1 ? [0, 0, 0] as Color : colors.bg);
        const picked = await this.openPicker([...seed] as Color);
        if (!picked) return;
        if (isFg) this.setFieldColors(field, picked, colors.bg);
        else this.setFieldColors(field, colors.fg, picked);
        repaint();
        this.updateFieldPreview(field);
    }

    /** Re-render one field's preview: its plain text in the swatch colors. */
    private updateFieldPreview(field: 'name' | 'desc'): void {
        const preview = this.container.querySelector(
            field === 'name' ? '.room-editor-name-preview' : '.room-editor-desc-preview');
        if (!preview) return;
        const input = this.container.querySelector(
            field === 'name' ? '.room-editor-name' : '.room-editor-desc') as HTMLInputElement | HTMLTextAreaElement | null;
        preview.innerHTML = '';
        const text = stripAnsi(input?.value ?? '');
        if (!text) {
            const empty = document.createElement('span');
            empty.className = 'room-editor-preview-empty';
            empty.textContent = field === 'name' ? '(no name)' : '(no description)';
            preview.appendChild(empty);
            return;
        }
        const { fg, bg } = this.fieldColors(field);
        const span = document.createElement('span');
        span.textContent = text;
        if (!(fg[0] === DEFAULT_FG[0] && fg[1] === DEFAULT_FG[1] && fg[2] === DEFAULT_FG[2])) {
            span.style.color = cssColor(fg);
        }
        if (!(bg[0] === -1 && bg[1] === -1 && bg[2] === -1)) {
            span.style.backgroundColor = cssColor(bg);
        }
        preview.appendChild(span);
    }

    /** Full validation of one exit row against the server's caps. */
    private static exitError(exits: MapExitEdit[], index: number): string | null {
        const exit = exits[index];
        if (exit.name.length === 0 || exit.name.length > 64) return 'Exit names need 1–64 characters.';
        if (exits.some((e, i) => i !== index && e.name.toLowerCase() === exit.name.toLowerCase())) {
            return `An exit named '${exit.name}' already exists here.`;
        }
        if (exit.aliases.length > 16) return 'Too many aliases (max 16).';
        if (exit.aliases.some((a) => a.length === 0 || a.length > 32)) return 'Aliases need 1–32 characters.';
        if (exit.coord[0].length === 0 || exit.coord[0].length > 64) return 'Exit area needs 1–64 characters.';
        return null;
    }

    /** Whole-list commit gate: the edited row must pass, and no other
     * blank draft may linger (committing it would send a blank the
     * server rejects by killing the session). */
    private static commitBlocker(exits: MapExitEdit[], index: number): { message: string; mine: boolean } | null {
        const mine = RoomEditor.exitError(exits, index);
        if (mine) return { message: mine, mine: true };
        const otherBlank = exits.some((e, i) => i !== index && (e.name === '' || e.coord[0] === ''));
        if (otherBlank) return { message: 'Finish or delete the blank exit first.', mine: false };
        return null;
    }

    private renderExit(room: MapRoom, exit: MapExitEdit, index: number): HTMLElement {
        const row = document.createElement('div');
        row.className = 'room-editor-exit';

        const nameInput = document.createElement('input');
        nameInput.type = 'text';
        nameInput.className = 'room-editor-exit-name';
        nameInput.title = 'Exit name';
        nameInput.value = exit.name;
        nameInput.addEventListener('change', () => {
            const old = exit.name;
            const wasDraft = exit.name === '' || exit.coord[0] === '';
            exit.name = nameInput.value;
            const blocked = RoomEditor.commitBlocker(room.exits, index);
            if (blocked) {
                // A blank draft keeps what was typed so it can be finished
                // field by field; anything else restores — a lingering blank
                // elsewhere also restores, so the panel never shows edits
                // the server never received.
                if (!(wasDraft && blocked.mine)) exit.name = old;
                this.render();
                this.setStatus(blocked.message);
                return;
            }
            this.commitExits(room, room.exits);
        });
        row.appendChild(nameInput);

        const coordInput = document.createElement('input');
        coordInput.type = 'text';
        coordInput.className = 'room-editor-exit-coord';
        coordInput.title = 'Target room (Area,x,y,z)';
        coordInput.value = formatExitCoord(exit.coord[0], exit.coord[1], exit.coord[2], exit.coord[3]);
        coordInput.addEventListener('change', () => {
            const parsed = parseExitCoord(coordInput.value);
            if (!parsed) {
                this.render();
                this.setStatus('Invalid target — use (Area,x,y,z).');
                return;
            }
            const old: [string, number, number, number] = [...exit.coord];
            const wasDraft = exit.name === '' || exit.coord[0] === '';
            exit.coord = [parsed.area, parsed.x, parsed.y, parsed.z];
            const blocked = RoomEditor.commitBlocker(room.exits, index);
            if (blocked) {
                if (!(wasDraft && blocked.mine)) exit.coord = old;
                this.render();
                this.setStatus(blocked.message);
                return;
            }
            this.commitExits(room, room.exits);
        });
        row.appendChild(coordInput);

        const linkButton = document.createElement('button');
        linkButton.type = 'button';
        linkButton.className = 'room-editor-exit-link';
        linkButton.title = 'Point this exit at another room (click it on the map)';
        linkButton.textContent = 'Link';
        linkButton.addEventListener('click', () => {
            if (exit.name === '' || exit.coord[0] === '') {
                this.setStatus('Give the exit a name and target before linking.');
                return;
            }
            this.callbacks.onPickLink?.({ roomX: room.x, roomY: room.y, index });
        });
        row.appendChild(linkButton);

        const deleteButton = document.createElement('button');
        deleteButton.type = 'button';
        deleteButton.className = 'room-editor-exit-delete';
        deleteButton.title = `Delete exit '${exit.name}'`;
        deleteButton.textContent = 'X';
        deleteButton.addEventListener('click', () => {
            // A blank draft was never committed: drop it locally so the
            // host indexes stay aligned with the panel rows.
            if (exit.name === '' || exit.coord[0] === '') {
                room.exits.splice(index, 1);
                this.setStatus('');
                this.render();
                return;
            }
            this.callbacks.onDeleteExit?.({ roomX: room.x, roomY: room.y, index });
        });
        row.appendChild(deleteButton);

        const aliasesLabel = document.createElement('label');
        aliasesLabel.className = 'room-editor-exit-aliases';
        aliasesLabel.textContent = 'aliases: ';
        const aliasesInput = document.createElement('input');
        aliasesInput.type = 'text';
        aliasesInput.className = 'room-editor-exit-aliases-input';
        aliasesInput.title = 'Comma-separated aliases';
        aliasesInput.value = exit.aliases.join(', ');
        aliasesInput.addEventListener('change', () => {
            const old = exit.aliases;
            const wasDraft = exit.name === '' || exit.coord[0] === '';
            exit.aliases = aliasesInput.value.split(',').map((a) => a.trim()).filter((a) => a.length > 0);
            const blocked = RoomEditor.commitBlocker(room.exits, index);
            if (blocked) {
                if (!(wasDraft && blocked.mine)) exit.aliases = old;
                this.render();
                this.setStatus(blocked.message);
                return;
            }
            this.commitExits(room, room.exits);
        });
        aliasesLabel.appendChild(aliasesInput);
        row.appendChild(aliasesLabel);
        return row;
    }

    private commitExits(room: MapRoom, exits: MapExitEdit[]): void {
        room.exits = exits;
        this.setStatus('');
        this.callbacks.onExitsChange?.({ roomX: room.x, roomY: room.y, exits: exits.map((e) => ({ ...e })) });
        this.render();
    }
}
