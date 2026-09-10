// Port of atheriz/menu.py:153-156 prompt timeout loop shared by Menu.Run and MenuRunner.RunMenuAsync

namespace Atheriz.Core;

/// <summary>
/// Port of <c>atheriz/menu.py:153-156</c> prompt-with-timeout loop.
/// Shared helper for <c>Menu.Run</c> and <c>MenuRunner.RunMenuAsync</c> to avoid duplication.
/// Mirrors <c>using var cts=new CancellationTokenSource(Timeout); var t=session.Prompt(display);
/// await Task.WhenAny(t, Task.Delay(Timeout, cts.Token))</c> with timeout → null.
/// </summary>
public static class MenuPrompt
{
    /// <summary>
    /// Prompt with timeout — mirrors <c>menu.py:153</c> <c>session.Prompt(display)</c> + <c>Task.WhenAny</c> timeout.
    /// Returns prompt result or <c>null</c> on timeout/cancel/failure.
    /// </summary>
    public static async Task<string?> PromptWithTimeoutAsync(Session session, string display, TimeSpan timeout)
    {
        try
        {
            // Single timer: the delay below is the only armed clock (the old
            // CancellationTokenSource(timeout) armed a second one). The
            // source is cancelled on the success path to release the delay.
            using var cts = new CancellationTokenSource();
            var promptTask = session.Prompt(display);
            // Capture the live future so the timeout path can cancel it (no orphaned
            // InputFuture). Prompt's synchronous prefix runs to completion on call, so the
            // field is already set. Mirrors asyncio.wait_for cancelling the prompt coroutine.
            var pending = session.InputFuture;
            var delayTask = Task.Delay(timeout, cts.Token);
            var done = await Task.WhenAny(promptTask, delayTask).ConfigureAwait(false);
            if (done != promptTask) { try { session.CancelPrompt(pending); } catch { } return null; }
            try { await cts.CancelAsync().ConfigureAwait(false); } catch { }
            return await promptTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }
    }

    /// <summary>Sync alias for <c>PromptWithTimeoutAsync</c>. Kept: external
    /// game code and the parity suite call this spelling.</summary>
    public static Task<string?> PromptWithTimeout(Session session, string display, TimeSpan timeout)
        => PromptWithTimeoutAsync(session, display, timeout);
}
