import { ConnectionState, encodeWireMessage, MessageHandler, parseWireMessage } from './types';

export interface WebSocketLike {
    readyState: number;
    binaryType?: string;
    onopen: ((event: Event) => void) | null;
    onclose: ((event: CloseEvent) => void) | null;
    onerror: ((event: Event) => void) | null;
    onmessage: ((event: MessageEvent) => void) | null;
    send(data: string): void;
    close(): void;
}

export interface ConnectionOptions {
    createSocket?: (url: string) => WebSocketLike;
    onMessage: MessageHandler;
    onStateChange?: (state: ConnectionState) => void;
    onInvalidMessage?: () => void;
    onError?: (event: Event) => void;
    minReconnectDelayMs?: number;
    maxReconnectDelayMs?: number;
    maxReconnectAttempts?: number;
    connectTimeoutMs?: number;
}

const STABLE_CONNECTION_MS = 30_000;
const CONNECT_TIMEOUT_MS = 10_000;
// How long a server-announced restart is waited out before the normal
// retry budget applies again: build + checkpoint + handover + boot runs
// seconds on the fast path and a minute or more when the build is cold.
export const RESTART_WAIT_MS = 5 * 60_000;
// Game-terminal retry budget: an in-game restart drops the socket for the
// build + checkpoint + handover + boot gap (the observed fast path is ~6 s;
// a build-first restart runs a minute or more), far longer than the
// blip-sized default of 3 below. The terminal client passes this so a
// restart reconnects on its own instead of stranding the player.
export const GAME_CLIENT_MAX_RECONNECT_ATTEMPTS = 20;
export const OPEN_STATE = 1;
// Inbound frames larger than this are treated as invalid instead of parsed.
export const MAX_INBOUND_FRAME_BYTES = 4 * 1024 * 1024;

export function websocketUrl(locationLike?: Pick<Location, 'protocol' | 'host'>, token?: string | null): string {
    const currentLocation = locationLike ?? (
        typeof window === 'undefined'
            ? { protocol: 'http:', host: 'localhost' }
            : window.location
    );
    const protocol = currentLocation.protocol === 'https:' ? 'wss:' : 'ws:';
    const host = currentLocation.host || 'localhost';
    const base = `${protocol}//${host}/ws`;
    const t = token ?? gameTokenFromFragment();
    return t ? `${base}?token=${encodeURIComponent(t)}` : base;
}

// Landing-page auto-login: the Play button opens the webclient with the game
// token in the URL fragment (#token=...), which never reaches the server on
// page load. The token is forwarded as the ?token= WebSocket query the
// server auto-login accepts. Explicit arg beats fragment; absent fragment
// yields null so plain logins keep the bare /ws URL.
export function gameTokenFromFragment(hash?: string): string | null {
    const raw = hash ?? (typeof window === 'undefined' ? '' : window.location.hash);
    const match = /^#token=(.+)$/.exec(raw);
    if (!match) return null;
    try {
        const token = decodeURIComponent(match[1]);
        return token || null;
    } catch {
        return null;
    }
}

export function decodeWireData(data: unknown): string | null {
    if (typeof data === 'string') return data;
    if (data instanceof ArrayBuffer) return new TextDecoder().decode(data);
    if (typeof ArrayBuffer !== 'undefined' && Object.prototype.toString.call(data) === '[object ArrayBuffer]') {
        return new TextDecoder().decode(data as ArrayBuffer);
    }
    if (ArrayBuffer.isView(data as ArrayBufferView)) return new TextDecoder().decode(data as ArrayBufferView);
    return null;
}

function isBlobLike(data: unknown): data is { arrayBuffer(): Promise<ArrayBuffer>; text?: () => Promise<string> } {
    return typeof data === 'object'
        && data !== null
        && typeof (data as { arrayBuffer?: unknown }).arrayBuffer === 'function';
}

/**
 * Async variant of decodeWireData that additionally handles Blob frames
 * (the WebSocket default binaryType). Blobs cannot be decoded synchronously,
 * so the sync decoder keeps returning null for them while this awaits
 * arrayBuffer(), falling back to text().
 */
export async function decodeWireDataAsync(data: unknown): Promise<string | null> {
    if (!isBlobLike(data)) return decodeWireData(data);
    try {
        const text = decodeWireData(await data.arrayBuffer());
        if (text !== null) return text;
    } catch {
        // Fall through to the text() fallback below.
    }
    try {
        if (typeof data.text === 'function') return await data.text();
    } catch {
        return null;
    }
    return null;
}

