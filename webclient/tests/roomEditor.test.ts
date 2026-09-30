// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { RoomEditor, ExitsChange, ExitDelete, ExitLinkPick } from '../src/ui/RoomEditor';
import { MapRoom } from '../src/mapedit';

function mountContainer(): void {
    document.body.innerHTML = '<div id="room-editor-container"></div>';
}

function sampleRooms(): MapRoom[] {
    return [
        { x: 0, y: 0, name: 'Hall', desc: 'A dusty hall.', exits: [{ name: 'North', aliases: ['n'], coord: ['TestArea', 0, 1, 0] }] },
        { x: 1, y: 0, desc: 'Kitchen', exits: [] },
    ];
}

function selectFirst(editor: RoomEditor): void {
    editor.setRooms(sampleRooms());
    editor.selectRoom(0, 0);
}

describe('RoomEditor', () => {
    it('throws when the container is missing', () => {
        document.body.innerHTML = '';
        expect(() => new RoomEditor('room-editor-container')).toThrow();
    });

    it('shows an empty notice when there are no rooms', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        editor.setRooms([]);
        expect(document.querySelector('.room-editor-empty')!.textContent).toBe(
            'No rooms — re-run mapedit in-game.'
        );
        expect(editor.selected).toBeNull();
    });

    it('asks for a single-room selection when nothing is selected', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        editor.setRooms(sampleRooms());
        expect(document.querySelector('.room-editor-empty')!.textContent).toBe(
            'Select a single room to edit it.'
        );
        expect(editor.selected).toBeNull();
        expect(document.querySelector('.room-editor-name')).toBeNull();
    });

    it('selectRoom shows the detail fields and editable exit rows', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        selectFirst(editor);
        expect((document.querySelector('.room-editor-name') as HTMLInputElement).value).toBe('Hall');
        expect((document.querySelector('.room-editor-desc') as HTMLTextAreaElement).value).toBe('A dusty hall.');
        const rows = document.querySelectorAll('.room-editor-exit');
        expect(rows).toHaveLength(1);
        expect((document.querySelector('.room-editor-exit-name') as HTMLInputElement).value).toBe('North');
        expect((document.querySelector('.room-editor-exit-coord') as HTMLInputElement).value).toBe('(TestArea,0,1,0)');
        expect((document.querySelector('.room-editor-exit-aliases-input') as HTMLInputElement).value).toBe('n');
        expect((document.querySelector('.room-editor-exit-link') as HTMLButtonElement).textContent).toBe('Link');
        expect((document.querySelector('.room-editor-exit-delete') as HTMLButtonElement).textContent).toBe('X');
        expect(editor.selected?.x).toBe(0);
    });

    it('selectRoom shows none for rooms without exits', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        editor.setRooms(sampleRooms());
        editor.selectRoom(1, 0);
        expect((document.querySelector('.room-editor-name') as HTMLInputElement).value).toBe('');
        expect(document.querySelector('.room-editor-no-exits')!.textContent).toBe('none');
        expect(editor.selected?.x).toBe(1);
    });

    it('ignores selectRoom for unknown coords', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        selectFirst(editor);
        editor.selectRoom(9, 9);
        expect(editor.selected?.x).toBe(0);
    });

    it('keeps edits local on the selected room', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        const rooms = sampleRooms();
        editor.setRooms(rooms);
        editor.selectRoom(0, 0);
        const nameInput = document.querySelector('.room-editor-name') as HTMLInputElement;
        nameInput.value = 'Grand Hall';
        nameInput.dispatchEvent(new Event('input', { bubbles: true }));
        const descInput = document.querySelector('.room-editor-desc') as HTMLTextAreaElement;
        descInput.value = 'Renovated.';
        descInput.dispatchEvent(new Event('input', { bubbles: true }));
        expect(editor.selected?.name).toBe('Grand Hall');
        expect(editor.selected?.desc).toBe('Renovated.');
        // the caller's array is untouched (the panel holds its own copy)
        expect(rooms[0].name).toBe('Hall');
    });

    it('sends the edited name and description through Save', () => {
        mountContainer();
        const saved: { x: number; y: number; name: string; desc: string }[] = [];
        const editor = new RoomEditor('room-editor-container', (room) => saved.push(room));
        selectFirst(editor);
        const nameInput = document.querySelector('.room-editor-name') as HTMLInputElement;
        nameInput.value = 'Grand Hall';
        const descInput = document.querySelector('.room-editor-desc') as HTMLTextAreaElement;
        descInput.value = 'Renovated.';
        (document.querySelector('.room-editor-save') as HTMLButtonElement).click();
        expect(saved).toEqual([{ x: 0, y: 0, name: 'Grand Hall', desc: 'Renovated.' }]);
        expect(editor.selected?.name).toBe('Grand Hall');
        expect(editor.selected?.desc).toBe('Renovated.');
    });

    it('does nothing on Save without a callback', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        selectFirst(editor);
        expect(() => (document.querySelector('.room-editor-save') as HTMLButtonElement).click()).not.toThrow();
    });

    it('shows server feedback in the status line', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        selectFirst(editor);
        editor.setStatus('Saved to server.');
        expect(document.querySelector('.room-editor-status')!.textContent).toBe('Saved to server.');
    });

    it('keeps operation feedback visible when the panel falls back to a notice', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        selectFirst(editor);
        editor.setStatus('Deleted 1 room.');
        editor.showNotice();
        expect(document.querySelector('.room-editor-empty')!.textContent).toBe(
            'Select a single room to edit it.'
        );
        expect(document.querySelector('.room-editor-status')!.textContent).toBe('Deleted 1 room.');
    });

    it('commits an exit rename through onExitsChange', () => {
        mountContainer();
        const changes: ExitsChange[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: (c) => changes.push(c) });
        selectFirst(editor);
        const nameInput = document.querySelector('.room-editor-exit-name') as HTMLInputElement;
        nameInput.value = 'Up';
        nameInput.dispatchEvent(new Event('change', { bubbles: true }));
        expect(changes).toHaveLength(1);
        expect(changes[0]).toEqual({
            roomX: 0,
            roomY: 0,
            exits: [{ name: 'Up', aliases: ['n'], coord: ['TestArea', 0, 1, 0] }],
        });
        expect(editor.selected?.exits[0].name).toBe('Up');
    });

    it('rejects an empty exit rename without calling back', () => {
        mountContainer();
        let calls = 0;
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: () => calls++ });
        selectFirst(editor);
        const nameInput = document.querySelector('.room-editor-exit-name') as HTMLInputElement;
        nameInput.value = '';
        nameInput.dispatchEvent(new Event('change', { bubbles: true }));
        expect(calls).toBe(0);
        expect(document.querySelector('.room-editor-status')!.textContent).toContain('1–64 characters');
        expect((document.querySelector('.room-editor-exit-name') as HTMLInputElement).value).toBe('North');
    });

    it('commits an exit coord rewrite through onExitsChange', () => {
        mountContainer();
        const changes: ExitsChange[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: (c) => changes.push(c) });
        selectFirst(editor);
        const coordInput = document.querySelector('.room-editor-exit-coord') as HTMLInputElement;
        coordInput.value = '(TestArea,1,0,0)';
        coordInput.dispatchEvent(new Event('change', { bubbles: true }));
        expect(changes).toHaveLength(1);
        expect(changes[0].exits[0].coord).toEqual(['TestArea', 1, 0, 0]);
    });

    it('rejects a malformed exit coord without calling back', () => {
        mountContainer();
        let calls = 0;
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: () => calls++ });
        selectFirst(editor);
        const coordInput = document.querySelector('.room-editor-exit-coord') as HTMLInputElement;
        coordInput.value = 'TestArea 1,0';
        coordInput.dispatchEvent(new Event('change', { bubbles: true }));
        expect(calls).toBe(0);
        expect(document.querySelector('.room-editor-status')!.textContent).toContain('(Area,x,y,z)');
        expect((document.querySelector('.room-editor-exit-coord') as HTMLInputElement).value).toBe('(TestArea,0,1,0)');
    });

    it('routes delete clicks through onDeleteExit', () => {
        mountContainer();
        const deletes: ExitDelete[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onDeleteExit: (d) => deletes.push(d) });
        selectFirst(editor);
        (document.querySelector('.room-editor-exit-delete') as HTMLButtonElement).click();
        expect(deletes).toEqual([{ roomX: 0, roomY: 0, index: 0 }]);
    });

    it('routes Link clicks through onPickLink', () => {
        mountContainer();
        const picks: ExitLinkPick[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onPickLink: (p) => picks.push(p) });
        selectFirst(editor);
        (document.querySelector('.room-editor-exit-link') as HTMLButtonElement).click();
        expect(picks).toEqual([{ roomX: 0, roomY: 0, index: 0 }]);
    });

    it('shows and hides the picking banner', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        selectFirst(editor);
        expect(document.querySelector('.room-editor-picking')).toBeNull();
        editor.setPicking(true);
        expect(document.querySelector('.room-editor-picking')!.textContent).toContain('Click a room');
        editor.setPicking(false);
        expect(document.querySelector('.room-editor-picking')).toBeNull();
    });
});

