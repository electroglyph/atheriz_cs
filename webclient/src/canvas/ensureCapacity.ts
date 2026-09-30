import { ServerBounds, ViewportGrowth } from '../types';
import { CanvasState } from '../state/CanvasState';
import { ToolContext } from '../tools/Tool';

/**
 * What ensureToolCapacity did: the remapped (post-growth) points, the
 * points dropped at the 2048 cap, and whether the helper already pushed
 * an undo checkpoint (callers must skip their own push then, so one
 * Ctrl+Z reverts paint, growth, and bounds expansion together).
 */
export interface CapacityResult {
    points: { col: number; row: number }[];
    /** Post-growth `"col,row"` keys of points dropped at the cap. Growth
     * shifts every stored coord uniformly, so callers work entirely in
     * the post-growth frame: shift inputs by `shift`, then compare
     * outputs against this set directly. */
    dropped: Set<string>;
    /** Uniform coord shift applied by left/top inserts. Add to every
     * pre-growth viewport coord before painting, selecting, or reporting. */
    shift: { col: number; row: number };
    pushed: boolean;
}

/**
 * Pure planner shared by ensureToolCapacity and unit tests: given storage
 * dims and a set of viewport points, compute the combined growth (one
 * ensureViewportFor call covers all points, so multi-point strokes shift
 * overlays exactly once) and which points survive the 2048 cap.
 */
export function planCapacityFor(
    width: number,
    height: number,
    points: { col: number; row: number }[],
): { growth: Omit<ViewportGrowth, 'col' | 'row'>; surviving: { col: number; row: number }[]; dropped: { col: number; row: number }[] } {
    let minCol = width;
    let minRow = height;
    let maxCol = -1;
    let maxRow = -1;
    for (const p of points) {
        if (p.col < minCol) minCol = p.col;
        if (p.row < minRow) minRow = p.row;
        if (p.col > maxCol) maxCol = p.col;
        if (p.row > maxRow) maxRow = p.row;
    }
    if (points.length === 0) {
        return {
            growth: { addedLeft: 0, addedTop: 0, addedRight: 0, addedBottom: 0, capped: false },
            surviving: [],
            dropped: [],
        };
    }
    // One corner per extreme side covers the full needed range: the
    // planner takes max per side across both corners.
    const lo = CanvasState.planViewportGrowth(width, height, minCol, minRow);
    const hi = CanvasState.planViewportGrowth(width, height, maxCol, maxRow);
    const growth = {
        addedLeft: Math.max(lo.addedLeft, hi.addedLeft),
        addedTop: Math.max(lo.addedTop, hi.addedTop),
        addedRight: Math.max(lo.addedRight, hi.addedRight),
        addedBottom: Math.max(lo.addedBottom, hi.addedBottom),
        capped: lo.capped || hi.capped,
    };
    const newWidth = width + growth.addedLeft + growth.addedRight;
    const newHeight = height + growth.addedTop + growth.addedBottom;
    const surviving: { col: number; row: number }[] = [];
    const dropped: { col: number; row: number }[] = [];
    for (const p of points) {
        const c = p.col + growth.addedLeft;
        const r = p.row + growth.addedTop;
        if (c >= 0 && c < newWidth && r >= 0 && r < newHeight) {
            surviving.push({ col: c, row: r });
        } else {
            dropped.push({ col: p.col, row: p.row });
        }
    }
    return { growth, surviving, dropped };
}

/**
 * World-coord stability rules for viewport growth (mapedit.ts: world x =
 * col + originX, y = H-1-row + originY). Right appends need nothing;
 * bottom appends re-base originY down by the added rows; left inserts
 * re-base originX down by the added cols; top inserts are self-stable
 * (no rebase — re-basing would break stability).
 */
export function originDeltaForGrowth(growth: Omit<ViewportGrowth, 'col' | 'row' | 'capped'>): { dx: number; dy: number } {
    // `|| 0` normalizes -0 (right/top growth) to 0 for clean equality.
    return { dx: -growth.addedLeft || 0, dy: -growth.addedBottom || 0 };
}

/**
 * Ensure viewport storage (and, by default, the violet server-grid rect)
 * covers every point before a tool commits. Pushes one undo checkpoint
 * BEFORE mutating — but only when storage or bounds would actually
 * change — then grows storage, expands bounds, and notifies the host via
 * ctx.onViewportShifted so it can re-base the mapedit origin and offset
 * viewport-keyed overlays. Returns remapped points plus any dropped at
 * the 2048 cap. Without the host hook the state still grows (keeps bare
 * unit-test contexts working); the caller owns overlay correctness then.
 */