export class WebSocketConnection {
    private readonly createSocket: (url: string) => WebSocketLike;
    private readonly onMessage: MessageHandler;
    private readonly onStateChange?: (state: ConnectionState) => void;
    private readonly onInvalidMessage?: () => void;
    private readonly onError?: (event: Event) => void;
    private readonly minReconnectDelayMs: number;
    private readonly maxReconnectDelayMs: number;
    private readonly maxReconnectAttempts: number;
    private readonly connectTimeoutMs: number;
    private socket: WebSocketLike | null = null;
    private reconnectTimer: ReturnType<typeof setTimeout> | undefined;
    private connectTimer: ReturnType<typeof setTimeout> | undefined;
    private reconnectAttempt = 0;
    private openedAt = 0;
    private manuallyClosed = false;
    private state: ConnectionState = 'idle';
    // Set when the server announces a committed restart (`restarting`
    // wire frame, sent just before it stops): reconnects until this
    // timestamp don't consume the retry budget, so the restart gap —
    // far longer than a blip — never strands the client.
    private restartWaitUntil = 0;

    constructor(options: ConnectionOptions) {
        this.createSocket = options.createSocket ?? ((url) => new WebSocket(url));
        this.onMessage = options.onMessage;
        this.onStateChange = options.onStateChange;
        this.onInvalidMessage = options.onInvalidMessage;
        this.onError = options.onError;
        this.minReconnectDelayMs = options.minReconnectDelayMs ?? 500;
        this.maxReconnectDelayMs = options.maxReconnectDelayMs ?? 15_000;
        this.maxReconnectAttempts = Math.max(0, options.maxReconnectAttempts ?? 3);
        this.connectTimeoutMs = options.connectTimeoutMs ?? CONNECT_TIMEOUT_MS;
    }

    connect(): void {
        const wasManuallyClosed = this.manuallyClosed;
        this.manuallyClosed = false;
        if (this.state === 'failed' || wasManuallyClosed) {
            this.reconnectAttempt = 0;
            this.restartWaitUntil = 0;
        }
        this.clearReconnectTimer();
        if (this.socket?.readyState === OPEN_STATE || this.state === 'connecting') return;

        this.setState('connecting');
        const socket = this.createSocket(websocketUrl());
        this.socket = socket;
        try {
            socket.binaryType = 'arraybuffer';
        } catch {
            // Read-only shims keep Blob frames; those decode via the async path.
        }
        socket.onopen = () => {
            if (this.socket !== socket) return;
            this.clearConnectTimer();
            this.openedAt = Date.now();
            // Attempts burned while waiting out a restart belong to the
            // gap, not to the new session: the first flap after a restart
            // gets the full budget instead of failing instantly.
            if (this.restartWaitUntil !== 0) this.reconnectAttempt = 0;
            this.restartWaitUntil = 0;
            this.setState('open');
        };
        socket.onmessage = (event) => {
            if (this.socket !== socket) return;
            const data: unknown = event.data;
            if (isBlobLike(data)) {
                void decodeWireDataAsync(data).then((raw) => {
                    if (this.socket !== socket) return;
                    if (raw === null || raw.length > MAX_INBOUND_FRAME_BYTES) {
                        this.onInvalidMessage?.();
                        return;
                    }
                    this.handleRaw(raw);
                });
                return;
            }
            if (typeof data === 'string' && data.length > MAX_INBOUND_FRAME_BYTES) {
                this.onInvalidMessage?.();
                return;
            }
            const raw = decodeWireData(data);
            if (raw === null || raw.length > MAX_INBOUND_FRAME_BYTES) {
                this.onInvalidMessage?.();
                return;
            }
            this.handleRaw(raw);
        };
        socket.onerror = (event) => {
            if (this.socket === socket) this.onError?.(event);
        };
        socket.onclose = () => {
            if (this.socket !== socket) return;
            this.socket = null;
            this.clearConnectTimer();
            if (!this.manuallyClosed && this.openedAt > 0 && Date.now() - this.openedAt >= STABLE_CONNECTION_MS) {
                this.reconnectAttempt = 0;
            }
            this.openedAt = 0;
            if (this.manuallyClosed) {
                this.setState('closed');
                return;
            }
            // A restart the server announced is waited out, not counted:
            // the gap until the replacement binds dwarfs the blip budget.
            // Once the wait expires the budget starts fresh (see
            // consumeExpiredRestartWait), so only an unannounced outage
            // fails here.
            if (this.isWaitingForRestart()) {
                this.scheduleReconnect();
                this.setState('closed');
                return;
            }
            this.consumeExpiredRestartWait();
            if (this.reconnectAttempt >= this.maxReconnectAttempts) {
                this.setState('failed');
                return;
            }
            this.scheduleReconnect();
            this.setState('closed');
        };
        this.clearConnectTimer();
        if (this.connectTimeoutMs > 0) {
            this.connectTimer = setTimeout(() => this.handleConnectTimeout(socket), this.connectTimeoutMs);
        }
    }

