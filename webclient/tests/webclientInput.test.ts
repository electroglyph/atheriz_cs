// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { inputHeight, shouldClearSubmittedInput, shouldFocusInputOnKeydown, shouldNavigateHistory, submissionFeedback } from '../src/webclient/input';

describe('webclient input behavior', () => {
    it('keeps arrow navigation inside multiline input', () => {
        expect(shouldNavigateHistory('ArrowDown', 'line one\nline two', 5, 5, false)).toBe(false);
    });

    it('allows history navigation from an empty input or at its start', () => {
        expect(shouldNavigateHistory('ArrowUp', '', 0, 0, false)).toBe(true);
        expect(shouldNavigateHistory('ArrowUp', 'command', 0, 0, false)).toBe(true);
        expect(shouldNavigateHistory('ArrowDown', 'command', 0, 0, false)).toBe(true);
        expect(shouldNavigateHistory('ArrowDown', '', 0, 0, false)).toBe(true);
    });

    it('keeps the input large enough for multiline content', () => {
        expect(inputHeight(80)).toBe(80);
        expect(inputHeight(10)).toBe(30);
    });

    it('clears a submitted command only after ordinary next-key input', () => {
        expect(shouldClearSubmittedInput('a', true, false, false, false)).toBe(true);
        expect(shouldClearSubmittedInput('Enter', true, false, false, false)).toBe(false);
        expect(shouldClearSubmittedInput('a', true, true, false, false)).toBe(false);
        expect(shouldClearSubmittedInput('a', false, false, false, false)).toBe(false);
    });

    it('reports when a command cannot be sent', () => {
        expect(submissionFeedback(false)).toBe('\r\nNot connected to server.\r\n');
        expect(submissionFeedback(true)).toBeNull();
    });

    it('focuses the input on typing outside editables, including terminals', () => {
        const input = document.createElement('textarea');
        document.body.append(input);
        const term = document.createElement('div');
        term.className = 'xterm';
        const helper = document.createElement('textarea');
        helper.className = 'xterm-helper-textarea';
        term.append(helper);
        document.body.append(term);
        const other = document.createElement('input');
        document.body.append(other);
        try {
            expect(shouldFocusInputOnKeydown('a', document.body, input, false, false, false)).toBe(true);
            expect(shouldFocusInputOnKeydown('a', null, input, false, false, false)).toBe(true);
            expect(shouldFocusInputOnKeydown('a', helper, input, false, false, false)).toBe(true);
            expect(shouldFocusInputOnKeydown('a', input, input, false, false, false)).toBe(false);
            expect(shouldFocusInputOnKeydown('a', other, input, false, false, false)).toBe(false);
            expect(shouldFocusInputOnKeydown('Enter', document.body, input, false, false, false)).toBe(false);
            expect(shouldFocusInputOnKeydown('a', document.body, input, true, false, false)).toBe(false);
            expect(shouldFocusInputOnKeydown('a', helper, input, true, false, false)).toBe(false);
        } finally {
            input.remove();
            term.remove();
            other.remove();
        }
    });
});
