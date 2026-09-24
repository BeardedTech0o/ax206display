using Ax206Display.Config.Services;
using Ax206Display.Engine.Composition;
using Ax206Display.Server.Auth;

namespace Ax206Display.Server;

/// <summary>
/// <c>ax206display set-password</c>: resets the web UI password from a shell,
/// for when it's forgotten. Reads the new password from stdin (typed twice
/// when stdin is a terminal) so it never shows up in shell history or
/// <c>ps</c> output. Run it as the service account so file ownership stays
/// right - the packaged <c>ax206display-passwd</c> wrapper does that.
/// </summary>
internal static class SetPasswordCommand
{
    public static int Run(Ax206DisplayPaths paths)
    {
        var interactive = !Console.IsInputRedirected;

        var password = ReadPassword(interactive ? "New web UI password: " : null);
        if (interactive && ReadPassword("Repeat it: ") != password)
        {
            Console.Error.WriteLine("Passwords don't match; nothing changed.");
            return 1;
        }

        if (password is not { Length: >= WebPasswordStore.MinimumLength })
        {
            Console.Error.WriteLine($"Password must be at least {WebPasswordStore.MinimumLength} characters; nothing changed.");
            return 1;
        }

        SecureDirectory.EnsureExists(paths.DataDirectory);
        new WebPasswordStore(paths.DataDirectory).SetPassword(password);
        Console.WriteLine($"Password updated in {paths.DataDirectory}. Existing browser sessions are signed out.");
        return 0;
    }

    private static string? ReadPassword(string? prompt)
    {
        if (prompt is null)
        {
            return Console.ReadLine();
        }

        Console.Write(prompt);
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