describe('RoomEditor exit drafts and aliases', () => {
    function rows(): NodeListOf<Element> {
        return document.querySelectorAll('.room-editor-exit');
    }

    function rowInputs(row: Element): { name: HTMLInputElement; coord: HTMLInputElement; aliases: HTMLInputElement } {
        return {
            name: row.querySelector('.room-editor-exit-name') as HTMLInputElement,
            coord: row.querySelector('.room-editor-exit-coord') as HTMLInputElement,
            aliases: row.querySelector('.room-editor-exit-aliases-input') as HTMLInputElement,
        };
    }

    function change(el: HTMLInputElement, value: string): void {
        el.value = value;
        el.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function twoExitRooms(): MapRoom[] {
        return [{
            x: 0, y: 0, name: 'Hall', desc: 'A dusty hall.', exits: [
                { name: 'North', aliases: ['n'], coord: ['TestArea', 0, 1, 0] },
                { name: 'South', aliases: ['s'], coord: ['TestArea', 0, -1, 0] },
            ],
        }];
    }

    it('Add appends a blank exit row without committing', () => {
        mountContainer();
        const changes: ExitsChange[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: (c) => changes.push(c) });
        selectFirst(editor);
        expect(rows()).toHaveLength(1);
        (document.querySelector('.room-editor-exit-add') as HTMLButtonElement).click();
        expect(rows()).toHaveLength(2);
        const blank = rowInputs(rows()[1]);
        expect(blank.name.value).toBe('');
        expect(blank.coord.value).toBe('(,0,0,0)');
        expect(changes).toHaveLength(0);
        expect(document.querySelector('.room-editor-status')!.textContent).toContain('Blank exit added');
        expect(editor.selected?.exits).toHaveLength(2);
    });

    it('a blank draft keeps a typed name so it can be finished field by field', () => {
        mountContainer();
        const changes: ExitsChange[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: (c) => changes.push(c) });
        selectFirst(editor);
        (document.querySelector('.room-editor-exit-add') as HTMLButtonElement).click();
        change(rowInputs(rows()[1]).name, 'Up');
        // name kept locally, area still blank so nothing commits.
        expect(rowInputs(rows()[1]).name.value).toBe('Up');
        expect(changes).toHaveLength(0);
        expect(document.querySelector('.room-editor-status')!.textContent).toBe('Exit area needs 1–64 characters.');
        change(rowInputs(rows()[1]).coord, '(TestArea,9,9,0)');
        expect(changes).toHaveLength(1);
        expect(changes[0].exits).toHaveLength(2);
        expect(changes[0].exits[1]).toEqual({ name: 'Up', aliases: [], coord: ['TestArea', 9, 9, 0] });
    });

    it('commits edited aliases, trimming and dropping empties', () => {
        mountContainer();
        const changes: ExitsChange[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: (c) => changes.push(c) });
        selectFirst(editor);
        change(rowInputs(rows()[0]).aliases, ' n ,, s ');
        expect(changes).toHaveLength(1);
        expect(changes[0].exits[0].aliases).toEqual(['n', 's']);
    });

    it('rejects more than 16 aliases without calling back', () => {
        mountContainer();
        const changes: ExitsChange[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: (c) => changes.push(c) });
        selectFirst(editor);
        const many = Array.from({ length: 17 }, (_, i) => `a${i}`).join(',');
        change(rowInputs(rows()[0]).aliases, many);
        expect(changes).toHaveLength(0);
        expect(document.querySelector('.room-editor-status')!.textContent).toBe('Too many aliases (max 16).');
        expect(rowInputs(rows()[0]).aliases.value).toBe('n');
    });

    it('a lingering blank restores the edited row and blocks the commit', () => {
        mountContainer();
        const changes: ExitsChange[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: (c) => changes.push(c) });
        selectFirst(editor);
        (document.querySelector('.room-editor-exit-add') as HTMLButtonElement).click();
        change(rowInputs(rows()[0]).name, 'Up');
        expect(changes).toHaveLength(0);
        expect(document.querySelector('.room-editor-status')!.textContent).toBe('Finish or delete the blank exit first.');
        // committed row restored: the panel never shows edits the server never got.
        expect(rowInputs(rows()[0]).name.value).toBe('North');
    });

    it('deleting a blank row stays local and never commits', () => {
        mountContainer();
        const changes: ExitsChange[] = [];
        const deletes: ExitDelete[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, {
            onExitsChange: (c) => changes.push(c),
            onDeleteExit: (d) => deletes.push(d),
        });
        selectFirst(editor);
        (document.querySelector('.room-editor-exit-add') as HTMLButtonElement).click();
        expect(rows()).toHaveLength(2);
        (rows()[1].querySelector('.room-editor-exit-delete') as HTMLButtonElement).click();
        expect(rows()).toHaveLength(1);
        expect(changes).toHaveLength(0);
        expect(deletes).toHaveLength(0);
    });

    it('Link on a blank row hints instead of picking', () => {
        mountContainer();
        const picks: ExitLinkPick[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onPickLink: (p) => picks.push(p) });
        selectFirst(editor);
        (document.querySelector('.room-editor-exit-add') as HTMLButtonElement).click();
        (rows()[1].querySelector('.room-editor-exit-link') as HTMLButtonElement).click();
        expect(picks).toHaveLength(0);
        expect(document.querySelector('.room-editor-status')!.textContent).toBe('Give the exit a name and target before linking.');
    });

    it('drafts survive a host setRooms refresh as a suffix', () => {
        mountContainer();
        const editor = new RoomEditor('room-editor-container');
        selectFirst(editor);
        (document.querySelector('.room-editor-exit-add') as HTMLButtonElement).click();
        change(rowInputs(rows()[1]).name, 'Up');
        editor.setRooms(sampleRooms());
        editor.selectRoom(0, 0);
        expect(rows()).toHaveLength(2);
        expect(rowInputs(rows()[0]).name.value).toBe('North');
        expect(rowInputs(rows()[1]).name.value).toBe('Up');
    });

    it('rejects a duplicate rename without calling back', () => {
        mountContainer();
        const changes: ExitsChange[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: (c) => changes.push(c) });
        editor.setRooms(twoExitRooms());
        editor.selectRoom(0, 0);
        change(rowInputs(rows()[0]).name, 'south');
        expect(changes).toHaveLength(0);
        expect(document.querySelector('.room-editor-status')!.textContent).toBe(`An exit named 'south' already exists here.`);
        expect(rowInputs(rows()[0]).name.value).toBe('North');
    });

    it('rejects a 65-char rename without calling back', () => {
        mountContainer();
        const changes: ExitsChange[] = [];
        const editor = new RoomEditor('room-editor-container', undefined, { onExitsChange: (c) => changes.push(c) });
        selectFirst(editor);
        change(rowInputs(rows()[0]).name, 'x'.repeat(65));
        expect(changes).toHaveLength(0);
        expect(document.querySelector('.room-editor-status')!.textContent).toBe('Exit names need 1–64 characters.');
        expect(rowInputs(rows()[0]).name.value).toBe('North');
    });
});
