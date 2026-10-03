import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
// Inlined so the viewer ships as one stable file (no hashed CSS asset
// for the landing page to reference).
import xtermCss from '@xterm/xterm/css/xterm.css?inline';

const SNAPSHOT_URL = '/server-log';
const STREAM_URL = '/server-log/stream';

// xterm needs carriage returns: the snapshot and stream frames are
// newline-joined, so translate before writing.
function toRows(text: string): string {
    return text.split('\n').join('\r\n');
}

const MONTHS = [
    'January', 'February', 'March', 'April', 'May', 'June',
    'July', 'August', 'September', 'October', 'November', 'December',
];

function pad2(n: number): string {
    return n < 10 ? `0${n}` : `${n}`;
}

// The server prefixes every line with its UTC epoch; render it in the
// viewer's timezone in the same shape the game uses server-side.
// Lines without a prefix (older servers) pass through untouched.
export function renderLogLine(line: string): string {
    const match = /^(\d+) ([\s\S]*)$/.exec(line);
    if (!match) return line;
    const date = new Date(parseInt(match[1], 10) * 1000);
    const stamp =
        `${pad2(date.getDate())} ${MONTHS[date.getMonth()]}, ` +
        `${date.getFullYear()} ${pad2(date.getHours())}:` +
        `${pad2(date.getMinutes())}:${pad2(date.getSeconds())}`;
    return `[${stamp}] ${match[2]}`;
}

function hideSection(doc: Document): void {
    // No log (endpoint disabled or unreachable): hide the section instead
    // of an error line. A failed stream after a good snapshot keeps the
    // snapshot with a note (see onerror below).
    const section = doc.getElementById('server-log-section');
    if (section instanceof HTMLElement) section.hidden = true;
}

export function startServerLog(doc: Document = document): void {
    const el = doc.getElementById('server-log-terminal');
    if (!(el instanceof HTMLElement)) return;
    // Hidden until the snapshot lands: hiding after a failed fetch lets
    // the empty terminal flash on screen for a network roundtrip.
    // (The template also ships the section hidden for pre-script paint.)
    hideSection(doc);
    const style = doc.createElement('style');
    style.textContent = xtermCss;
    doc.head.append(style);
    const term = new Terminal({
        disableStdin: true,
        theme: { background: '#000000', foreground: '#cccccc' },
    });
    const fit = new FitAddon();
    term.loadAddon(fit);
    term.open(el);
    fit.fit();
    window.addEventListener('resize', () => fit.fit());
    let live = false;
    fetch(SNAPSHOT_URL, { headers: { Accept: 'text/plain' } })
        .then(async (res) => {
            if (!res.ok) {
                hideSection(doc);
                return;
            }
            const section = doc.getElementById('server-log-section');
            if (section instanceof HTMLElement) section.hidden = false;
            // The terminal was opened and fitted while the section was
            // hidden (zero size), so fit again now that it has real
            // dimensions — otherwise lines wrap at the hidden-time width
            // instead of the full pane.
            fit.fit();
            term.write(toRows((await res.text()).split('\n').map(renderLogLine).join('\n')));
            // Snapshot and stream overlap: without the snapshot's last id
            // the stream replays the same history the snapshot just
            // rendered, so every line appears twice on reload. The server
            // exposes the newest id as X-Last-Seq; hand it back as ?lastId=
            // so the stream replays only newer lines (reconnects still use
            // Last-Event-ID).
            let streamUrl = STREAM_URL;
            try {
                const lastSeq = Number.parseInt(res.headers.get('X-Last-Seq') ?? '', 10);
                if (Number.isFinite(lastSeq) && lastSeq > 0) streamUrl += `?lastId=${lastSeq}`;
            } catch {
                // Old server without the header: fall back to the bare stream.
            }
            // Resume point for re-opens: snapshot id first, then the newest
            // frame actually rendered (EventSource exposes it per message).
            // Resuming from the last seen frame — never the stale snapshot
            // id — is what keeps a re-open from replaying rendered lines.
            let resumeQuery = streamUrl.startsWith(`${STREAM_URL}?`)
                ? streamUrl.slice(STREAM_URL.length)
                : '';
            let failures = 0;
            let noteShown = false;
            const openStream = (): void => {
                const source = new EventSource(`${STREAM_URL}${resumeQuery}`);
                source.onmessage = (event) => {
                    live = true;
                    failures = 0;
                    if (typeof event.lastEventId === 'string' && event.lastEventId !== '') {
                        resumeQuery = `?lastId=${event.lastEventId}`;
                    }
                    const data = typeof event.data === 'string' ? event.data : String(event.data);
                    term.write(`${toRows(renderLogLine(data))}\r\n`);
                };
                source.onerror = () => {
                    // A failed stream may be wedged (no more frames and no
                    // browser reconnect), so close it and re-open from the
                    // last rendered frame instead of trusting the retry.
                    source.close();
                    if (!live && !noteShown) {
                        noteShown = true;
                        term.writeln('Server log live stream unavailable — showing snapshot.');
                    }
                    failures++;
                    setTimeout(openStream, Math.min(1000 * 2 ** (failures - 1), 30000));
                };
            };
            openStream();
        })
        .catch(() => hideSection(doc));
}

startServerLog();
