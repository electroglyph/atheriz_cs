namespace Atheriz.Server.Cli;
public static class ResetHandler
{
    public static Task HandleResetAsync(string[] a) => StopHandler.HandleResetAsync(a);
}
