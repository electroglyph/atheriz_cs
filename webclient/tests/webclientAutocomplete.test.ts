// @vitest-environment jsdom
import { describe, expect, it, beforeEach, afterEach } from 'vitest';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { CommandHistory } from '../src/webclient/history';
import { isCaretAtEnd } from '../src/webclient/input';

describe('autocomplete caret gating', () => {
    it('accepts only a collapsed caret at the very end', () => {
        expect(isCaretAtEnd('look', 4, 4)).toBe(true);
        expect(isCaretAtEnd('', 0, 0)).toBe(true);
    });

    it('rejects a mid-line caret even when text matches', () => {
        expect(isCaretAtEnd('look', 2, 2)).toBe(false);
        expect(isCaretAtEnd('look', 0, 0)).toBe(false);
    });

    it('rejects any selection, including one ending at the end', () => {
        expect(isCaretAtEnd('look', 0, 4)).toBe(false);
        expect(isCaretAtEnd('look', 1, 4)).toBe(false);
        expect(isCaretAtEnd('look', 4, 1)).toBe(false);
    });

    it('rejects null caret positions', () => {
        expect(isCaretAtEnd('look', null, 4)).toBe(false);
        expect(isCaretAtEnd('look', 4, null)).toBe(false);
        expect(isCaretAtEnd('look', null, null)).toBe(false);
    });
});

describe('autocomplete ghost stays completable', () => {
    beforeEach(() => localStorage.clear());
    afterEach(() => localStorage.clear());

    it('hides the ghost hint when the caret leaves the end', () => {
        const hist = new CommandHistory('test-autocomplete-ghost', 10);
        hist.add('look north');
        hist.findCompletions('look');
        const suggestion = hist.getSuggestion();
        expect(suggestion).toBe('look north');
        // Typing leaves the caret at the end: hint visible.
        expect(isCaretAtEnd('look', 4, 4)).toBe(true);
        // ArrowLeft / mouse moves the caret mid-line without firing `input`:
        // the hint must hide there, otherwise Right would visibly do nothing.
        const shownMidLine = isCaretAtEnd('look', 2, 2) ? suggestion : '';
        expect(shownMidLine).toBe('');
    });
});

describe('autocomplete wiring in main.ts', () => {
    const mainPath = path.resolve(import.meta.dirname, '../src/webclient/main.ts');
    const main = fs.readFileSync(mainPath, 'utf-8');

    it('gates ghost display on the caret at the end', () => {
        expect(main).toContain('isCaretAtEnd(elements.input.value');
        const updateIdx = main.indexOf('const updateHint');
        expect(updateIdx).toBeGreaterThan(-1);
        const hintLine = main.indexOf('hint.value = atEnd ? history.getSuggestion()', updateIdx);
        expect(hintLine).toBeGreaterThan(updateIdx);
    });

    it('requires the caret at the end for both Tab and ArrowRight', () => {
        expect(main).toContain('caretAtEnd');
        expect(main).toContain("(event.key === 'Tab' || event.key === 'ArrowRight')");
        expect(main).not.toContain('event.key === \'Tab\' || (event.key === \'ArrowRight\'');
    });

    it('refreshes the ghost when the caret moves without input', () => {
        expect(main).toContain("addEventListener('keyup', updateHint)");
        expect(main).toContain("addEventListener('click', updateHint)");
        expect(main).toContain("addEventListener('select', updateHint)");
    });

    it('parks the caret at the end after accepting or navigating', () => {
        expect(main).toContain('elements.input.setSelectionRange(suggestion.length, suggestion.length)');
        expect(main).toContain('elements.input.setSelectionRange(elements.input.value.length, elements.input.value.length)');
    });
});
