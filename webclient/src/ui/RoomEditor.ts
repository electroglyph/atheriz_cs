import { MapRoom, MapExitEdit, formatExitCoord, parseExitCoord } from '../mapedit';

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
 * to the host callbacks for a server roundtrip. */
export class RoomEditor {
    private container: HTMLElement;
    private rooms: MapRoom[] = [];
    private selectedKey: string | null = null;
    private notice: string = 'Select a single room to edit it.';
    private picking: boolean = false;
    private onSave: ((room: RoomSave) => void) | null;
    private callbacks: RoomEditorCallbacks;

    constructor(containerId: string, onSave?: (room: RoomSave) => void, callbacks?: RoomEditorCallbacks) {
        const container = document.getElementById(containerId);
        if (!container) throw new Error(`Missing #${containerId}`);
        this.container = container;
        this.onSave = onSave ?? null;
        this.callbacks = callbacks ?? {};
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

        const nameLabel = document.createElement('label');
        nameLabel.className = 'room-editor-label';
        nameLabel.textContent = 'Name';
        const nameInput = document.createElement('input');
        nameInput.type = 'text';
        nameInput.className = 'room-editor-name';
        nameInput.value = room.name ?? '';
        nameInput.addEventListener('input', () => {
            room.name = nameInput.value;
        });
        nameLabel.appendChild(nameInput);
        this.container.appendChild(nameLabel);

        const descLabel = document.createElement('label');
        descLabel.className = 'room-editor-label';
        descLabel.textContent = 'Description';
        const descInput = document.createElement('textarea');
        descInput.className = 'room-editor-desc';
        descInput.rows = 4;
        descInput.value = room.desc ?? '';
        descInput.addEventListener('input', () => {
            room.desc = descInput.value;
        });
        descLabel.appendChild(descInput);
        this.container.appendChild(descLabel);

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
            room.name = nameInput.value;
            room.desc = descInput.value;
            this.setStatus('');
            this.onSave?.({ x: room.x, y: room.y, name: nameInput.value, desc: descInput.value });
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
