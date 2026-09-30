import { describe, expect, it } from 'vitest';
import { parseDescRuns, encodeDescRuns } from '../src/utils/ansiParser';

describe('parseDescRuns', () => {
    it('returns one default run for plain text', () => {
        expect(parseDescRuns('A dusty hall.')).toEqual([
            { text: 'A dusty hall.', fg: null, bg: null, bold: undefined, italic: undefined, underline: undefined },
        ]);
    });

    it('returns no runs for empty text', () => {
        expect(parseDescRuns('')).toEqual([]);
    });

    it('decodes a 24-bit foreground run', () => {
        const runs = parseDescRuns('A \x1b[38;2;255;0;0mred\x1b[0m hall.');
        expect(runs).toHaveLength(3);
        expect(runs[0]).toMatchObject({ text: 'A ', fg: null });
        expect(runs[1]).toMatchObject({ text: 'red', fg: [255, 0, 0] });
        expect(runs[2]).toMatchObject({ text: ' hall.', fg: null });
    });

    it('decodes combined background and foreground codes', () => {
        const runs = parseDescRuns('\x1b[48;2;10;20;30m\x1b[38;2;255;255;255m#\x1b[0m');
        expect(runs).toHaveLength(1);
        expect(runs[0].fg).toEqual([255, 255, 255]);
        expect(runs[0].bg).toEqual([10, 20, 30]);
    });

    it('keeps an explicit black background as a real black', () => {
        const runs = parseDescRuns('\x1b[48;2;0;0;0mDark\x1b[0m');
        expect(runs[0].bg).toEqual([0, 0, 0]);
    });

    it('maps 256-color codes to RGB', () => {
        const runs = parseDescRuns('\x1b[38;5;9mQ\x1b[0m');
        expect(runs[0].fg).toEqual([255, 0, 0]);
    });

    it('maps basic SGR colors to RGB', () => {
        expect(parseDescRuns('\x1b[31mR\x1b[0m')[0].fg).toEqual([128, 0, 0]);
        expect(parseDescRuns('\x1b[94mB\x1b[0m')[0].fg).toEqual([0, 0, 255]);
        expect(parseDescRuns('\x1b[42mG\x1b[0m')[0].bg).toEqual([0, 128, 0]);
    });

    it('tracks bold/italic/underline and 39/49 resets', () => {
        const runs = parseDescRuns('\x1b[1mBold\x1b[22m plain \x1b[38;2;1;2;3mC\x1b[39m.');
        expect(runs[0]).toMatchObject({ text: 'Bold', bold: true });
        expect(runs[1]).toMatchObject({ text: ' plain ', bold: false, fg: null });
        expect(runs[2]).toMatchObject({ text: 'C', fg: [1, 2, 3] });
        expect(runs[3]).toMatchObject({ text: '.', fg: null });
    });

    it('collapses a trailing reset after plain text', () => {
        expect(parseDescRuns('Hall.\x1b[0m')).toHaveLength(1);
    });

    it('keeps newlines inside runs', () => {
        const runs = parseDescRuns('Line one\n\x1b[38;2;0;255;0mLine two\x1b[0m');
        expect(runs).toHaveLength(2);
        expect(runs[0].text).toBe('Line one\n');
    });

    it('drops a lone ESC instead of leaking it into text', () => {
        expect(parseDescRuns('a\x1bb').map((r) => r.text).join('')).toBe('ab');
    });
});

describe('encodeDescRuns', () => {
    it('leaves plain text untouched', () => {
        expect(encodeDescRuns(parseDescRuns('A dusty hall.'))).toBe('A dusty hall.');
    });

    it('emits 24-bit codes for colors', () => {
        const out = encodeDescRuns([{ text: 'red', fg: [255, 0, 0], bg: null }]);
        expect(out).toBe('\x1b[38;2;255;0;0mred\x1b[0m');
    });

    it('normalizes 256-color and basic codes to 24-bit', () => {
        expect(encodeDescRuns(parseDescRuns('\x1b[38;5;9mQ\x1b[0m')))
            .toBe('\x1b[38;2;255;0;0mQ\x1b[0m');
        expect(encodeDescRuns(parseDescRuns('\x1b[31mR\x1b[0m')))
            .toBe('\x1b[38;2;128;0;0mR\x1b[0m');
    });

    it('round-trips mixed content exactly', () => {
        const original = 'A \x1b[38;2;255;0;0mred\x1b[0m and \x1b[48;2;0;0;255mblue-bg\x1b[0m hall.';
        expect(encodeDescRuns(parseDescRuns(original))).toBe(original);
    });

    it('skips empty runs', () => {
        expect(encodeDescRuns([{ text: '', fg: [1, 2, 3], bg: null }])).toBe('');
    });
});
