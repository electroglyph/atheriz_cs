const ANSI_COLOR = /\x1B\[[0-9;]+m/g;
// Broad CSI matcher (cursor moves, clears, scrolls — same shape as map.ts).
// Used for measuring visible text: non-SGR sequences occupy no columns.
const ANSI_CSI_BROAD = /\x1B\[[0-?]*[ -/]*[@-~]/g;
// OSC sequences (hyperlinks, window titles, clipboard) terminated by BEL or ST.
const ANSI_OSC = /\x1B\][^\x07]*(?:\x07|\x1B\\)/g;
const ANSI_SGR_SINGLE = /\x1B\[[0-9;]+m/;
const LONE_ESC = /\x1B/g;
// An ESC that does not open a CSI sequence. Removed before the CSI pass so
// the ESC bytes of preserved SGR sequences are never eaten.
const LONE_ESC_BEFORE_CSI = /\x1B(?!\[[0-?]*[ -/]*[@-~])/g;
const ESC = '\x1B';
const RESET = `${ESC}[0m`;
const WHITE = `${ESC}[37m`;
const WHITE_BRIGHT = `${ESC}[97m`;
const WHITE_BRIGHT_BLACK = `${ESC}[90m`;

export const DEFAULT_TEXT_COLOR = `${ESC}[38;2;190;190;190m`;
export const DEFAULT_TEXT_RESET = `${RESET}${DEFAULT_TEXT_COLOR}`;

/**
 * Strip every ANSI escape: broad CSI (SGR colors AND cursor/clear/scroll
 * sequences), OSC sequences, and any leftover lone ESC byte. Server text is
 * measured and compared with this so control sequences never count as
 * visible columns and never leak into echoes or length math.
 */
export function stripAnsiBroad(value: string): string {
    return value.replace(ANSI_OSC, '').replace(ANSI_CSI_BROAD, '').replace(LONE_ESC, '');
}

/**
 * Strip every ANSI escape except SGR colors/styles, which are preserved so
 * intentional coloring survives. Use for values rendered with their colors
 * intact (legend symbols) where cursor movements and OSC must still go.
 */
export function stripNonSgrAnsi(value: string): string {
    return value
        .replace(ANSI_OSC, '')
        .replace(LONE_ESC_BEFORE_CSI, '')
        .replace(ANSI_CSI_BROAD, (match) => (ANSI_SGR_SINGLE.test(match) ? match : ''));
}

export function normalizeServerText(input: string, width: number, screenReader: boolean): string {
    if (screenReader) return input;
    let output = input;
    if (output.charAt(0) !== ESC) output = DEFAULT_TEXT_COLOR + output;
    output = wrapText(output, width);
    return output.replaceAll(RESET, DEFAULT_TEXT_RESET).replaceAll(WHITE, DEFAULT_TEXT_COLOR).replaceAll(WHITE_BRIGHT, DEFAULT_TEXT_COLOR).replaceAll(WHITE_BRIGHT_BLACK, DEFAULT_TEXT_COLOR);
}

// Text frames erase a live prompt but never reprint it. The stored prompt is
// redrawn once per writer drain instead (see redrawPrompt in main.ts), so a
// burst of N texts leaves one prompt on the bottom line instead of sealing
// one copy per frame into scrollback via the drain newline.
export function formatTextOutput(
    input: string,
    width: number,
    screenReader: boolean,
    prompt: string,
    promptPrinted: boolean,
): string {
    const output = normalizeServerText(input, width, screenReader);
    if (promptPrinted) {
        return `\r${' '.repeat(promptVisibleLength(prompt))}\r${RESET}${output}${RESET}`;
    }
    return `${RESET}${output}${RESET}`;
}

export function formatPrompt(prompt: string, oldPrompt: string, promptPrinted: boolean): string {
    const clear = promptPrinted ? `\r${' '.repeat(promptVisibleLength(oldPrompt))}\r` : '';
    return `${clear}${RESET}${prompt}${RESET}`;
}

export function promptVisibleLength(value: string): number {
    return [...stripAnsiBroad(value)].length;
}

export function wrapText(text: string, width: number): string {
    // `!(width > 0)` also catches NaN (NaN <= 0 is false, so `width <= 0`
    // would let NaN through into the wrapping math). Infinity still wraps.
    if (!text || !(width > 0)) return text;
    let result = '';
    let currentLineLength = 0;
    let currentColor = '';
    const words = text.split(/(\s+)/);

    for (const word of words) {
        if (!word) continue;
        let nextColor = currentColor;
        const codes = word.match(ANSI_COLOR);
        if (codes) {
            for (const code of codes) nextColor = code === RESET ? '' : code;
        }

        if (word.includes('\n')) {
            const parts = word.split('\n');
            for (let i = 0; i < parts.length; i++) {
                const part = parts[i];
                if (i > 0) {
                    result += '\n';
                    currentLineLength = 0;
                }
                if (!part) continue;
                const visiblePart = stripAnsiBroad(part);
                const partLen = [...visiblePart].length;
                const isWhitespacePart = /^\s*$/.test(visiblePart);
                if (currentLineLength + partLen > width) {
                    if (isWhitespacePart) {
                        if (currentLineLength > 0) {
                            result += `${RESET}\n${nextColor}`;
                            currentLineLength = 0;
                        }
                    } else {
                        if (currentLineLength > 0) {
                            result += `${RESET}\n${currentColor}`;
                            currentLineLength = 0;
                        }
                        result += part;
                        currentLineLength += partLen;
                    }
                } else {
                    result += part;
                    currentLineLength += partLen;
                }
            }
            currentColor = nextColor;
            continue;
        }

        const visibleWord = stripAnsiBroad(word);
        const wordLength = [...visibleWord].length;
        const isWhitespace = /^\s+$/.test(visibleWord);
        if (currentLineLength + wordLength > width) {
            if (isWhitespace) {
                if (currentLineLength > 0) {
                    result += `${RESET}\n${nextColor}`;
                    currentLineLength = 0;
                }
            } else {
                if (currentLineLength > 0) {
                    result += `${RESET}\n${currentColor}`;
                    currentLineLength = 0;
                }
                result += word;
                currentLineLength += wordLength;
            }
        } else {
            result += word;
            currentLineLength += wordLength;
        }
        currentColor = nextColor;
    }
    return result;
}
