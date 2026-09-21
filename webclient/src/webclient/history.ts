/** Max characters kept per history entry; longer entries are truncated. */
export const MAX_ENTRY_SIZE = 4096;

export class CommandHistory {
    private readonly storageKey: string;
    private readonly maxSize: number;    private history: string[];
    private index = -1;
    private currentInput = '';
    private playerCommands: string[] = [];
    private completionMatches: string[] = [];

    constructor(storageKey = 'xtermia2CommandHistory', maxSize = 2048) {
        this.storageKey = storageKey;
        this.maxSize = maxSize;
        this.history = this.load();
    }

    add(value: string): void {
        if (!value) return;
        const capped = value.length > MAX_ENTRY_SIZE ? value.slice(0, MAX_ENTRY_SIZE) : value;
        this.history = [capped, ...this.history.filter((item) => item !== capped)].slice(0, this.maxSize);
        this.save();
        this.reset();
    }

    navigate(direction: 'up' | 'down', currentValue: string): string {
        if (this.index === -1) this.currentInput = currentValue;
        if (direction === 'up') this.index = Math.min(this.index + 1, this.history.length - 1);
        else this.index = Math.max(this.index - 1, -1);
        return this.index === -1 ? this.currentInput : (this.history[this.index] ?? this.currentInput);
    }

    reset(): void {
        this.index = -1;
        this.currentInput = '';
        this.completionMatches = [];
    }

    setPlayerCommands(commands: string[]): void {
        const filtered = commands.filter((c): c is string => typeof c === 'string' && c.length > 0);
        this.playerCommands = [...new Set([...this.playerCommands, ...filtered])].slice(0, this.maxSize);
    }

    findCompletions(value: string): void {
        if (!value) {
            this.completionMatches = [];
            return;
        }
        const candidates = [...new Set([...this.history, ...this.playerCommands])];
        this.completionMatches = candidates.filter((candidate) => {
            return candidate.startsWith(value) && candidate.length > value.length;
        });
    }

    getSuggestion(): string {
        return this.completionMatches[0] ?? '';
    }

    isNavigating(): boolean {
        return this.index !== -1;
    }

    save(): void {
        try {
            window.localStorage.setItem(this.storageKey, JSON.stringify(this.history));
        } catch {
            // Storage can be disabled or full; history must never block input.
        }
    }

    clear(): void {
        this.history = [];
        this.reset();
        this.save();
    }

    private load(): string[] {
        try {
            const saved = window.localStorage.getItem(this.storageKey);
            const parsed: unknown = saved ? JSON.parse(saved) : [];
            if (!Array.isArray(parsed)) return [];
            // Cap entry count and truncate oversized entries so a bloated or
            // hostile stored blob cannot exhaust memory on startup.
            return parsed
                .filter((item): item is string => typeof item === 'string')
                .slice(0, this.maxSize)
                .map((item) => (item.length > MAX_ENTRY_SIZE ? item.slice(0, MAX_ENTRY_SIZE) : item));
        } catch {
            return [];
        }
    }
}
