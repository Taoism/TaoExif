using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using TaoExif.Core;

namespace TaoExif.Controls;

/// <summary>A marker drawn on the map (a photo with GPS).</summary>
public readonly record struct MapMarker(double Latitude, double Longitude, string Label, bool Highlight);

/// <summary>A small slippy map (OpenStreetMap tiles, Web Mercator) drawn natively, so it works on every platform.</summary>
public sealed class MapControl : Control
{
    private const int TileSize = 256;
    public const int MinZoom = 1;
    public const int MaxZoom = 19;

    private static readonly IBrush DarkBackground = new SolidColorBrush(Color.Parse("#FF15161A"));
    private static readonly IBrush DarkTileGap = new SolidColorBrush(Color.Parse("#FF1E2024"));
    private static readonly IBrush LightBackground = new SolidColorBrush(Color.Parse("#FFE4E6EA"));
    private static readonly IBrush LightTileGap = new SolidColorBrush(Color.Parse("#FFEDEFF2"));
    private static readonly IBrush MarkerBrush = new SolidColorBrush(Color.Parse("#FFE0A040"));
    private static readonly IBrush MarkerHighlightBrush = new SolidColorBrush(Color.Parse("#FFFF5A4A"));
    private static readonly IPen MarkerPen = new Pen(new SolidColorBrush(Color.Parse("#FF15161A")), 1.5);
    private static readonly IBrush PinBrush = new SolidColorBrush(Color.Parse("#FF3D7FD6"));
    private static readonly IPen PinPen = new Pen(Brushes.White, 2);
    private static readonly IPen TrackPen = new Pen(new SolidColorBrush(Color.Parse("#E05AC8FA")), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IBrush LabelBack = new SolidColorBrush(Color.Parse("#CC15161A"));
    private static readonly IBrush LabelFore = new SolidColorBrush(Color.Parse("#FFE8E8EA"));
    private static readonly Typeface LabelFont = new("Inter");
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#30000000"));

    private double _centerX = 0.5; // world coordinates 0..1
    private double _centerY = 0.5;
    private int _zoom = 4;
    private Point? _pressPoint;
    private Point _lastPoint;
    private bool _dragging;

    private IReadOnlyList<MapMarker> _markers = [];
    private IReadOnlyList<TrackPoint> _track = [];
    private GeoPoint? _pin;

    public MapControl()
    {
        ClipToBounds = true;
        Focusable = true;
        TileCache.Shared.TileLoaded += OnTileLoaded;
    }

    /// <summary>Raised when the user clicks (without dragging) a point on the map.</summary>
    public event EventHandler<GeoPoint>? MapClicked;

    /// <summary>Raised after panning or zooming.</summary>
    public event EventHandler? ViewChanged;

    public int Zoom => _zoom;
    public double CenterLatitude => YToLat(_centerY);
    public double CenterLongitude => XToLon(_centerX);

    public GeoPoint? Pin
    {
        get => _pin;
        set
        {
            _pin = value;
            InvalidateVisual();
        }
    }

    public void SetMarkers(IReadOnlyList<MapMarker> markers)
    {
        _markers = markers;
        InvalidateVisual();
    }

    public void SetTrack(IReadOnlyList<TrackPoint> track)
    {
        _track = track;
        InvalidateVisual();
    }

    public void CenterOn(double lat, double lon, int? zoom = null)
    {
        _centerX = LonToX(lon);
        _centerY = LatToY(lat);
        if (zoom is { } z)
            _zoom = Math.Clamp(z, MinZoom, MaxZoom);
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Zooms and centres so all points are visible.</summary>
    public void FitTo(IEnumerable<(double Lat, double Lon)> points)
    {
        var list = points.ToList();
        if (list.Count == 0)
            return;
        double minX = list.Min(p => LonToX(p.Lon)), maxX = list.Max(p => LonToX(p.Lon));
        double minY = list.Min(p => LatToY(p.Lat)), maxY = list.Max(p => LatToY(p.Lat));
        _centerX = (minX + maxX) / 2;
        _centerY = (minY + maxY) / 2;
        double w = Math.Max(Bounds.Width, 200) * 0.8, h = Math.Max(Bounds.Height, 200) * 0.8;
        int zoom = 16;
        while (zoom > MinZoom && ((maxX - minX) * WorldSize(zoom) > w || (maxY - minY) * WorldSize(zoom) > h))
            zoom--;
        _zoom = zoom;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomBy(int delta) => ZoomAround(new Point(Bounds.Width / 2, Bounds.Height / 2), delta);

    private void OnTileLoaded(object? sender, EventArgs e) => Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        TileCache.Shared.TileLoaded -= OnTileLoaded;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TileCache.Shared.TileLoaded -= OnTileLoaded;
        TileCache.Shared.TileLoaded += OnTileLoaded;
    }

    // ---------- projection ----------

    private static double WorldSize(int zoom) => TileSize * Math.Pow(2, zoom);
    private static double LonToX(double lon) => (lon + 180.0) / 360.0;
    private static double XToLon(double x) => x * 360.0 - 180.0;

    private static double LatToY(double lat)
    {
        lat = Math.Clamp(lat, -85.05112878, 85.05112878);
        double r = lat * Math.PI / 180.0;
        return (1 - Math.Log(Math.Tan(r) + 1 / Math.Cos(r)) / Math.PI) / 2;
    }

    private static double YToLat(double y) => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y))) * 180.0 / Math.PI;

    private Point WorldToScreen(double wx, double wy)
    {
        double ws = WorldSize(_zoom);
        return new Point((wx - _centerX) * ws + Bounds.Width / 2, (wy - _centerY) * ws + Bounds.Height / 2);
    }

    private (double X, double Y) ScreenToWorld(Point p)
    {
        double ws = WorldSize(_zoom);
        return (_centerX + (p.X - Bounds.Width / 2) / ws, _centerY + (p.Y - Bounds.Height / 2) / ws);
    }

    private Point GeoToScreen(double lat, double lon) => WorldToScreen(LonToX(lon), LatToY(lat));

    // ---------- rendering ----------

    private bool IsDark => ActualThemeVariant == ThemeVariant.Dark;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(IsDark ? DarkBackground : LightBackground, bounds);
        DrawTiles(context);
        DrawTrack(context);
        DrawMarkers(context);
        DrawPin(context);
        DrawAttribution(context, bounds);
    }

    private void DrawTiles(DrawingContext context)
    {
        int n = 1 << _zoom;
        double ws = WorldSize(_zoom);
        double left = _centerX * ws - Bounds.Width / 2;
        double top = _centerY * ws - Bounds.Height / 2;
        int tx0 = (int)Math.Floor(left / TileSize), ty0 = (int)Math.Floor(top / TileSize);
        int tx1 = (int)Math.Floor((left + Bounds.Width) / TileSize), ty1 = (int)Math.Floor((top + Bounds.Height) / TileSize);

        for (int ty = ty0; ty <= ty1; ty++)
        {
            if (ty < 0 || ty >= n)
                continue;
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int wrapped = ((tx % n) + n) % n;
                var dest = new Rect(tx * TileSize - left, ty * TileSize - top, TileSize, TileSize);
                var bmp = TileCache.Shared.Get(_zoom, wrapped, ty);
                if (bmp != null)
                {
                    context.DrawImage(bmp, new Rect(bmp.Size), dest);
                }
                else
                {
                    context.FillRectangle(IsDark ? DarkTileGap : LightTileGap, dest.Deflate(0.5));
                }
            }
        }
        // in dark mode a light dim keeps the bright OSM tiles closer to the UI
        if (IsDark)
            context.FillRectangle(DimBrush, new Rect(Bounds.Size));
    }

    private void DrawTrack(DrawingContext context)
    {
        if (_track.Count < 2)
            return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(GeoToScreen(_track[0].Latitude, _track[0].Longitude), false);
            foreach (var p in _track.Skip(1))
                g.LineTo(GeoToScreen(p.Latitude, p.Longitude));
            g.EndFigure(false);
        }
        context.DrawGeometry(null, TrackPen, geo);
    }

    private void DrawMarkers(DrawingContext context)
    {
        foreach (var m in _markers.OrderBy(m => m.Highlight))
        {
            var p = GeoToScreen(m.Latitude, m.Longitude);
            if (p.X < -20 || p.Y < -20 || p.X > Bounds.Width + 20 || p.Y > Bounds.Height + 20)
                continue;
            double r = m.Highlight ? 7 : 5;
            context.DrawEllipse(m.Highlight ? MarkerHighlightBrush : MarkerBrush, MarkerPen, p, r, r);
            if (m.Highlight && !string.IsNullOrEmpty(m.Label))
                DrawLabel(context, m.Label, new Point(p.X + 10, p.Y - 9));
        }
    }

    private void DrawPin(DrawingContext context)
    {
        if (_pin is not { } pin)
            return;
        var p = GeoToScreen(pin.Latitude, pin.Longitude);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(p, true);
            g.LineTo(new Point(p.X - 9, p.Y - 16));
            g.ArcTo(new Point(p.X + 9, p.Y - 16), new Size(9.5, 9.5), 0, true, SweepDirection.Clockwise);
            g.LineTo(p);
            g.EndFigure(true);
        }
        context.DrawGeometry(PinBrush, PinPen, geo);
        context.DrawEllipse(Brushes.White, null, new Point(p.X, p.Y - 18), 3.2, 3.2);
    }

    private static void DrawLabel(DrawingContext context, string text, Point at)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFont, 11.5, LabelFore);
        var rect = new Rect(at.X - 4, at.Y - 2, ft.Width + 8, ft.Height + 4);
        context.FillRectangle(LabelBack, rect, 3);
        context.DrawText(ft, at);
    }

    private static void DrawAttribution(DrawingContext context, Rect bounds)
    {
        var ft = new FormattedText("© OpenStreetMap contributors", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFont, 10.5, LabelFore);
        var at = new Point(bounds.Width - ft.Width - 8, bounds.Height - ft.Height - 4);
        context.FillRectangle(LabelBack, new Rect(at.X - 4, at.Y - 2, ft.Width + 8, ft.Height + 4), 3);
        context.DrawText(ft, at);
    }

    // ---------- input ----------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _pressPoint = e.GetPosition(this);
        _lastPoint = _pressPoint.Value;
        _dragging = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressPoint is not { } press)
            return;
        var p = e.GetPosition(this);
        if (!_dragging && (Math.Abs(p.X - press.X) > 4 || Math.Abs(p.Y - press.Y) > 4))
            _dragging = true;
        if (_dragging)
        {
            double ws = WorldSize(_zoom);
            _centerX -= (p.X - _lastPoint.X) / ws;
            _centerY = Math.Clamp(_centerY - (p.Y - _lastPoint.Y) / ws, 0, 1);
            _centerX -= Math.Floor(_centerX);
            _lastPoint = p;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressPoint == null)
            return;
        var p = e.GetPosition(this);
        if (_dragging)
        {
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            var (wx, wy) = ScreenToWorld(p);
            wx -= Math.Floor(wx);
            MapClicked?.Invoke(this, new GeoPoint(YToLat(Math.Clamp(wy, 0, 1)), XToLon(wx)));
        }
        _pressPoint = null;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAround(e.GetPosition(this), e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus:
                ZoomBy(1);
                e.Handled = true;
                break;
            case Key.Subtract or Key.OemMinus:
                ZoomBy(-1);
                e.Handled = true;
                break;
        }
    }

    private void ZoomAround(Point screen, int delta)
    {
        int newZoom = Math.Clamp(_zoom + delta, MinZoom, MaxZoom);
        if (newZoom == _zoom)
            return;
        var (wx, wy) = ScreenToWorld(screen);
        _zoom = newZoom;
        double ws = WorldSize(_zoom);
        _centerX = wx - (screen.X - Bounds.Width / 2) / ws;
        _centerY = Math.Clamp(wy - (screen.Y - Bounds.Height / 2) / ws, 0, 1);
        _centerX -= Math.Floor(_centerX);
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
