using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DeskBox.Controls;

internal static class ScreenPositionSelector
{
    public static Grid Create(int selected, bool includeCenter, LocalizationService localization, Action<int> changed)
    {
        var grid = new Grid { Width = 156, ColumnSpacing = 4, RowSpacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 0; i < 3; i++) { grid.RowDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); }
        string[] keys = ["TopLeft", "Top", "TopRight", "Left", "Center", "Right", "BottomLeft", "Bottom", "BottomRight"];
        for (int i = 1; i <= 9; i++)
        {
            if (i == 5 && !includeCenter) continue;
            int position = i;
            var button = new ToggleButton { Width = 48, Height = 32, Padding = new Thickness(0), IsChecked = selected == i };
            string name = localization.T("Position." + keys[i - 1]);
            AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
            Grid.SetRow(button, (i - 1) / 3); Grid.SetColumn(button, (i - 1) % 3);
            button.Click += (_, _) =>
            {
                foreach (var other in grid.Children.OfType<ToggleButton>()) other.IsChecked = ReferenceEquals(other, button);
                changed(position);
            };
            grid.Children.Add(button);
        }
        return grid;
    }
}