    close(): void {
        this.manuallyClosed = true;
        this.restartWaitUntil = 0;
        this.clearReconnectTimer();
        this.clearConnectTimer();
        this.socket?.close();
        this.socket = null;
        this.setState('closed');
    }

    send(command: string, args: unknown[] = [], kwargs: Record<string, unknown> = {}): boolean {
        if (this.socket?.readyState !== OPEN_STATE) return false;
        try {
            this.socket.send(encodeWireMessage(command, args, kwargs));
        } catch {
            return false;
        }
        return true;
    }

    getState(): ConnectionState {
        return this.state;
    }

    /**
     * The server announced a committed restart just before stopping.
     * Reconnects keep going until `patienceMs` runs out without consuming
     * the retry budget — the caller shows the waiting notice.
     */
    notifyRestarting(patienceMs: number = RESTART_WAIT_MS): void {
        this.restartWaitUntil = Date.now() + Math.max(0, patienceMs);
    }

    /** Whether a server-announced restart is still being waited out. */
    isWaitingForRestart(): boolean {
        return Date.now() < this.restartWaitUntil;
    }

    // A wait that ran out hands a fresh budget to the world after the
    // restart: attempts burned while waiting must not fail the first
    // redials of whatever came back (or didn't).
    private consumeExpiredRestartWait(): void {
        if (this.restartWaitUntil !== 0 && !this.isWaitingForRestart()) {
            this.restartWaitUntil = 0;
            this.reconnectAttempt = 0;
        }
    }

    private handleRaw(raw: string): void {
        const message = parseWireMessage(raw);
        if (message) this.onMessage(message);
        else this.onInvalidMessage?.();
    }

    private handleConnectTimeout(socket: WebSocketLike): void {
        if (this.socket !== socket || this.state !== 'connecting') return;
        if (socket.readyState === OPEN_STATE) return;
        // An error without a close would otherwise leave 'connecting' forever.
        try {
            socket.close();
        } catch {
            // The stale socket is dropped below regardless.
        }
        // If close() fired onclose synchronously it already scheduled a
        // reconnect and cleared this.socket; otherwise take that path here.
        if (this.socket !== socket) return;
        this.socket = null;
        this.openedAt = 0;
        if (this.isWaitingForRestart()) {
            this.scheduleReconnect();
            this.setState('closed');
            return;
        }
        this.consumeExpiredRestartWait();
        if (this.reconnectAttempt >= this.maxReconnectAttempts) {
            this.setState('failed');
            return;
        }
        this.scheduleReconnect();
        this.setState('closed');
    }

    private setState(state: ConnectionState): void {
        this.state = state;
        this.onStateChange?.(state);
    }

    private scheduleReconnect(): void {
        this.clearReconnectTimer();
        const exponential = Math.min(
            this.maxReconnectDelayMs,
            this.minReconnectDelayMs * (2 ** this.reconnectAttempt),
        );
        const jitter = Math.floor(Math.random() * Math.min(250, exponential / 4));
        this.reconnectAttempt += 1;
        this.reconnectTimer = setTimeout(() => this.connect(), exponential + jitter);
    }

    private clearReconnectTimer(): void {
        if (this.reconnectTimer !== undefined) {
            clearTimeout(this.reconnectTimer);
            this.reconnectTimer = undefined;
        }
    }

    private clearConnectTimer(): void {
        if (this.connectTimer !== undefined) {
            clearTimeout(this.connectTimer);
            this.connectTimer = undefined;
        }
    }
}
