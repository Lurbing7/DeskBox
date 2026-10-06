namespace DeskBox.Services;

internal static class DockApplicationIdentity
{
    internal static bool Matches(string? executable, string target)
    {
        if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(target)) return false;
        if (string.Equals(executable, target, StringComparison.OrdinalIgnoreCase)) return true;
        if (!Path.IsPathFullyQualified(executable) || !Path.IsPathFullyQualified(target) ||
            !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            string processPath = Path.GetFullPath(executable);
            string shortcutPath = Path.GetFullPath(target);
            // Some launchers (including Doubao) start a same-named EXE in their
            // immediate app directory. Keep the install directory in the identity.
            return string.Equals(processPath, shortcutPath, StringComparison.OrdinalIgnoreCase) ||
                IsLauncherPair(processPath, shortcutPath) || IsLauncherPair(shortcutPath, processPath);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }

    private static bool IsLauncherPair(string application, string launcher)
    {
        string? appDirectory = Path.GetDirectoryName(application);
        if (appDirectory is null || !Path.GetFileName(appDirectory).Equals("app", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(Path.GetDirectoryName(appDirectory), Path.GetDirectoryName(launcher), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(application), Path.GetFileName(launcher), StringComparison.OrdinalIgnoreCase);
    }
}
