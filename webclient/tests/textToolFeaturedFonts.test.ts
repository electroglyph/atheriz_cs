// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

vi.mock('../src/utils/googleFontLoader', async importOriginal => {
  const actual = await importOriginal<typeof import('../src/utils/googleFontLoader')>();
  return { ...actual, loadFontFull: vi.fn(async (_family: string) => {}) };
});

import { loadFontFull } from '../src/utils/googleFontLoader';
import { TextToolDialog } from '../src/ui/TextToolDialog';
import { CanvasState } from '../src/state/CanvasState';
import type { AppState } from '../src/types';

function stubIO() {
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
}

function setupDom() {
  document.body.innerHTML = `
    <div id="text-tool-modal" class="hidden">
      <textarea id="text-tool-input"></textarea>
      <select id="text-tool-font"></select>
      <select id="text-tool-style"></select>
      <select id="text-tool-align"></select>
      <input id="text-tool-max-width" value="24" />
      <span id="text-tool-max-width-val"></span>
      <input id="text-tool-stretch" value="100" />
      <span id="text-tool-stretch-val"></span>
      <canvas id="text-tool-preview"></canvas>
      <button id="btn-text-cancel"></button>
      <button id="btn-text-confirm"></button>
      <button id="text-tool-google-fonts-btn"></button>
      <div id="text-chafa-options-container"></div>
    </div>
    <div id="google-font-picker-modal" class="hidden">
      <input id="gfp-search" type="text" />
      <div id="gfp-tabs"></div>
      <div id="gfp-list"></div>
      <div id="gfp-sentinel"></div>
      <button id="gfp-cancel"></button>
      <button id="gfp-ok"></button>
    </div>`;
}

function makeDialog(): TextToolDialog {
  const appState = {
    bgColor: [0, 0, 0],
    fgColor: [204, 204, 204],
    fontFamily: 'Arial',
  } as unknown as AppState;
  const canvasState = new CanvasState(24, 24);
  return new TextToolDialog(
    appState,
    canvasState,
    () => {},
    () => ({ width: 8, height: 16, font: '8px monospace', advance: 8 }),
  );
}

function fontSelect(): HTMLSelectElement {
  return document.getElementById('text-tool-font') as HTMLSelectElement;
}

beforeEach(() => {
  stubIO();
  setupDom();
  Object.defineProperty(document, 'fonts', {
    value: { load: async () => [], ready: Promise.resolve() },
    configurable: true,
  });
});

afterEach(() => {
  document.body.innerHTML = '';
  vi.clearAllMocks();
  vi.unstubAllGlobals();
});

describe('TextToolDialog featured Google Fonts section', () => {
  it('lists local fonts plus all 50 featured fonts under their own separator', async () => {
    const dialog = makeDialog();
    await dialog.open();
    const options = Array.from(fontSelect().options);
    const separator = options.find(o => o.textContent === '── Featured Google Fonts ──');
    expect(separator).toBeTruthy();
    expect(separator!.disabled).toBe(true);
    // Local entries still come first, unchanged.
    expect(options[0].textContent).toBe('Unifont');
    const roboto = options.find(o => o.textContent === 'Roboto');
    expect(roboto).toBeTruthy();
    expect(roboto!.value).toBe('Roboto');
    expect(options.find(o => o.textContent === 'Black Ops One')).toBeTruthy();
    // 7 local entries + separator + 50 featured, in that order.
    expect(options).toHaveLength(58);
    expect(options[7]).toBe(separator);
  });

  it('does not duplicate the featured section on reopen', async () => {
    const dialog = makeDialog();
    await dialog.open();
    dialog.close();
    await dialog.open();
    const separators = Array.from(fontSelect().options).filter(
      o => o.textContent === '── Featured Google Fonts ──',
    );
    expect(separators).toHaveLength(1);
  });

  it('loads the featured font before previewing on selection', async () => {
    const dialog = makeDialog();
    await dialog.open();
    const select = fontSelect();
    select.value = 'Roboto';
    select.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(vi.mocked(loadFontFull)).toHaveBeenCalledWith('Roboto'));
    dialog.close();
  });
});
