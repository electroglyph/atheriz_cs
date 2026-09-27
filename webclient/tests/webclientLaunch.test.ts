// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearDrawGrant, launchDraw, readDrawGrant, __resetLaunchThrottleForTests } from '../src/webclient/launch';

describe('draw launch command', () => {
    beforeEach(() => {
        document.body.innerHTML = '<div id="left-terminal"></div>';
        localStorage.clear();
        vi.restoreAllMocks();
        vi.useFakeTimers();
        __resetLaunchThrottleForTests();
    });

    afterEach(() => vi.useRealTimers());

    it('opens the fixed draw route in a new tab', () => {
        vi.setSystemTime(1000);
        const opened = vi.spyOn(window, 'open').mockReturnValue({} as Window);
        expect(launchDraw()).toBe('opened');
        expect(opened).toHaveBeenCalledWith(
            'http://localhost:3000/static/atheriz_draw/',
            '_blank',
            'noopener,noreferrer',
        );
    });

    it('shows a fallback link when the popup is blocked', () => {
        vi.setSystemTime(3000);
        vi.spyOn(window, 'open').mockReturnValue(null);
        expect(launchDraw()).toBe('blocked');
        expect(document.querySelector('a')?.href).toBe('http://localhost:3000/static/atheriz_draw/');
        expect(document.querySelector('.popup-fallback')).not.toBeNull();
    });

    it('stores a grant before opening and keeps it until reader clears', () => {
        vi.setSystemTime(5000);
        const opened = vi.spyOn(window, 'open').mockReturnValue({} as Window);
        const payload = { area: 'TestArea', z: 0, grid: [] };
        expect(launchDraw('secret-key', payload)).toBe('opened');
        expect(readDrawGrant()).toEqual({ key: 'secret-key', payload });
        clearDrawGrant();
        expect(readDrawGrant()).toBeNull();
        expect(opened).toHaveBeenCalledWith(
            'http://localhost:3000/static/atheriz_draw/',
            '_blank',
            'noopener,noreferrer',
        );
    });

    it('keeps the grant when the popup is blocked', () => {
        vi.setSystemTime(7000);
        vi.spyOn(window, 'open').mockReturnValue(null);
        const payload = { area: 'TestArea', z: 0, grid: [] };
        expect(launchDraw('secret-key', payload)).toBe('blocked');
        expect(readDrawGrant()).toEqual({ key: 'secret-key', payload });
        expect(document.querySelector('.popup-fallback')).not.toBeNull();
    });

    it('does not store a grant without a key', () => {
        vi.setSystemTime(9000);
        vi.spyOn(window, 'open').mockReturnValue({} as Window);
        expect(launchDraw()).toBe('opened');
        expect(readDrawGrant()).toBeNull();
    });

    it('throttles launches within one second', () => {
        vi.setSystemTime(11000);
        const opened = vi.spyOn(window, 'open').mockReturnValue({} as Window);
        expect(launchDraw()).toBe('opened');
        vi.setSystemTime(11500);
        expect(launchDraw()).toBe('throttled');
        expect(opened).toHaveBeenCalledTimes(1);
        vi.setSystemTime(12001);
        expect(launchDraw()).toBe('opened');
        expect(opened).toHaveBeenCalledTimes(2);
    });

    it('reports blocked (not throttled) on repeated blocked launches', () => {
        vi.setSystemTime(30000);
        vi.spyOn(window, 'open').mockReturnValue(null);
        expect(launchDraw('k1', { area: 'A' })).toBe('blocked');
        // well past the throttle gate: still blocked, never misread as throttled
        vi.setSystemTime(45000);
        expect(launchDraw('k2', { area: 'A' })).toBe('blocked');
        expect(document.querySelectorAll('.popup-fallback').length).toBe(1);
        // the newest grant is stored; :draw only reopens this stored grant
        // (no fresh data), it is not a retry of the mapedit launch itself
        expect(readDrawGrant()).toEqual({ key: 'k2', payload: { area: 'A' } });
    });

    it('round-trips grants and rejects malformed ones', () => {
        vi.setSystemTime(20000);
        localStorage.setItem('atheriz_draw_grant', JSON.stringify({ key: 'k', payload: { area: 'a' } }));
        localStorage.setItem('atheriz_draw_grant_ts', String(Date.now()));
        expect(readDrawGrant()).toEqual({ key: 'k', payload: { area: 'a' } });
        localStorage.setItem('atheriz_draw_grant', 'not json');
        expect(readDrawGrant()).toBeNull();
        localStorage.setItem('atheriz_draw_grant', JSON.stringify({ key: 'k', payload: [] }));
        localStorage.setItem('atheriz_draw_grant_ts', String(Date.now()));
        expect(readDrawGrant()).toBeNull();
        localStorage.setItem('atheriz_draw_grant', JSON.stringify({ payload: { area: 'a' } }));
        localStorage.setItem('atheriz_draw_grant_ts', String(Date.now()));
        expect(readDrawGrant()).toBeNull();
        localStorage.setItem('atheriz_draw_grant', JSON.stringify({ key: 'k', payload: null }));
        localStorage.setItem('atheriz_draw_grant_ts', String(Date.now()));
        expect(readDrawGrant()).toBeNull();
        // missing timestamp is treated as expired (corrupt lives forever fix)
        localStorage.setItem('atheriz_draw_grant', JSON.stringify({ key: 'k', payload: { area: 'a' } }));
        localStorage.removeItem('atheriz_draw_grant_ts');
        expect(readDrawGrant()).toBeNull();
        clearDrawGrant();
        expect(readDrawGrant()).toBeNull();
    });

    it('expires stale grants after a minute', () => {
        vi.setSystemTime(1000);
        localStorage.setItem('atheriz_draw_grant', JSON.stringify({ key: 'k', payload: { area: 'a' } }));
        localStorage.setItem('atheriz_draw_grant_ts', '100');
        expect(readDrawGrant()).toEqual({ key: 'k', payload: { area: 'a' } });
        vi.setSystemTime(61000);
        expect(readDrawGrant()).toBeNull();
        expect(localStorage.getItem('atheriz_draw_grant')).toBeNull();
    });
});
