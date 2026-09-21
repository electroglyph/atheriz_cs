import { MapPayload } from './types';
import { parseBackground } from './map';

export function asString(value: unknown): string {
    return typeof value === 'string' ? value : '';
}

export function asBoolean(value: unknown): boolean {
    if (typeof value === 'boolean') return value;
    if (typeof value === 'number') return value !== 0;
    if (typeof value === 'string') {
        const normalized = value.trim().toLowerCase();
        return normalized === 'true' || normalized === '1' || normalized === 'yes' || normalized === 'on';
    }
    return false;
}

export function asPosition(value: unknown): [number, number] | undefined {
    if (!Array.isArray(value) || typeof value[0] !== 'number' || typeof value[1] !== 'number') return undefined;
    // Reject NaN/Infinity (they corrupt viewport math downstream).
    // Floats are kept: map coordinates may be fractional.
    if (!Number.isFinite(value[0]) || !Number.isFinite(value[1])) return undefined;
    return [value[0], value[1]];
}

/**
 * String caps for legend entries (truncate with slice, code-point safe):
 * - symbol: 64 raw chars max — a symbol is one glyph, optionally wrapped in
 *   ANSI escapes, so the cap applies to the raw string including escapes.
 * - desc: 256 chars max.
 */
export const MAX_LEGEND_SYMBOL_LENGTH = 64;
export const MAX_LEGEND_DESC_LENGTH = 256;

function truncateText(value: string, maxLength: number): string {
    if (value.length <= maxLength) return value;
    return [...value].slice(0, maxLength).join('');
}

export function asLegend(value: unknown): MapPayload['legend'] {
    if (!Array.isArray(value)) return [];
    return value.flatMap((entry) => {
        if (Array.isArray(entry) && typeof entry[0] === 'string') {
            const rawDesc = entry[1];
            const desc = typeof rawDesc === 'string' ? rawDesc : rawDesc == null ? '' : null;
            if (desc === null) return [];
            const coords = asPosition(entry[2]);
            return [{ symbol: truncateText(entry[0], MAX_LEGEND_SYMBOL_LENGTH), desc: truncateText(desc, MAX_LEGEND_DESC_LENGTH), coords }];
        }
        if (typeof entry === 'object' && entry !== null &&
            typeof (entry as { symbol?: unknown }).symbol === 'string') {
            const raw = entry as { symbol: string; desc?: unknown; coords?: unknown };
            const rawDesc = raw.desc;
            let desc: string | null;
            if (typeof rawDesc === 'string') desc = rawDesc;
            else if (rawDesc == null) desc = '';
            else return [];
            return [{ symbol: truncateText(raw.symbol, MAX_LEGEND_SYMBOL_LENGTH), desc: truncateText(desc, MAX_LEGEND_DESC_LENGTH), coords: asPosition(raw.coords) }];
        }
        return [];
    });
}

export function normalizeShowLegend(value: unknown): boolean {
    if (value === undefined) return true;
    if (value === false || value === 0 || value === '' || value === null) return false;
    if (typeof value === 'string') {
        const normalized = value.trim().toLowerCase();
        if (normalized === 'false' || normalized === '0' || normalized === 'no' || normalized === 'off') return false;
        if (normalized === 'true' || normalized === '1' || normalized === 'yes' || normalized === 'on') return true;
        return normalized.length > 0;
    }
    if (typeof value === 'number') return value !== 0;
    if (typeof value === 'boolean') return value;
    return Boolean(value);
}

export function asMapPayload(value: unknown): MapPayload {
    if (typeof value !== 'object' || value === null) return { map: '' };
    const data = value as Partial<MapPayload>;
    return {
        map: typeof data.map === 'string' ? data.map : '',
        pos: asPosition(data.pos),
        symbol: typeof data.symbol === 'string' ? data.symbol : undefined,
        legend: asLegend(data.legend),
        min_x: typeof data.min_x === 'number' && Number.isFinite(data.min_x) ? data.min_x : 0,
        max_y: typeof data.max_y === 'number' && Number.isFinite(data.max_y) ? data.max_y : 0,
        area: typeof data.area === 'string' ? data.area : undefined,
        show_legend: normalizeShowLegend(data.show_legend),
        background: parseBackground(data.background),
    };
}
