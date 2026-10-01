export function shouldNavigateHistory(
    _key: 'ArrowUp' | 'ArrowDown',
    value: string,
    selectionStart: number | null,
    selectionEnd: number | null,
    navigating: boolean,
): boolean {
    if (value === '' || navigating) return true;
    const fullSelection = selectionStart === 0 && selectionEnd === value.length;
    const atStart = selectionStart === 0 && selectionEnd === 0;
    return fullSelection || atStart;
}

export function inputHeight(scrollHeight: number, minimum = 30, maximum = 300): number {
    return Math.min(Math.max(scrollHeight, minimum), maximum);
}

export function shouldClearSubmittedInput(
    key: string,
    submitted: boolean,
    controlKey: boolean,
    altKey: boolean,
    metaKey: boolean,
): boolean {
    return submitted && key.length === 1 && !controlKey && !altKey && !metaKey;
}

export function submissionFeedback(sent: boolean): string | null {
    return sent ? null : '\r\nNot connected to server.\r\n';
}

// Global typing focus: plain typing anywhere outside an editable should
// land in the command input. Only single printable keys redirect:
// shortcuts (copy/paste), navigation keys, and modifier chords stay put
// so terminal selection and other terminal keys keep working. Already-
// focused editables keep their focus — except xterm.js terminals, which
// capture keys in their own off-screen helper textarea: typing there
// must still reach the command input.
export function shouldFocusInputOnKeydown(
    key: string,
    target: EventTarget | null,
    input: HTMLElement,
    controlKey: boolean,
    altKey: boolean,
    metaKey: boolean,
): boolean {
    if (key.length !== 1 || controlKey || altKey || metaKey) return false;
    if (target === input) return false;
    if (typeof HTMLElement !== 'undefined' && target instanceof HTMLElement) {
        if (target.closest('.xterm')) return true;
        if (target.isContentEditable) return false;
        const tag = target.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return false;
    }
    return true;
}
