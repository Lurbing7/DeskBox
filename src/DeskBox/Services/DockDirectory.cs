using DeskBox.Helpers;

namespace DeskBox.Services;

internal sealed record DockEntry(string Path, string Name, string Target, bool IsFolder);
internal static class DockDirectory
{
    public static IReadOnlyList<DockEntry> Read(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var result = new List<DockEntry>();
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            bool folder = Directory.Exists(path);
            string extension = Path.GetExtension(path);
            if (!folder && !new[] { ".lnk", ".exe", ".url" }.Contains(extension, StringComparer.OrdinalIgnoreCase)) continue;
            string target = path;
            if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                target = ShortcutHelper.ReadStoredMetadata(path)?.TargetPath ?? path;
            result.Add(new(path, folder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path), target, folder || Directory.Exists(target)));
            if (result.Count >= 200) break;
        }
        return result.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
