using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace DeskBox.Controls.WidgetContents;

internal sealed class SystemMonitorMetricIcon : Canvas
{
    private readonly string _kind;
    private readonly Path _pie = new();
    private readonly Ellipse _ring = new() { Width = 18, Height = 18, StrokeThickness = 1, Opacity = .35 };
    private readonly Rectangle _stem = new() { Width = 3, Height = 10, RadiusX = 1.5, RadiusY = 1.5 };
    private readonly List<Shape> _colored = [];
    public SystemMonitorMetricIcon(string kind)
    {
        _kind = kind; Width = 20; Height = 20; IsHitTestVisible = false;
        if (kind == "Load") { Children.Add(_ring); SetLeft(_ring, 1); SetTop(_ring, 1); Children.Add(_pie); }
        else if (kind == "Temperature")
        {
            Add(new Rectangle { Width = 7, Height = 14, RadiusX = 3.5, RadiusY = 3.5, StrokeThickness = 1, Fill = null }, 6, 1);
            Add(new Ellipse { Width = 9, Height = 9 }, 5, 10);
            Add(_stem, 8, 4);
        }
        else if (kind == "Fan")
        {
            for (int i = 0; i < 4; i++)
            {
                var blade = new Polygon { Points = new PointCollection { new(10, 10), new(10, 1), new(17, 4) }, RenderTransform = new RotateTransform { Angle = i * 90, CenterX = 10, CenterY = 10 } };
                Add(blade, 0, 0);
            }
            Add(new Ellipse { Width = 4, Height = 4 }, 8, 8);
        }
        else if (kind == "Frequency")
        {
            foreach (var point in new[] { new Point(2, 7), new Point(11, 2), new Point(11, 12) })
                Add(new Polygon { Points = new PointCollection { new(0, 3), new(2, 0), new(6, 0), new(8, 3), new(6, 6), new(2, 6) } }, point.X, point.Y);
        }
        else
        {
            var figure = new PathFigure { StartPoint = new(10, 0), IsClosed = true };
            figure.Segments.Add(new BezierSegment { Point1 = new(12, 7), Point2 = new(19, 10), Point3 = new(17, 15) });
            figure.Segments.Add(new BezierSegment { Point1 = new(14, 22), Point2 = new(2, 19), Point3 = new(4, 12) });
            figure.Segments.Add(new BezierSegment { Point1 = new(5, 8), Point2 = new(8, 7), Point3 = new(10, 0) });
            Add(new Path { Data = new PathGeometry { Figures = new PathFigureCollection { figure } } }, 0, 0);
        }
        Update(null);
    }
    private void Add(Shape shape, double x, double y) { SetLeft(shape, x); SetTop(shape, y); Children.Add(shape); _colored.Add(shape); }
    public void Update(double? value, double warning = 90, double critical = 100)
    {
        bool available = value is { } number && double.IsFinite(number) && number >= 0;
        Windows.UI.Color color = !available ? Windows.UI.Color.FromArgb(255, 130, 130, 130) :
            _kind == "Temperature" && value >= critical ? Windows.UI.Color.FromArgb(255, 215, 52, 56) :
            _kind == "Temperature" && value >= warning ? Windows.UI.Color.FromArgb(255, 211, 132, 0) :
            _kind == "Load" ? Windows.UI.Color.FromArgb(255, 0, 120, 212) : Windows.UI.Color.FromArgb(255, 24, 164, 154);
        var brush = new SolidColorBrush(color);
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            brush = (SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        foreach (var shape in _colored) { shape.Fill = _kind == "Temperature" && shape is Rectangle && shape != _stem ? null : brush; shape.Stroke = brush; shape.Opacity = available ? 1 : .45; }
        if (_kind == "Temperature") { _stem.Height = available ? Math.Clamp(value!.Value / 120, 0, 1) * 10 : 0; SetTop(_stem, 14 - _stem.Height); }
        if (_kind != "Load") return;
        _ring.Stroke = brush; _pie.Fill = brush;
        double fraction = available ? Math.Clamp(value!.Value / 100, 0, 1) : 0;
        if (fraction <= 0) { _pie.Data = null; return; }
        if (fraction >= 1) { _pie.Data = new EllipseGeometry { Center = new(10, 10), RadiusX = 9, RadiusY = 9 }; return; }
        double angle = fraction * Math.PI * 2;
        var pie = new PathFigure { StartPoint = new(10, 10), IsClosed = true };
        pie.Segments.Add(new LineSegment { Point = new(10, 1) });
        pie.Segments.Add(new ArcSegment { Point = new(10 + Math.Sin(angle) * 9, 10 - Math.Cos(angle) * 9), Size = new Size(9, 9), SweepDirection = SweepDirection.Clockwise, IsLargeArc = fraction > .5 });
        _pie.Data = new PathGeometry { Figures = new PathFigureCollection { pie } };
    }
}
