namespace DeskBox.Services;

internal static class DockManualOrder
{
    // The delimiter is not legal in Windows file names; store only root file names.
    public static string[] Arrange(IEnumerable<string> names, string? saved)
    {
        var remaining = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string name in (saved ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
            if (remaining.Remove(name)) result.Add(name);
        result.AddRange(remaining.Order(StringComparer.CurrentCultureIgnoreCase));
        return result.ToArray();
    }

    public static string Insert(IEnumerable<string> current, IEnumerable<string> incoming, int index)
    {
        var order = current.ToList();
        var inserted = incoming.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        index = Math.Clamp(index, 0, order.Count);
        // Index belongs to the row before removing the moving entries.
        index -= order.Take(index).Count(n => inserted.Contains(n, StringComparer.OrdinalIgnoreCase));
        order.RemoveAll(n => inserted.Contains(n, StringComparer.OrdinalIgnoreCase));
        order.InsertRange(Math.Clamp(index, 0, order.Count), inserted);
        return string.Join('|', order);
    }
}
