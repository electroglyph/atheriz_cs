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
            term.write(toRows((await res.text()).split('\n').map(renderLogLine).join('\n')));
            const source = new EventSource(STREAM_URL);
            source.onmessage = (event) => {
                live = true;
                const data = typeof event.data === 'string' ? event.data : String(event.data);
                term.write(`${toRows(renderLogLine(data))}\r\n`);
            };
            source.onerror = () => {
                if (!live) term.writeln('Server log live stream unavailable — showing snapshot.');
            };
        })
        .catch(() => hideSection(doc));
}

startServerLog();
