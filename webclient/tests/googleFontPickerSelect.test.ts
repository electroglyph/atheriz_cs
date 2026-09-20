// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { GoogleFontPicker } from '../src/ui/GoogleFontPicker';

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
    <div id="google-font-picker-modal" class="hidden">
      <input id="gfp-search" type="text" />
      <div id="gfp-tabs"></div>
      <div id="gfp-list"></div>
      <div id="gfp-sentinel"></div>
      <button id="gfp-cancel">Cancel</button>
      <button id="gfp-ok">Use Font</button>
    </div>`;
}

function modal(): HTMLElement {
  return document.getElementById('google-font-picker-modal')!;
}

function okButton(): HTMLButtonElement {
  return document.getElementById('gfp-ok') as HTMLButtonElement;
}

function firstItems(count: number): HTMLElement[] {
  return Array.from(document.querySelectorAll('.gfp-item')).slice(0, count) as HTMLElement[];
}

beforeEach(() => {
  stubIO();
  setupDom();
});

afterEach(() => {
  document.body.innerHTML = '';
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('GoogleFontPicker select-then-confirm', () => {
  it('opens with no selection and a disabled Use Font button', () => {
    const picker = new GoogleFontPicker(() => {});
    picker.open();
    expect(modal().classList.contains('hidden')).toBe(false);
    expect(okButton().disabled).toBe(true);
    expect(document.querySelector('.gfp-item.selected')).toBeNull();
    picker.destroy();
  });

  it('click highlights one item without selecting or closing', () => {
    const onSelect = vi.fn();
    const picker = new GoogleFontPicker(onSelect);
    picker.open();
    const [first, second] = firstItems(2);
    first.click();
    expect(first.classList.contains('selected')).toBe(true);
    expect(okButton().disabled).toBe(false);
    expect(onSelect).not.toHaveBeenCalled();
    expect(modal().classList.contains('hidden')).toBe(false);
    second.click();
    expect(first.classList.contains('selected')).toBe(false);
    expect(second.classList.contains('selected')).toBe(true);
    expect(onSelect).not.toHaveBeenCalled();
    picker.destroy();
  });

  it('Use Font with no selection does nothing', () => {
    const onSelect = vi.fn();
    const picker = new GoogleFontPicker(onSelect);
    picker.open();
    okButton().click();
    expect(onSelect).not.toHaveBeenCalled();
    expect(modal().classList.contains('hidden')).toBe(false);
    picker.destroy();
  });

  it('Use Font confirms the highlighted family and closes', () => {
    const onSelect = vi.fn();
    const picker = new GoogleFontPicker(onSelect);
    picker.open();
    const [first] = firstItems(1);
    first.click();
    okButton().click();
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect.mock.calls[0][0]).toBe(first.dataset.family);
    expect(modal().classList.contains('hidden')).toBe(true);
    picker.destroy();
  });

  it('double-click confirms immediately', () => {
    const onSelect = vi.fn();
    const picker = new GoogleFontPicker(onSelect);
    picker.open();
    const [first] = firstItems(1);
    first.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect.mock.calls[0][0]).toBe(first.dataset.family);
    expect(modal().classList.contains('hidden')).toBe(true);
    picker.destroy();
  });

  it('Cancel selects nothing', () => {
    const onSelect = vi.fn();
    const picker = new GoogleFontPicker(onSelect);
    picker.open();
    const [first] = firstItems(1);
    first.click();
    (document.getElementById('gfp-cancel') as HTMLButtonElement).click();
    expect(onSelect).not.toHaveBeenCalled();
    expect(modal().classList.contains('hidden')).toBe(true);
    picker.destroy();
  });

  it('reopen resets the selection and disables Use Font', () => {
    const picker = new GoogleFontPicker(() => {});
    picker.open();
    const [first] = firstItems(1);
    first.click();
    expect(okButton().disabled).toBe(false);
    picker.close();
    picker.open();
    expect(okButton().disabled).toBe(true);
    expect(document.querySelector('.gfp-item.selected')).toBeNull();
    picker.destroy();
  });
});
