export const BUFFER_FINAL_SEQUENCE = '\x1b[0m\x1b[?25h\n';

// Drain trailer without the committing newline: reset + show-cursor only.
// Server `text` frames already end in \r\n, so appending another newline
// after such a drain prints a blank line. The full BUFFER_FINAL_SEQUENCE
// is still needed when the drain's last chunk did not end in a newline
// (prompt-only and raw `buffer` drains), where it commits the live line.
export const BUFFER_DRAIN_RESET = '\x1b[0m\x1b[?25h';

export function drainTrailer(lastChunkEndedNewline: boolean): string {
    return lastChunkEndedNewline ? BUFFER_DRAIN_RESET : BUFFER_FINAL_SEQUENCE;
}

// Trailing SGR color/reset sequences (formatTextOutput wraps every chunk in
// RESET...RESET): a chunk "ends in a newline" when the last non-SGR content
// is \n. A bare endsWith('\n') check is always false here, which made the
// drain emit the committing newline after every text drain (blank line).
const TRAILING_NEWLINE_IGNORING_SGR = /\n(\x1b\[[0-9;]*m)*$/;

export function chunkEndsWithNewline(chunk: string): boolean {
    return TRAILING_NEWLINE_IGNORING_SGR.test(chunk);
}

// Fallback delay for a stalled terminal write callback (e.g. disposed renderer).
export const BUFFER_WRITE_FALLBACK_MS = 100;

/**
 * Serializes terminal writes through a single queue so chunked `buffer`
 * output and interleaved `text` output keep wire order. A generation counter
 * makes `clear()` safe mid-write: a stale in-flight callback is ignored
 * instead of draining the next session's queue.
 */
export class SequentialWriter {
    private queue: string[] = [];
    private writing = false;
    private generation = 0;

    constructor(
        private readonly writeChunk: (chunk: string, done: () => void) => void,
        private readonly onDrained: () => void,
    ) {}

    enqueue(chunk: string): void {
        this.queue.push(chunk);
        this.flush();
    }

    clear(): void {
        this.generation += 1;
        this.queue.length = 0;
        this.writing = false;
    }

    get pending(): number {
        return this.queue.length;
    }

    get busy(): boolean {
        return this.writing;
    }

    private flush(): void {
        if (this.writing || this.queue.length === 0) return;
        this.writing = true;
        const generation = this.generation;
        const chunk = this.queue.shift();
        if (chunk === undefined) {
            this.writing = false;
            return;
        }
        let settled = false;
        const done = () => {
            if (settled || generation !== this.generation) return;
            settled = true;
            this.writing = false;
            if (this.queue.length > 0) this.flush();
            else this.onDrained();
        };
        try {
            this.writeChunk(chunk, done);
        } catch {
            done();
        }
        setTimeout(done, BUFFER_WRITE_FALLBACK_MS);
    }
}
