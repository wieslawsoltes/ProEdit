using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ProEdit.Documents;

namespace ProEdit.Word.Avalonia;

public sealed class RulerCornerControl : Control
{
    public static readonly StyledProperty<Color> SurfaceColorProperty =
        AvaloniaProperty.Register<RulerCornerControl, Color>(nameof(SurfaceColor), new Color(255, 238, 241, 245));

    public static readonly StyledProperty<TabAlignment> SelectedAlignmentProperty =
        AvaloniaProperty.Register<RulerCornerControl, TabAlignment>(nameof(SelectedAlignment), TabAlignment.Left);

    private static readonly Color BorderColor = new Color(255, 208, 212, 219);
    private static readonly Color MarkerColor = new Color(255, 58, 110, 165);
    private static readonly Pen BorderPen = new Pen(new SolidColorBrush(BorderColor), 1);
    private static readonly Pen MarkerPen = new Pen(new SolidColorBrush(MarkerColor), 1);
    private static readonly IBrush MarkerBrush = new SolidColorBrush(MarkerColor);

    private IBrush _surfaceBrush = new SolidColorBrush(new Color(255, 238, 241, 245));

    public Color SurfaceColor
    {
        get => GetValue(SurfaceColorProperty);
        set => SetValue(SurfaceColorProperty, value);
    }

    public TabAlignment SelectedAlignment
    {
        get => GetValue(SelectedAlignmentProperty);
        set => SetValue(SelectedAlignmentProperty, value);
    }

    public event EventHandler<TabAlignment>? SelectedAlignmentChanged;

    public RulerCornerControl()
    {
        ClipToBounds = true;
        UpdateSurfaceBrush(SurfaceColor);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SurfaceColorProperty)
        {
            UpdateSurfaceBrush(change.GetNewValue<Color>());
        }

        if (change.Property == SelectedAlignmentProperty)
        {
            SelectedAlignmentChanged?.Invoke(this, SelectedAlignment);
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        SelectedAlignment = NextTabAlignment(SelectedAlignment);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(_surfaceBrush, Bounds);

        var centerX = Bounds.Width / 2f;
        var centerY = Bounds.Height / 2f;
        DrawTabMarker(context, centerX, centerY, SelectedAlignment);

        context.DrawLine(BorderPen, new Point(Bounds.Width - 0.5f, 0), new Point(Bounds.Width - 0.5f, Bounds.Height));
        context.DrawLine(BorderPen, new Point(0, Bounds.Height - 0.5f), new Point(Bounds.Width, Bounds.Height - 0.5f));
    }

    private void UpdateSurfaceBrush(Color color)
    {
        _surfaceBrush = new SolidColorBrush(color);
        InvalidateVisual();
    }

    private void DrawTabMarker(DrawingContext context, double x, double y, TabAlignment alignment)
    {
        var top = y - 6f;
        var bottom = y + 6f;
        var left = x - 6f;
        var right = x + 6f;

        switch (alignment)
        {
            case TabAlignment.Right:
                context.DrawLine(MarkerPen, new Point(right, top), new Point(right, bottom));
                context.DrawLine(MarkerPen, new Point(right, bottom), new Point(x, bottom));
                break;
            case TabAlignment.Center:
                context.DrawLine(MarkerPen, new Point(x, top), new Point(x, bottom));
                context.DrawLine(MarkerPen, new Point(left, bottom), new Point(right, bottom));
                break;
            case TabAlignment.Decimal:
                context.DrawLine(MarkerPen, new Point(x, top), new Point(x, bottom));
                context.DrawEllipse(MarkerBrush, null, new Point(x + 3f, bottom - 1f), 1f, 1f);
                break;
            default:
                context.DrawLine(MarkerPen, new Point(left, top), new Point(left, bottom));
                context.DrawLine(MarkerPen, new Point(left, bottom), new Point(x, bottom));
                break;
        }
    }

    private static TabAlignment NextTabAlignment(TabAlignment alignment)
    {
        return alignment switch
        {
            TabAlignment.Left => TabAlignment.Center,
            TabAlignment.Center => TabAlignment.Right,
            TabAlignment.Right => TabAlignment.Decimal,
            _ => TabAlignment.Left
        };
    }
}
