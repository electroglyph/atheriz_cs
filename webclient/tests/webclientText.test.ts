import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import {
    DEFAULT_TEXT_COLOR,
    DEFAULT_TEXT_RESET,
    formatPrompt,
    formatTextOutput,
    normalizeServerText,
    wrapText,
} from '../src/webclient/text';

describe('webclient text rendering', () => {
    it('applies the legacy gray default to plain text', () => {
        expect(normalizeServerText('hello', 80, false)).toBe(`${DEFAULT_TEXT_COLOR}hello`);
    });

    it('preserves explicit colors and restores gray after reset or white', () => {
        const red = '\x1b[38;2;255;0;0m';
        expect(normalizeServerText(`${red}red\x1b[0m white\x1b[37mwhite`, 80, false)).toBe(
            `${red}red${DEFAULT_TEXT_RESET} white${DEFAULT_TEXT_COLOR}white`,
        );
    });

    it('does not inject color in screen-reader mode', () => {
        expect(normalizeServerText('plain', 80, true)).toBe('plain');
    });

    it('wraps visible text without counting ANSI sequences', () => {
        const red = '\x1b[31m';
        expect(wrapText(`${red}one two`, 5)).toContain(`${red}one \x1b[0m\n${red}two`);
    });

    it('clears the old prompt without reprinting it after text', () => {
        // Text frames erase the live prompt but must not stamp a fresh copy
        // onto the stream: one stamped copy per frame used to seal a ">"
        // line per message into scrollback via the drain newline.
        expect(formatTextOutput('hello', 80, false, '> ', true)).toBe(
            `\r  \r\x1b[0m${DEFAULT_TEXT_COLOR}hello\x1b[0m`,
        );
        expect(formatPrompt('new> ', 'old> ', true)).toBe('\r     \r\x1b[0mnew> \x1b[0m');
    });

    it('never stamps the prompt into the text stream', () => {
        for (const printed of [true, false]) {
            const out = formatTextOutput('hello', 80, false, '>', printed);
            expect(out).toContain('hello');
            expect(out.endsWith('>')).toBe(false);
        }
        // Erase width still follows the stored prompt so the live line clears.
        expect(formatTextOutput('hello', 80, false, '>>>', true).startsWith('\r   \r')).toBe(true);
        // Unprinted branch carries no prompt either.
        expect(formatTextOutput('hello', 80, false, '>', false)).toBe(
            `\x1b[0m${DEFAULT_TEXT_COLOR}hello\x1b[0m`,
        );
    });
});

describe('prompt lives on the bottom line, not in the text stream', () => {
    const main = readFileSync(join(__dirname, '..', 'src/webclient/main.ts'), 'utf8');

    it('writeText erases the live prompt without reprinting it', () => {
        const block = main.match(/function writeText[\s\S]*?\n\}/);
        expect(block).toBeTruthy();
        expect(block![0]).toContain('promptPrinted = false');
        expect(block![0]).not.toContain('prompt.length > 0');
    });

    it('writer drain redraws the stored prompt exactly once per drain', () => {
        expect(main).toContain('redrawPrompt()');
        const fn = main.match(/function redrawPrompt[\s\S]*?\n\}/);
        expect(fn).toBeTruthy();
        expect(fn![0]).toContain('if (promptPrinted || prompt.length === 0) return;');
        expect(fn![0]).toContain("recorder.output('o', chunk)");
        expect(fn![0]).toContain('promptPrinted = true');
    });
});
