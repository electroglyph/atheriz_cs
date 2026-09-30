export type Color = [number, number, number];

export interface Cell {
  char: string;
  fg: Color;
  bg: Color;
  bold?: boolean;
  italic?: boolean;
  underline?: boolean;
}

export type TypeStyle = "regular" | "bold" | "italic" | "underline";

export type RectMode = "light" | "rounded" | "double" | "heavy" | "custom";
export type OvalMode = "light" | "rounded" | "double" | "heavy" | "circle" | "custom";
export type LineMode = "light" | "rounded" | "double" | "heavy" | "custom";
export type GradientTarget = "foreground" | "background" | "both" | "luminance" | "inverse-luminance";
export type EyedropperTarget = "fg-fg" | "fg-bg" | "bg-fg" | "bg-bg";
export type SelectMode = "single" | "rectangle" | "lasso" | "magic" | "color-match" | "color-fuzzy";
export type RotateMode = "cw90" | "ccw90" | "180" | "flip-h" | "flip-v" | "free";
export type FillMode = "brush" | "foreground" | "background" | "gradient";

export interface Layer {
  id: string;
  name: string;
  visible: boolean;
  cells: Cell[][];
  overflowCells?: Map<string, Cell>;
}

export interface AppState {
  activeToolId: string;
  rectMode: RectMode;
  ovalMode: OvalMode;
  lineMode: LineMode;
  gradientTarget: GradientTarget;
  typeStyle: TypeStyle;
  selectedChar: string;
  fgColor: Color;
  bgColor: Color;
  fontFamily: string;
  gradientStops: Color[];
  selectMode: SelectMode;
  rotateMode: RotateMode;
  fillMode: FillMode;
  lineDiagonal: boolean;
  eyedropperTarget: EyedropperTarget;
}

export interface Point {
  x: number;
  y: number;
}

/**
 * Violet server-grid rect in viewport (canvas) coordinates: top-left cell
 * plus size in cells. The only region diffed, exported, previewed, and
 * sent to the server. Always contained in viewport storage.
 */
export interface ServerBounds {
  col: number;
  row: number;
  w: number;
  h: number;
}

/**
 * What a viewport growth did, in per-side amounts. Insertions at index 0
 * (left/top) shift every existing viewport coord right/down by the amount;
 * appends (right/bottom) leave existing indices alone. `col`/`row` are the
 * input point remapped into post-growth storage; `capped` means the point
 * is still outside storage (2048 cap) and must be dropped.
 */
export interface ViewportGrowth {
  col: number;
  row: number;
  addedLeft: number;
  addedTop: number;
  addedRight: number;
  addedBottom: number;
  capped: boolean;
}
