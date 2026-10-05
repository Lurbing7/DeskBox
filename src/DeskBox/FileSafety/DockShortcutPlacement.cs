using DeskBox.Helpers;

namespace DeskBox.FileSafety;

internal static class DockShortcutPlacement
{
    private static readonly object Gate = new();

    private static IEnumerable<string> OwnedDirectories(string root)
    {
        yield return Path.GetFullPath(root);
        foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                foreach (string child in OwnedDirectories(directory)) yield return child;
    }

    private static string? Identity(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        var metadata = ShortcutHelper.ReadStoredMetadata(path);
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.TargetPath)) return null;
        // Different launch arguments may intentionally represent different profiles.
        return metadata.TargetPath.ToUpperInvariant() + "\n" + metadata.Arguments;
    }

    internal static string BackupDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskBox", "dock-shortcut-backups");
    internal static string GetBackupDirectory(string root) => string.Equals(Path.GetPathRoot(Path.GetFullPath(root)), Path.GetPathRoot(BackupDirectory), StringComparison.OrdinalIgnoreCase)
        ? BackupDirectory : Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + ".removed-backups";

    private static string Archive(string path, string? backupDirectory = null)
    {
        backupDirectory ??= BackupDirectory;
        Directory.CreateDirectory(backupDirectory);
        string backup = Path.Combine(backupDirectory, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path));
        if (Directory.Exists(path)) Directory.Move(path, backup);
        else File.Move(path, backup);
        return backup;
    }

    public static string Remove(string root, string path)
    {
        lock (Gate)
        {
            if (!IsOwned(root, path)) throw new InvalidOperationException("Only owned Dock entries can be removed.");
            // Keep directory moves on one volume so archiving is atomic and lossless.
            string backup = Archive(path, GetBackupDirectory(root));
            PruneEmptyDirectories(root, [Path.GetDirectoryName(path)!]);
            return backup;
        }
    }

    public static void Normalize(string root)
    {
        lock (Gate)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            // Root entries take precedence over copies inside owned classification folders.
            foreach (string directory in OwnedDirectories(root))
                foreach (string path in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
                {
                    string? identity = Identity(path);
                    if (identity is not null && !seen.Add(identity)) Archive(path);
                }
        }
    }

    public static bool IsOwned(string root, string path) => OwnedDirectories(root).Any(directory =>
        string.Equals(Path.GetFullPath(directory), Path.GetFullPath(Path.GetDirectoryName(path)!), StringComparison.OrdinalIgnoreCase));

    public static string Place(string dockRoot, string destinationDirectory, string source)
    {
        lock (Gate)
        {
            string? identity = Identity(source);
            var directories = OwnedDirectories(dockRoot).ToArray();
            bool owned = directories.Any(directory => string.Equals(Path.GetFullPath(directory), Path.GetFullPath(destinationDirectory), StringComparison.OrdinalIgnoreCase));
            // External folder links remain copy-only; never remove their contents.
            string? existing = identity is null ? null : Directory.EnumerateFiles(destinationDirectory).FirstOrDefault(path => Identity(path) == identity);
            string destination = existing ?? Path.Combine(destinationDirectory, Path.GetFileName(source));
            if (existing is null && !string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(destination))
                    destination = Path.Combine(destinationDirectory, Path.GetFileNameWithoutExtension(source) + "-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(source));
                File.Copy(source, destination, overwrite: false);
            }
            if (owned && identity is null && IsOwned(dockRoot, source) && !string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                Archive(source);
            if (!owned || identity is null || Identity(destination) != identity) return destination;
            foreach (string directory in directories)
                foreach (string path in Directory.EnumerateFiles(directory).ToArray())
                    if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase) && Identity(path) == identity)
                        Archive(path);
            return destination;
        }
    }

    public static string MoveDirectory(string dockRoot, string destinationDirectory, string source)
    {
        lock (Gate)
        {
            string fullSource = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
            string fullDestination = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (!IsOwned(dockRoot, source) || !OwnedDirectories(dockRoot).Contains(fullDestination, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only owned Dock folders can be moved.");
            if (fullDestination.Equals(fullSource, StringComparison.OrdinalIgnoreCase) || fullDestination.StartsWith(fullSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot move a folder into itself.");
            string destination = Path.Combine(fullDestination, Path.GetFileName(fullSource));
            if (destination.Equals(fullSource, StringComparison.OrdinalIgnoreCase)) return source;
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Destination already exists.");
            Directory.Move(fullSource, destination);
            return destination;
        }
    }

    public static void PruneEmptyDirectories(string dockRoot, IEnumerable<string> sourceDirectories)
    {
        lock (Gate)
        {
            string root = Path.GetFullPath(dockRoot).TrimEnd(Path.DirectorySeparatorChar);
            var owned = OwnedDirectories(root).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string source in sourceDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string? current = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
                while (current is not null && !current.Equals(root, StringComparison.OrdinalIgnoreCase) && owned.Contains(current))
                {
                    try
                    {
                        if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any()) break;
                        // Never recurse: newly created or hidden contents prevent removal.
                        Directory.Delete(current, recursive: false);
                        App.Log("[Dock] Removed moved-empty owned folder");
                    }
                    catch (IOException) { break; }
                    catch (UnauthorizedAccessException) { break; }
                    current = Path.GetDirectoryName(current);
                }
            }
        }
    }
}
