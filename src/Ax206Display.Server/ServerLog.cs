namespace Ax206Display.Server;

internal static partial class ServerLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Web UI password is in {Path}. Sign in with it, then change it under Settings (that deletes the file).")]
    public static partial void InitialPasswordAvailable(ILogger logger, string path);
}
