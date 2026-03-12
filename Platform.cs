using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

public static class Platform
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Returns true when a graphical (windowed) environment is available.
    /// On Windows this is always true. On Linux/macOS it checks for an X11 or Wayland session.
    /// </summary>
    public static bool HasGraphicalEnvironment
    {
        get
        {
            if (IsWindows) return true;
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        }
    }

    /// <summary>
    /// Launches a terminal emulator as a detached, fire-and-forget process,
    /// opening in <paramref name="workingDirectory"/> (defaults to current directory).
    /// Does nothing when no graphical environment is detected.
    /// </summary>
    public static void OpenTerminal(string? workingDirectory = null)
    {
        if (!HasGraphicalEnvironment) return;

        string cwd = Directory.Exists(workingDirectory) ? workingDirectory! : Directory.GetCurrentDirectory();

        if (IsWindows)
        {
            TryStartProcess("cmd.exe", args: null, useShell: true, cwd);
            return;
        }

        // Linux/macOS: try candidates in priority order.
        var candidates = new List<string>();

        // Honour the user's explicit preference first.
        string? userTerminal = Environment.GetEnvironmentVariable("TERMINAL");
        if (!string.IsNullOrWhiteSpace(userTerminal))
            candidates.Add(userTerminal);

        candidates.AddRange([
            "x-terminal-emulator",  // Debian/Ubuntu alternatives symlink
            "gnome-terminal",
            "konsole",
            "xfce4-terminal",
            "lxterminal",
            "xterm",                // last resort — nearly universal on X11
        ]);

        foreach (var term in candidates)
        {
            if (TryStartProcess(term, args: null, useShell: false, cwd))
                return;
        }
    }

    /// <summary>
    /// Opens <paramref name="filePath"/> with the default system application.
    /// Does nothing when no graphical environment is detected.
    /// </summary>
    public static void OpenFile(string filePath)
    {
        if (!HasGraphicalEnvironment) return;

        if (IsWindows)
        {
            TryStartProcess(filePath, args: null, useShell: true, Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
            return;
        }

        // Linux: xdg-open; macOS: open
        string opener = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
        TryStartProcess(opener, args: $"\"{filePath}\"", useShell: false, Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
    }

    private static bool TryStartProcess(string fileName, string? args, bool useShell, string workingDirectory)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute    = useShell,
                WorkingDirectory   = workingDirectory,
            };
            if (args != null) psi.Arguments = args;
            Process.Start(psi);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }
}
