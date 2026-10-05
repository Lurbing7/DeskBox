using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DockWidgetContent
{
    private readonly Grid _layout = new() { Margin = new Thickness(8) };
    private readonly StackPanel _commands = new() { Spacing = 4 };
    private bool _verticalLayout;

    private static void ReverseChildren(StackPanel panel)
    {
        var items = panel.Children.Reverse().ToArray();
        panel.Children.Clear();
        foreach (var item in items) panel.Children.Add(item);
    }

    private void ApplyDockOrientation(bool force = false)
    {
        bool vertical = DockAxisLayout.Vertical(Config);
        if (!force && vertical == _verticalLayout) return;
        if (vertical != _verticalLayout)
            foreach (var panel in new[] { _row, _attentionRow, _runningRow, _attentionArea, _runningArea, _commands }) ReverseChildren(panel);
        _verticalLayout = vertical;
        _layout.ColumnDefinitions.Clear(); _layout.RowDefinitions.Clear();
        var order = vertical
            ? new FrameworkElement[] { _commands, _runningArea, _scroll, _attentionArea }
            : new FrameworkElement[] { _attentionArea, _scroll, _runningArea, _commands };
        for (int i = 0; i < order.Length; i++)
        {
            var length = ReferenceEquals(order[i], _scroll) ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            if (vertical) _layout.RowDefinitions.Add(new() { Height = length });
            else _layout.ColumnDefinitions.Add(new() { Width = length });
            Grid.SetColumn(order[i], vertical ? 0 : i); Grid.SetRow(order[i], vertical ? i : 0);
        }
        _layout.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        _layout.VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        foreach (var panel in new[] { _row, _attentionRow, _runningRow, _attentionArea, _runningArea, _commands })
            panel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        foreach (var divider in new[] { _attentionDivider, _runningDivider })
        {
            divider.Width = vertical ? 28 : 1; divider.Height = vertical ? 1 : 28;
            divider.Margin = vertical ? new Thickness(0, 8, 0, 8) : new Thickness(8, 0, 8, 0);
            divider.HorizontalAlignment = HorizontalAlignment.Center; divider.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    private void ReverseNewRowsForVertical()
    {
        if (_verticalLayout) ReverseChildren(_row);
    }

    private IEnumerable<Button> CanonicalRootButtons() => _verticalLayout
        ? _row.Children.OfType<Button>().Reverse() : _row.Children.OfType<Button>();

    private void OrderRuntimeRows()
    {
        SetOrder(_attentionRow, _attentionEntries.Values.Select(item => item.Button));
        SetOrder(_runningRow, _runningButtons.Values);
        void SetOrder(StackPanel panel, IEnumerable<Button> canonical)
        {
            var desired = (_verticalLayout ? canonical.Reverse() : canonical).ToArray();
            if (panel.Children.SequenceEqual(desired)) return;
            panel.Children.Clear(); foreach (var button in desired) panel.Children.Add(button);
        }
    }
}
