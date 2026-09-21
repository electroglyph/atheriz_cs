// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { SelectionTool } from '../src/tools/SelectionTool';

describe('SelectionTool does not accumulate window listeners', () => {
  it('registers one Escape handler per instance and removes it on destroy()', () => {
    const addSpy = vi.spyOn(window, 'addEventListener');
    const removeSpy = vi.spyOn(window, 'removeEventListener');

    // Per-instance handlers: each live tool owns exactly one keydown
    // registration (the old shared-global handler is gone).
    const first = new SelectionTool();
    const second = new SelectionTool();

    const keydownRegistrations = addSpy.mock.calls.filter(
      (call) => call[0] === 'keydown',
    );

    // Proper behavior: one handler per live instance, each removed by
    // destroy() — never a handler per instance that is never removed.
    expect(keydownRegistrations.length).toBe(2);

    first.destroy();
    second.destroy();

    const keydownRemovals = removeSpy.mock.calls.filter(
      (call) => call[0] === 'keydown',
    );
    expect(keydownRemovals.length).toBe(2);
    for (const [, fn] of keydownRegistrations) {
      expect(removeSpy).toHaveBeenCalledWith('keydown', fn);
    }
  });

  it('stops reacting to Escape after destroy()', () => {
    const tool = new SelectionTool();
    tool.destroy();

    // No handler left behind: dispatching Escape must not throw or act.
    expect(() =>
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })),
    ).not.toThrow();
  });
});