export function ensureToolCapacity(
    ctx: ToolContext,
    points: { col: number; row: number }[],
    options?: { expandBounds?: boolean },
): CapacityResult {
    const state = ctx.state;
    const expandBounds = options?.expandBounds ?? true;
    const none = (points: { col: number; row: number }[]): CapacityResult => ({
        points,
        dropped: new Set<string>(),
        shift: { col: 0, row: 0 },
        pushed: false,
    });
    if (points.length === 0) return none([]);
    const planned = planCapacityFor(state.width, state.height, points);
    const needsStorage = planned.growth.addedLeft !== 0 || planned.growth.addedTop !== 0 ||
        planned.growth.addedRight !== 0 || planned.growth.addedBottom !== 0;
    let pushed = false;
    let remapped = planned.surviving;
    let shiftCol = 0;
    let shiftRow = 0;
    if (needsStorage) {
        ctx.undoStack.push(state);
        pushed = true;
        // Growth amounts were computed up front, so apply them with per-
        // corner calls: the first call does the work, later ones no-op.
        // Left/top inserts shift later points, so remap through each step.
        const corners: { col: number; row: number }[] = [];
        if (planned.growth.addedLeft !== 0 || planned.growth.addedTop !== 0) {
            corners.push({ col: minCoord(points, p => p.col), row: minCoord(points, p => p.row) });
        }
        if (planned.growth.addedRight !== 0 || planned.growth.addedBottom !== 0) {
            corners.push({ col: maxCoord(points, p => p.col), row: maxCoord(points, p => p.row) });
        }
        let total: ViewportGrowth = {
            col: 0, row: 0,
            addedLeft: 0, addedTop: 0, addedRight: 0, addedBottom: 0, capped: false,
        };
        for (const corner of corners) {
            const g = state.ensureViewportFor(corner.col + shiftCol, corner.row + shiftRow);
            shiftCol += g.addedLeft;
            shiftRow += g.addedTop;
            total = {
                col: g.col, row: g.row,
                addedLeft: total.addedLeft + g.addedLeft,
                addedTop: total.addedTop + g.addedTop,
                addedRight: total.addedRight + g.addedRight,
                addedBottom: total.addedBottom + g.addedBottom,
                capped: total.capped || g.capped,
            };
        }
        remapped = planned.surviving;
        total.capped = total.capped || planned.dropped.length > 0;
        ctx.onViewportShifted?.(total);
    }
    if (expandBounds && remapped.length > 0) {
        const bounds: ServerBounds = {
            col: minCoord(remapped, p => p.col),
            row: minCoord(remapped, p => p.row),
            w: maxCoord(remapped, p => p.col) - minCoord(remapped, p => p.col) + 1,
            h: maxCoord(remapped, p => p.row) - minCoord(remapped, p => p.row) + 1,
        };
        if (!pushed && wouldExpandBounds(state, bounds)) {
            ctx.undoStack.push(state);
            pushed = true;
        }
        const boundsChanged = state.ensureBoundsForRect(bounds);
        if (boundsChanged) {
            // Publish the grown grid to the host (renderer, session,
            // origin mirrors). With bounds-only expansion this is the
            // single hook call and the zero growth keeps rebase/selection
            // a no-op with no cap warning. With storage growth the
            // earlier hook mirrored the pre-expansion bounds, so this
            // second call is what actually publishes the grown grid.
            // An undo entry always owns the change (growth pushes, else
            // the wouldExpand push above did), so Ctrl+Z reverts it whole.
            ctx.onViewportShifted?.({
                col: 0, row: 0,
                addedLeft: 0, addedTop: 0, addedRight: 0, addedBottom: 0,
                capped: false,
            });
        }
    }
    return {
        points: remapped,
        dropped: new Set(planned.dropped.map((p) => `${p.col + shiftCol},${p.row + shiftRow}`)),
        shift: { col: shiftCol, row: shiftRow },
        pushed,
    };
}

function wouldExpandBounds(state: CanvasState, rect: ServerBounds): boolean {
    const b = state.serverBounds;
    const c0 = Math.max(0, Math.min(Math.floor(rect.col), state.width - 1));
    const r0 = Math.max(0, Math.min(Math.floor(rect.row), state.height - 1));
    const c1 = Math.max(0, Math.min(Math.ceil(rect.col + rect.w) - 1, state.width - 1));
    const r1 = Math.max(0, Math.min(Math.ceil(rect.row + rect.h) - 1, state.height - 1));
    return c0 < b.col || r0 < b.row || c1 >= b.col + b.w || r1 >= b.row + b.h;
}

function minCoord(points: { col: number; row: number }[], pick: (p: { col: number; row: number }) => number): number {
    let m = Infinity;
    for (const p of points) m = Math.min(m, pick(p));
    return m;
}

function maxCoord(points: { col: number; row: number }[], pick: (p: { col: number; row: number }) => number): number {
    let m = -Infinity;
    for (const p of points) m = Math.max(m, pick(p));
    return m;
}
