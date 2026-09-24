
namespace Atheriz.Core;

/// <summary>
/// Shared helper for <c>MenuEngine.RunAsync</c> to avoid duplicating the
/// timeout pattern at every prompt site.
/// Mirrors <c>menu.py:153</c> <c>session.Prompt(display)</c> with timeout → null.
/// </summary>
public static class MenuPrompt
{
    /// <summary>
    /// Prompt with timeout — mirrors <c>menu.py:153</c> <c>session.Prompt(display)</c> with timeout.
    /// Returns prompt result or <c>null</c> on timeout/cancel/failure.
    /// </summary>
    public static async Task<string?> PromptWithTimeoutAsync(Session session, string display, TimeSpan timeout)
    {
        // Atomic capture: the token owns exactly this prompt, so the timeout
        // path cancels it even if a newer prompt has since taken the slot.
        var (promptTask, token) = session.PromptWithToken(display);
        try
        {
            return await promptTask.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            try { session.CancelPrompt(token); } catch { }
            return null;
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }
    }

    /// <summary>Sync alias for <c>PromptWithTimeoutAsync</c>. Kept: external
    /// game code and the parity suite call this spelling.</summary>
    public static Task<string?> PromptWithTimeout(Session session, string display, TimeSpan timeout)
        => PromptWithTimeoutAsync(session, display, timeout);
}
