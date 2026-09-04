// Port of atheriz/menu.py:153-156 prompt timeout loop shared by Menu.Run and MenuRunner.RunMenuAsync
using Atheriz.Core.Objects;

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
            using var cts = new CancellationTokenSource(timeout);
            var promptTask = session.Prompt(display);
            var delayTask = Task.Delay(timeout, cts.Token);
            var done = await Task.WhenAny(promptTask, delayTask).ConfigureAwait(false);
            if (done != promptTask) return null;
            try { cts.Cancel(); } catch { }
            return await promptTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }
    }

    /// <summary>Sync alias for <c>PromptWithTimeoutAsync</c>.</summary>
    public static Task<string?> PromptWithTimeout(Session session, string display, TimeSpan timeout)
        => PromptWithTimeoutAsync(session, display, timeout);
}

/// <summary>Alias per spec: <c>MenuHelper.PromptWithTimeout</c> delegates to <see cref="MenuPrompt"/>.</summary>
public static class MenuHelper
{
    public static Task<string?> PromptWithTimeout(Session session, string display, TimeSpan timeout)
        => MenuPrompt.PromptWithTimeoutAsync(session, display, timeout);
    public static Task<string?> PromptWithTimeoutAsync(Session session, string display, TimeSpan timeout)
        => MenuPrompt.PromptWithTimeoutAsync(session, display, timeout);
}
