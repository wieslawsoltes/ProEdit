using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ProEdit.Reporting.Avalonia.Designer;

/// <summary>
/// Resize handle used to adjust designer drawer widths.
/// </summary>
public sealed class ReportDesignerDrawerResizeHandle : Border
{
    /// <summary>
    /// Defines the <see cref="TargetWidth"/> property.
    /// </summary>
    public static readonly DirectProperty<ReportDesignerDrawerResizeHandle, double> TargetWidthProperty =
        AvaloniaProperty.RegisterDirect<ReportDesignerDrawerResizeHandle, double>(
            nameof(TargetWidth),
            handle => handle.TargetWidth,
            (handle, value) => handle.TargetWidth = value);

    /// <summary>
    /// Defines the <see cref="MinimumTargetWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MinimumTargetWidthProperty =
        AvaloniaProperty.Register<ReportDesignerDrawerResizeHandle, double>(
            nameof(MinimumTargetWidth),
            defaultValue: 260d);

    /// <summary>
    /// Defines the <see cref="MaximumTargetWidth"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MaximumTargetWidthProperty =
        AvaloniaProperty.Register<ReportDesignerDrawerResizeHandle, double>(
            nameof(MaximumTargetWidth),
            defaultValue: 640d);

    /// <summary>
    /// Defines the <see cref="InvertDelta"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> InvertDeltaProperty =
        AvaloniaProperty.Register<ReportDesignerDrawerResizeHandle, bool>(
            nameof(InvertDelta),
            defaultValue: false);

    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartWidth;
    private double _targetWidth = 336d;

    /// <summary>
    /// Gets or sets the current drawer width target.
    /// </summary>
    public double TargetWidth
    {
        get => _targetWidth;
        set
        {
            var clamped = Math.Clamp(value, MinimumTargetWidth, MaximumTargetWidth);
            SetAndRaise(TargetWidthProperty, ref _targetWidth, clamped);
        }
    }

    /// <summary>
    /// Gets or sets the minimum drawer width.
    /// </summary>
    public double MinimumTargetWidth
    {
        get => GetValue(MinimumTargetWidthProperty);
        set => SetValue(MinimumTargetWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum drawer width.
    /// </summary>
    public double MaximumTargetWidth
    {
        get => GetValue(MaximumTargetWidthProperty);
        set => SetValue(MaximumTargetWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether horizontal drag delta should be inverted.
    /// </summary>
    public bool InvertDelta
    {
        get => GetValue(InvertDeltaProperty);
        set => SetValue(InvertDeltaProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDragging = true;
        _dragStartPoint = e.GetPosition(this);
        _dragStartWidth = TargetWidth;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isDragging)
        {
            return;
        }

        var position = e.GetPosition(this);
        var deltaX = position.X - _dragStartPoint.X;
        if (InvertDelta)
        {
            deltaX = -deltaX;
        }

        TargetWidth = _dragStartWidth + deltaX;
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag(e);
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    private void EndDrag(PointerEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}
