import { describe, expect, it } from 'vitest';
import { BUFFER_DRAIN_RESET, BUFFER_FINAL_SEQUENCE, chunkEndsWithNewline, drainTrailer, SequentialWriter } from '../src/webclient/buffer';
import { formatTextOutput, stripAnsiBroad } from '../src/webclient/text';

describe('webclient buffered output', () => {
    it('restores ANSI state, shows the cursor, and ends the buffer', () => {
        expect(BUFFER_FINAL_SEQUENCE).toBe('\x1b[0m\x1b[?25h\n');
    });

    it('drain trailer skips the newline after newline-terminated drains', () => {
        // Server `text` frames already end in \r\n: a further newline
        // would print a blank line, so the drain emits reset only.
        expect(drainTrailer(true)).toBe(BUFFER_DRAIN_RESET);
        expect(drainTrailer(true)).toBe('\x1b[0m\x1b[?25h');
        expect(drainTrailer(true).endsWith('\n')).toBe(false);
    });

    it('drain trailer keeps the committing newline otherwise', () => {
        // Prompt-only and raw `buffer` drains may leave the live line
        // unterminated; the full sequence commits it to history.
        expect(drainTrailer(false)).toBe(BUFFER_FINAL_SEQUENCE);
        expect(drainTrailer(false).endsWith('\n')).toBe(true);
    });

    it('detects newline through trailing reset sequences', () => {
        // Regression pin: formatTextOutput wraps every chunk in
        // RESET...RESET, so a bare endsWith('\n') is always false and the
        // drain printed a blank line after every single-message drain.
        const chunk = formatTextOutput("Shaman closes his eyes momentarily.\r\n", 80, false, '', false);
        expect(chunk.endsWith('\n')).toBe(false);
        expect(chunkEndsWithNewline(chunk)).toBe(true);
    });

    it('chunk newline detection covers raw and prompt shapes', () => {
        expect(chunkEndsWithNewline("line\r\n")).toBe(true);
        expect(chunkEndsWithNewline("line\r\n\x1b[0m")).toBe(true);
        expect(chunkEndsWithNewline("line\r\n\x1b[0m\x1b[38;2;190;190;190m")).toBe(true);
        expect(chunkEndsWithNewline('\x1b[0m100hp 100mp>\x1b[0m')).toBe(false);
        expect(chunkEndsWithNewline('no newline')).toBe(false);
        expect(chunkEndsWithNewline('')).toBe(false);
    });

    it('two single-message drains leave no blank line between messages', () => {
        // End-to-end pin through the real SequentialWriter, mirroring
        // main.ts write()/onDrained with an empty prompt (so redrawPrompt
        // is a no-op). Each server `text` frame arrives alone and drains
        // alone; the visible terminal bytes must be exactly the two lines.
        const written: string[] = [];
        let lastChunkEndedNewline = false;
        const writer = new SequentialWriter(
            (chunk, done) => { written.push(chunk); done(); },
            () => { written.push(drainTrailer(lastChunkEndedNewline)); },
        );
        const write = (text: string) => {
            const chunk = formatTextOutput(text, 80, false, '', false);
            lastChunkEndedNewline = chunkEndsWithNewline(chunk);
            writer.enqueue(chunk);
        };
        write("Battlemage utters the words, 'haste'.\r\n");
        write("Paladin closes her eyes momentarily.\r\n");
        const screen = stripAnsiBroad(written.join('')).replaceAll('\r\n', '\n');
        expect(screen).toBe("Battlemage utters the words, 'haste'.\nPaladin closes her eyes momentarily.\n");
    });
});
