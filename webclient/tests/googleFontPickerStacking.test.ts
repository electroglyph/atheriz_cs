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

// Parent Text tool dialog open underneath, picker on top of it.
function setupDom() {
  document.body.innerHTML = `
    <div id="text-tool-modal" class="modal">
      <button id="opener">G Fonts</button>
    </div>
    <div id="google-font-picker-modal" class="modal hidden">
      <input id="gfp-search" type="text" />
      <div id="gfp-tabs"></div>
      <div id="gfp-list"></div>
      <div id="gfp-sentinel"></div>
      <button id="gfp-cancel">Cancel</button>
      <button id="gfp-ok">Use Font</button>
    </div>`;
}

function parentModal(): HTMLElement {
  return document.getElementById('text-tool-modal')!;
}

function pickerModal(): HTMLElement {
  return document.getElementById('google-font-picker-modal')!;
}

function okButton(): HTMLButtonElement {
  return document.getElementById('gfp-ok') as HTMLButtonElement;
}

function searchInput(): HTMLInputElement {
  return document.getElementById('gfp-search') as HTMLInputElement;
}

function firstItem(): HTMLElement {
  return document.querySelector('.gfp-item') as HTMLElement;
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

describe('GoogleFontPicker stacked over Text tool', () => {
  it('open keeps the parent text dialog visible', () => {
    const picker = new GoogleFontPicker(() => {});
    picker.open();
    expect(pickerModal().classList.contains('hidden')).toBe(false);
    expect(parentModal().classList.contains('hidden')).toBe(false);
    picker.destroy();
  });

  it('confirming a font applies it and returns to the still-open text dialog', () => {
    const onSelect = vi.fn();
    const picker = new GoogleFontPicker(onSelect);
    picker.open();
    firstItem().click();
    okButton().click();
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect.mock.calls[0][0]).toBe(firstItem().dataset.family);
    expect(pickerModal().classList.contains('hidden')).toBe(true);
    expect(parentModal().classList.contains('hidden')).toBe(false);
    picker.destroy();
  });

  it('cancel closes only the picker and selects nothing', () => {
    const onSelect = vi.fn();
    const picker = new GoogleFontPicker(onSelect);
    picker.open();
    firstItem().click();
    (document.getElementById('gfp-cancel') as HTMLButtonElement).click();
    expect(onSelect).not.toHaveBeenCalled();
    expect(pickerModal().classList.contains('hidden')).toBe(true);
    expect(parentModal().classList.contains('hidden')).toBe(false);
    picker.destroy();
  });

  it('repeated Use Font clicks apply the font once', () => {
    const onSelect = vi.fn();
    const picker = new GoogleFontPicker(onSelect);
    picker.open();
    firstItem().click();
    okButton().click();
    okButton().click();
    expect(onSelect).toHaveBeenCalledTimes(1);
    picker.destroy();
  });

  it('Enter in the search box does not confirm a stale selection', () => {
    const onSelect = vi.fn();
    const picker = new GoogleFontPicker(onSelect);
    picker.open();
    firstItem().click();
    searchInput().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(onSelect).not.toHaveBeenCalled();
    expect(pickerModal().classList.contains('hidden')).toBe(false);
    expect(parentModal().classList.contains('hidden')).toBe(false);
    picker.destroy();
  });

  it('Escape closes only the picker and selects nothing', () => {
    const onSelect = vi.fn();
    const picker = new GoogleFontPicker(onSelect);
    picker.open();
    firstItem().click();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(onSelect).not.toHaveBeenCalled();
    expect(pickerModal().classList.contains('hidden')).toBe(true);
    expect(parentModal().classList.contains('hidden')).toBe(false);
    picker.destroy();
  });

  it('close hands focus back to the opener', () => {
    const picker = new GoogleFontPicker(() => {});
    const opener = document.getElementById('opener') as HTMLButtonElement;
    opener.focus();
    picker.open();
    expect(document.activeElement).toBe(searchInput());
    (document.getElementById('gfp-cancel') as HTMLButtonElement).click();
    expect(document.activeElement).toBe(opener);
    picker.destroy();
  });
});
