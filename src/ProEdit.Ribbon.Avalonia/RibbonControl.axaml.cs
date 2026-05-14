using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using ProEdit.Ribbon;
using ProEdit.Primitives;

namespace ProEdit.Ribbon.Avalonia;

public partial class RibbonControl : UserControl
{
    private readonly Dictionary<string, Control> _keyTipTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ComboBox> _comboBoxUserInput = new();
    private readonly Dictionary<string, Vector> _tabScrollOffsets = new(StringComparer.OrdinalIgnoreCase);
    private Canvas? _keyTipOverlay;
    private bool _keyTipsVisible;
    private string _keyTipBuffer = string.Empty;
    private ScrollViewer? _groupScrollViewer;
    private bool _isUpdatingGroupLayout;
    private RibbonTab? _lastLayoutTab;
    private RibbonTab? _lastScrollTab;
    private RibbonModel? _activeModel;
    private readonly RibbonGroupOverflowController _overflowController = new();
    private int _stateUpdateDepth;
    private int _suppressGallerySelectionDepth;
    private bool _isRestoringScrollOffset;
    private const double RibbonHorizontalScrollStep = 48d;

    public event EventHandler? CustomizeQuickAccessRequested;

    public static readonly StyledProperty<RibbonModel?> ModelProperty =
        AvaloniaProperty.Register<RibbonControl, RibbonModel?>(nameof(Model));

    public static readonly StyledProperty<double> OverflowCollapseThresholdProperty =
        AvaloniaProperty.Register<RibbonControl, double>(nameof(OverflowCollapseThreshold), 6d);

    public static readonly StyledProperty<double> OverflowExpandThresholdProperty =
        AvaloniaProperty.Register<RibbonControl, double>(nameof(OverflowExpandThreshold), 32d);

    public RibbonModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public double OverflowCollapseThreshold
    {
        get => GetValue(OverflowCollapseThresholdProperty);
        set => SetValue(OverflowCollapseThresholdProperty, value);
    }

    public double OverflowExpandThreshold
    {
        get => GetValue(OverflowExpandThresholdProperty);
        set => SetValue(OverflowExpandThresholdProperty, value);
    }

    public Control? FocusReturnTarget { get; set; }

    public RibbonControl()
    {
        InitializeComponent();
        ModelProperty.Changed.AddClassHandler<RibbonControl>((control, _) => control.OnModelChanged());
        _keyTipOverlay = this.FindControl<Canvas>("KeyTipOverlay");
        _groupScrollViewer = this.FindControl<ScrollViewer>("RibbonGroupsScrollViewer");
        if (_groupScrollViewer is not null)
        {
            _groupScrollViewer.ScrollChanged += OnGroupScrollChanged;
            _groupScrollViewer.PointerWheelChanged += OnGroupScrollWheelChanged;
        }
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnModelChanged()
    {
        using var _ = BeginStateUpdate();

        if (_activeModel is not null)
        {
            _activeModel.PropertyChanged -= OnModelPropertyChanged;
        }

        _activeModel = Model;
        if (_activeModel is not null)
        {
            _activeModel.PropertyChanged += OnModelPropertyChanged;
        }

        DataContext = Model;
        if (Model is { SelectedTab: null, Tabs.Count: > 0 })
        {
            Model.SelectedTab = Model.Tabs[0];
        }

        SuppressGallerySelection();
        Model?.RefreshState();

        _lastScrollTab = Model?.SelectedTab;
        RestoreScrollForSelectedTab();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RibbonModel.SelectedTab))
        {
            OnSelectedTabChanged();
        }
    }

    private void OnSelectedTabChanged()
    {
        if (_groupScrollViewer is null)
        {
            return;
        }

        if (_lastScrollTab is not null)
        {
            _tabScrollOffsets[_lastScrollTab.Id] = _groupScrollViewer.Offset;
        }

        _lastScrollTab = Model?.SelectedTab;
        RestoreScrollForSelectedTab();
    }

    private void OnGroupScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isRestoringScrollOffset || _groupScrollViewer is null)
        {
            return;
        }

        var tab = Model?.SelectedTab;
        if (tab is null)
        {
            return;
        }

        _tabScrollOffsets[tab.Id] = _groupScrollViewer.Offset;
    }

    private void RestoreScrollForSelectedTab()
    {
        if (_groupScrollViewer is null)
        {
            return;
        }

        var tab = Model?.SelectedTab;
        if (tab is null)
        {
            return;
        }

        var target = _tabScrollOffsets.TryGetValue(tab.Id, out var offset)
            ? offset
            : default;

        _isRestoringScrollOffset = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_groupScrollViewer is not null)
            {
                _groupScrollViewer.Offset = new Vector(target.X, 0);
            }

            _isRestoringScrollOffset = false;
        }, DispatcherPriority.Background);
    }

    private async void OnRibbonButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RibbonButton button })
        {
            await button.ExecuteAsync();
            RefreshState();
            RestoreFocusAfterAction();
        }
    }

    private async void OnRibbonToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { DataContext: RibbonToggleButton toggle } button)
        {
            await toggle.ToggleAsync(button.IsChecked ?? false);
            RefreshState();
            RestoreFocusAfterAction();
        }
    }

    private async void OnRibbonSplitPrimaryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RibbonSplitButton split })
        {
            await split.ExecutePrimaryAsync();
            RefreshState();
            RestoreFocusAfterAction();
        }
    }

    private void OnRibbonSplitMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RibbonSplitButton split })
        {
            return;
        }

        var menu = BuildMenu(split.Menu);
        menu.Open(sender as Control);
    }

    private async void OnRibbonSplitTogglePrimaryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { DataContext: RibbonSplitToggleButton split } button)
        {
            await split.ToggleAsync(button.IsChecked ?? false);
            RefreshState();
            RestoreFocusAfterAction();
        }
    }

    private void OnRibbonSplitToggleMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RibbonSplitToggleButton split })
        {
            return;
        }

        var menu = BuildMenu(split.Menu);
        menu.Open(sender as Control);
    }

    private void OnCollapsedGroupPopupClosed(object? sender, EventArgs e)
    {
        if (sender is Popup { PlacementTarget: ToggleButton toggle })
        {
            toggle.IsChecked = false;
        }
    }

    private void OnRibbonDropdownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RibbonDropdownButton dropdown })
        {
            return;
        }

        var menu = BuildMenu(dropdown.Menu);
        menu.Open(sender as Control);
    }

    private async void OnRibbonComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: RibbonComboBox combo })
        {
            return;
        }

        if (_stateUpdateDepth > 0)
        {
            return;
        }

        var comboBox = (ComboBox)sender;
        if (!ShouldHandleComboBoxSelection(comboBox))
        {
            return;
        }

        await combo.SelectAsync(combo.SelectedItem as RibbonComboBoxItem);
        RefreshState();
        RestoreFocusAfterAction();
    }

    private void OnRibbonComboBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            _comboBoxUserInput.Add(comboBox);
        }
    }

    private void OnRibbonComboBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            _comboBoxUserInput.Add(comboBox);
        }
    }

    private async void OnRibbonComboBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: RibbonComboBox combo })
        {
            return;
        }

        if (_stateUpdateDepth > 0)
        {
            return;
        }

        var comboBox = (ComboBox)sender;
        await combo.UpdateTextAsync(comboBox.Text);
        RefreshState();
        RestoreFocusAfterAction();
    }

    private async void OnRibbonSpinnerIncreaseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RibbonSpinner spinner })
        {
            return;
        }

        await spinner.IncreaseAsync();
        RefreshState();
        RestoreFocusAfterAction();
    }

    private async void OnRibbonSpinnerDecreaseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RibbonSpinner spinner })
        {
            return;
        }

        await spinner.DecreaseAsync();
        RefreshState();
        RestoreFocusAfterAction();
    }

    private async void OnRibbonTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
        {
            return;
        }

        if (sender is not TextBox { DataContext: RibbonTextBox textBox })
        {
            return;
        }

        await textBox.SubmitAsync();
        RefreshState();
        RestoreFocusAfterAction();
        e.Handled = true;
    }

    private async void OnRibbonSpinnerLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: RibbonSpinner spinner } textBox)
        {
            return;
        }

        if (!spinner.IsEditable)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(textBox.Text))
        {
            await spinner.SetValueAsync(null);
            RefreshState();
            RestoreFocusAfterAction();
            return;
        }

        if (double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            await spinner.SetValueAsync(value);
            RefreshState();
            RestoreFocusAfterAction();
        }
    }

    private async void OnRibbonGallerySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { DataContext: RibbonGallery gallery } listBox)
        {
            return;
        }

        if (_stateUpdateDepth > 0 || _suppressGallerySelectionDepth > 0)
        {
            return;
        }

        var selected = listBox.SelectedItem as RibbonGalleryItem;
        if (selected is null)
        {
            return;
        }

        await gallery.SelectAsync(selected);
        RefreshState();
        RestoreFocusAfterAction();
    }

    private async void OnRibbonGalleryPopupSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.DataContext is not RibbonGallery gallery)
        {
            return;
        }

        if (_stateUpdateDepth > 0 || _suppressGallerySelectionDepth > 0)
        {
            return;
        }

        var selected = listBox.SelectedItem as RibbonGalleryItem;
        if (selected is null)
        {
            return;
        }

        await gallery.SelectAsync(selected);
        if (listBox.Tag is ToggleButton toggle)
        {
            toggle.IsChecked = false;
        }

        RefreshState();
        RestoreFocusAfterAction();
    }

    private async void OnRibbonGalleryPopupMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RibbonMenuItem item } button)
        {
            return;
        }

        if (_stateUpdateDepth > 0)
        {
            return;
        }

        await item.ExecuteAsync();
        CloseGalleryPopup(button);
        RefreshState();
        RestoreFocusAfterAction();
    }

    private static void CloseGalleryPopup(Control control)
    {
        var popup = control.GetVisualAncestors().OfType<Popup>().FirstOrDefault();
        if (popup?.PlacementTarget is ToggleButton toggle)
        {
            toggle.IsChecked = false;
        }
    }

    private async void OnRibbonColorSplitPrimaryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RibbonColorSplitButton button })
        {
            await button.ExecutePrimaryAsync();
            RefreshState();
            RestoreFocusAfterAction();
        }
    }

    private void OnRibbonColorSplitMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RibbonColorSplitButton button })
        {
            return;
        }

        var menu = BuildColorMenu(button.Palette, button);
        menu.Open(sender as Control);
    }

    private ContextMenu BuildMenu(RibbonMenu menu)
    {
        var contextMenu = new ContextMenu();
        foreach (var entry in menu.Items)
        {
            switch (entry)
            {
                case RibbonMenuItem item when item.IsVisible:
                    var menuItem = new MenuItem
                    {
                        Header = item.Label,
                        IsEnabled = item.IsEnabled,
                        DataContext = item
                    };
                    ToolTip.SetTip(menuItem, item.ToolTipContent);
                    var iconText = RibbonIconResolver.ResolveText(item.IconKey);
                    if (!string.IsNullOrWhiteSpace(iconText))
                    {
                        menuItem.Icon = new TextBlock
                        {
                            Text = iconText,
                            FontFamily = "Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji",
                            FontSize = 14,
                            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                        };
                    }
                    menuItem.Click += OnRibbonMenuItemClick;
                    contextMenu.Items.Add(menuItem);
                    break;
                case RibbonMenuToggleItem toggle when toggle.IsVisible:
                    var toggleItem = new MenuItem
                    {
                        Header = toggle.Label,
                        IsEnabled = toggle.IsEnabled,
                        DataContext = toggle,
                        ToggleType = MenuItemToggleType.CheckBox,
                        IsChecked = toggle.IsChecked
                    };
                    ToolTip.SetTip(toggleItem, toggle.ToolTipContent);
                    var toggleIcon = RibbonIconResolver.ResolveText(toggle.IconKey);
                    if (!string.IsNullOrWhiteSpace(toggleIcon))
                    {
                        toggleItem.Icon = new TextBlock
                        {
                            Text = toggleIcon,
                            FontFamily = "Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji",
                            FontSize = 14,
                            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                        };
                    }
                    toggleItem.Click += OnRibbonMenuItemClick;
                    contextMenu.Items.Add(toggleItem);
                    break;
                case RibbonMenuSeparator separator when separator.IsVisible:
                    contextMenu.Items.Add(new Separator());
                    break;
            }
        }

        return contextMenu;
    }

    private ContextMenu BuildColorMenu(IReadOnlyList<RibbonColorItem> palette, object owner)
    {
        var contextMenu = new ContextMenu();
        foreach (var color in palette)
        {
            if (!color.IsVisible)
            {
                continue;
            }

            var menuItem = new MenuItem
            {
                Header = BuildColorMenuHeader(color),
                IsEnabled = color.IsEnabled,
                DataContext = color,
                Tag = owner
            };
            menuItem.Click += OnRibbonColorMenuItemClick;
            contextMenu.Items.Add(menuItem);
        }

        return contextMenu;
    }

    private static Control BuildColorMenuHeader(RibbonColorItem color)
    {
        var iconText = RibbonIconResolver.ResolveText(color.IconKey);
        Control preview;
        if (!string.IsNullOrWhiteSpace(iconText))
        {
            preview = new TextBlock
            {
                Text = iconText,
                FontFamily = "Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji",
                FontSize = 14,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };
        }
        else
        {
            preview = new Border
            {
                Width = 14,
                Height = 14,
                Background = RibbonColorBrushConverter.ResolveBrush(color),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };
        }

        return new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                preview,
                new TextBlock
                {
                    Text = color.Label,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                }
            }
        };
    }

    private async void OnRibbonColorMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: RibbonColorItem color, Tag: object owner })
        {
            return;
        }

        if (color.Kind == RibbonColorKind.Picker)
        {
            var picked = await ShowColorPickerAsync(owner);
            if (!picked.HasValue)
            {
                return;
            }

            color = new RibbonColorItem(
                $"{color.Id}-custom",
                "Custom",
                RibbonColorKind.Custom,
                picked.Value);
        }

        await ApplyColorSelectionAsync(owner, color);
    }

    private async ValueTask ApplyColorSelectionAsync(object owner, RibbonColorItem color)
    {
        switch (owner)
        {
            case RibbonColorSplitButton split:
                await split.SelectColorAsync(color);
                RefreshState();
                break;
            case RibbonColorButton button:
                await button.SelectColorAsync(color);
                RefreshState();
                break;
        }
    }

    private async ValueTask<DocColor?> ShowColorPickerAsync(object owner)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null)
        {
            return null;
        }

        var initial = ResolveCurrentColor(owner);
        var dialog = new RibbonColorPickerDialog(initial);
        return await dialog.ShowDialog<DocColor?>(window);
    }

    private static DocColor? ResolveCurrentColor(object owner)
    {
        return owner switch
        {
            RibbonColorSplitButton split => split.SelectedColor?.Color,
            RibbonColorButton button => button.SelectedColor?.Color,
            _ => null
        };
    }

    private async void OnRibbonMenuItemClick(object? sender, RoutedEventArgs e)
    {
        switch (sender)
        {
            case MenuItem { DataContext: RibbonMenuItem item }:
                await item.ExecuteAsync();
                RefreshState();
                RestoreFocusAfterAction();
                break;
            case MenuItem { DataContext: RibbonMenuToggleItem toggle }:
                await toggle.ExecuteAsync();
                RefreshState();
                RestoreFocusAfterAction();
                break;
        }
    }

    public void RefreshState()
    {
        using var _ = BeginStateUpdate();
        SuppressGallerySelection();
        Model?.RefreshState();
    }

    public IDisposable BeginStateUpdateScope()
    {
        return BeginStateUpdate();
    }

    private bool ShouldHandleComboBoxSelection(ComboBox comboBox)
    {
        if (_comboBoxUserInput.Remove(comboBox))
        {
            return true;
        }

        return comboBox.IsDropDownOpen;
    }

    private IDisposable BeginStateUpdate()
    {
        _stateUpdateDepth++;
        return new StateUpdateScope(this);
    }

    private void SuppressGallerySelection()
    {
        _suppressGallerySelectionDepth++;
        Dispatcher.UIThread.Post(
            () => _suppressGallerySelectionDepth = Math.Max(0, _suppressGallerySelectionDepth - 1),
            DispatcherPriority.Background);
    }

    private void RestoreFocusAfterAction()
    {
        var target = FocusReturnTarget;
        if (target is null)
        {
            return;
        }

        if (!target.IsEffectivelyVisible || !target.Focusable)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => target.Focus(), DispatcherPriority.Background);
    }

    private void OnGroupScrollWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_groupScrollViewer is null)
        {
            return;
        }

        if (_groupScrollViewer.Extent.Width <= _groupScrollViewer.Viewport.Width)
        {
            return;
        }

        var delta = e.Delta;
        if (delta.X == 0 && delta.Y == 0)
        {
            return;
        }

        var horizontalDelta = Math.Abs(delta.X) > 0 ? delta.X : -delta.Y;
        if (horizontalDelta == 0)
        {
            return;
        }

        var maxOffset = Math.Max(0, _groupScrollViewer.Extent.Width - _groupScrollViewer.Viewport.Width);
        var nextX = _groupScrollViewer.Offset.X - horizontalDelta * RibbonHorizontalScrollStep;
        nextX = Math.Clamp(nextX, 0, maxOffset);
        if (Math.Abs(nextX - _groupScrollViewer.Offset.X) < 0.01)
        {
            return;
        }

        _groupScrollViewer.Offset = new Vector(nextX, 0);
        e.Handled = true;
    }

    private sealed class StateUpdateScope : IDisposable
    {
        private RibbonControl? _owner;

        public StateUpdateScope(RibbonControl owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_owner is { } owner)
            {
                owner._stateUpdateDepth = Math.Max(0, owner._stateUpdateDepth - 1);
                _owner = null;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt)
        {
            ToggleKeyTips();
            e.Handled = true;
            return;
        }

        if (!_keyTipsVisible && TryHandleRovingFocus(e))
        {
            e.Handled = true;
            return;
        }

        if (!_keyTipsVisible)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            HideKeyTips();
            e.Handled = true;
            return;
        }

        if (TryGetKeyTipCharacter(e.Key, out var keyTipChar))
        {
            _keyTipBuffer += char.ToUpperInvariant(keyTipChar);
            var matches = _keyTipTargets.Keys
                .Where(key => key.StartsWith(_keyTipBuffer, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length == 0)
            {
                _keyTipBuffer = string.Empty;
            }
            else
            {
                var exactMatches = matches.Where(key => key.Length == _keyTipBuffer.Length).ToArray();
                if (exactMatches.Length == 1 && matches.Length == exactMatches.Length)
                {
                    ActivateKeyTip(exactMatches[0]);
                    HideKeyTips();
                }
                else if (exactMatches.Length == 1 && matches.Length == 1)
                {
                    ActivateKeyTip(exactMatches[0]);
                    HideKeyTips();
                }
            }

            e.Handled = true;
        }
    }

    private bool TryHandleRovingFocus(KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return false;
        }

        var direction = e.Key switch
        {
            Key.Left => RovingDirection.Left,
            Key.Right => RovingDirection.Right,
            Key.Up => RovingDirection.Up,
            Key.Down => RovingDirection.Down,
            Key.Home => RovingDirection.Home,
            Key.End => RovingDirection.End,
            _ => RovingDirection.None
        };

        if (direction == RovingDirection.None)
        {
            return false;
        }

        var focused = GetFocusedControl();
        if (focused is null || !focused.Classes.Contains("ribbon-control"))
        {
            return false;
        }

        if (!IsDescendantOfRibbon(focused) || IsTextEditing(focused))
        {
            return false;
        }

        var targets = GetRovingFocusTargets();
        if (targets.Count == 0)
        {
            return false;
        }

        var index = targets.FindIndex(target => ReferenceEquals(target.Control, focused));
        if (index < 0)
        {
            return false;
        }

        var current = targets[index];
        var next = direction switch
        {
            RovingDirection.Home => targets[0],
            RovingDirection.End => targets[^1],
            _ => FindDirectionalTarget(targets, current, direction)
        };

        if (next.Control is null || ReferenceEquals(next.Control, current.Control))
        {
            return false;
        }

        return next.Control.Focus();
    }

    private Control? GetFocusedControl()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        return topLevel?.FocusManager?.GetFocusedElement() as Control;
    }

    private bool IsDescendantOfRibbon(Control control)
    {
        return control.GetVisualAncestors().Contains(this);
    }

    private static bool IsTextEditing(Control control)
    {
        if (control is TextBox { IsReadOnly: false })
        {
            return true;
        }

        var combo = control.GetVisualAncestors().OfType<ComboBox>().FirstOrDefault();
        if (combo is { IsEditable: true })
        {
            return true;
        }

        return combo?.IsDropDownOpen == true;
    }

    private List<FocusTarget> GetRovingFocusTargets()
    {
        var targets = new List<FocusTarget>();
        foreach (var control in this.GetVisualDescendants().OfType<Control>())
        {
            if (!control.Classes.Contains("ribbon-control"))
            {
                continue;
            }

            if (control.Classes.Contains("ribbon-split-arrow") || control.Classes.Contains("ribbon-spinner-arrow"))
            {
                continue;
            }

            if (!control.IsVisible || !control.IsEnabled)
            {
                continue;
            }

            if (!TryGetControlRect(control, out var bounds))
            {
                continue;
            }

            targets.Add(new FocusTarget(control, bounds));
        }

        targets.Sort(static (left, right) =>
        {
            var top = left.Bounds.Y.CompareTo(right.Bounds.Y);
            return top != 0 ? top : left.Bounds.X.CompareTo(right.Bounds.X);
        });

        return targets;
    }

    private bool TryGetControlRect(Control control, out Rect rect)
    {
        var origin = control.TranslatePoint(new Point(0, 0), this);
        if (origin is null)
        {
            rect = default;
            return false;
        }

        rect = new Rect(origin.Value, control.Bounds.Size);
        return rect.Width > 0 && rect.Height > 0;
    }

    private static FocusTarget FindDirectionalTarget(
        List<FocusTarget> targets,
        FocusTarget current,
        RovingDirection direction)
    {
        var center = current.Center;
        var rowTolerance = Math.Max(6, current.Bounds.Height * 0.6);
        var columnTolerance = Math.Max(6, current.Bounds.Width * 0.6);
        var best = current;
        var bestDistance = double.PositiveInfinity;

        foreach (var target in targets)
        {
            if (ReferenceEquals(target.Control, current.Control))
            {
                continue;
            }

            var deltaX = target.Center.X - center.X;
            var deltaY = target.Center.Y - center.Y;

            switch (direction)
            {
                case RovingDirection.Left:
                    if (deltaX >= -1 || Math.Abs(deltaY) > rowTolerance)
                    {
                        continue;
                    }

                    break;
                case RovingDirection.Right:
                    if (deltaX <= 1 || Math.Abs(deltaY) > rowTolerance)
                    {
                        continue;
                    }

                    break;
                case RovingDirection.Up:
                    if (deltaY >= -1 || Math.Abs(deltaX) > columnTolerance)
                    {
                        continue;
                    }

                    break;
                case RovingDirection.Down:
                    if (deltaY <= 1 || Math.Abs(deltaX) > columnTolerance)
                    {
                        continue;
                    }

                    break;
            }

            var distance = direction is RovingDirection.Left or RovingDirection.Right
                ? Math.Abs(deltaX) + Math.Abs(deltaY) * 0.1
                : Math.Abs(deltaY) + Math.Abs(deltaX) * 0.1;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = target;
            }
        }

        if (!ReferenceEquals(best.Control, current.Control))
        {
            return best;
        }

        var index = targets.FindIndex(target => ReferenceEquals(target.Control, current.Control));
        if (index < 0)
        {
            return current;
        }

        return direction switch
        {
            RovingDirection.Left or RovingDirection.Up => index > 0 ? targets[index - 1] : current,
            RovingDirection.Right or RovingDirection.Down => index < targets.Count - 1 ? targets[index + 1] : current,
            _ => current
        };
    }

    private enum RovingDirection
    {
        None,
        Left,
        Right,
        Up,
        Down,
        Home,
        End
    }

    private readonly record struct FocusTarget(Control Control, Rect Bounds)
    {
        public Point Center => new(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_keyTipsVisible && (e.Key == Key.LeftAlt || e.Key == Key.RightAlt))
        {
            e.Handled = true;
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_keyTipsVisible)
        {
            BuildKeyTips();
        }

        UpdateGroupLayout();
    }

    private void UpdateGroupLayout()
    {
        if (_isUpdatingGroupLayout || _groupScrollViewer is null || Model?.SelectedTab is null)
        {
            return;
        }

        var viewportWidth = _groupScrollViewer.Viewport.Width;
        if (viewportWidth <= 0 || double.IsInfinity(viewportWidth))
        {
            return;
        }

        _isUpdatingGroupLayout = true;
        try
        {
            var tab = Model.SelectedTab;
            if (!ReferenceEquals(tab, _lastLayoutTab))
            {
                foreach (var group in tab.Groups)
                {
                    group.ResetLayoutMode();
                }

                _lastLayoutTab = tab;
            }

            var extentWidth = _groupScrollViewer.Extent.Width;
            var (collapseThreshold, expandThreshold) = ResolveOverflowThresholds(
                OverflowCollapseThreshold,
                OverflowExpandThreshold,
                viewportWidth);
            _overflowController.CollapseThreshold = collapseThreshold;
            _overflowController.ExpandThreshold = expandThreshold;
            _overflowController.UpdateLayout(tab.Groups, viewportWidth, extentWidth);
        }
        finally
        {
            _isUpdatingGroupLayout = false;
        }
    }

    private static (double CollapseThreshold, double ExpandThreshold) ResolveOverflowThresholds(
        double collapseBase,
        double expandBase,
        double viewportWidth)
    {
        var collapseThreshold = Math.Max(0, collapseBase);
        var expandThreshold = Math.Max(0, expandBase);
        var scale = Math.Clamp(viewportWidth / 720d, 0.6d, 1d);

        collapseThreshold = Math.Max(2d, collapseThreshold * scale);
        expandThreshold = Math.Max(16d, expandThreshold * scale);

        if (expandThreshold < collapseThreshold + 4d)
        {
            expandThreshold = collapseThreshold + 4d;
        }

        return (collapseThreshold, expandThreshold);
    }

    private void ToggleKeyTips()
    {
        if (_keyTipsVisible)
        {
            HideKeyTips();
        }
        else
        {
            ShowKeyTips();
        }
    }

    private void ShowKeyTips()
    {
        _keyTipsVisible = true;
        _keyTipBuffer = string.Empty;
        if (_keyTipOverlay is not null)
        {
            _keyTipOverlay.IsVisible = true;
        }

        Focus();
        BuildKeyTips();
    }

    private void HideKeyTips()
    {
        _keyTipsVisible = false;
        _keyTipBuffer = string.Empty;
        _keyTipTargets.Clear();
        if (_keyTipOverlay is not null)
        {
            _keyTipOverlay.Children.Clear();
            _keyTipOverlay.IsVisible = false;
        }
    }

    private void BuildKeyTips()
    {
        if (_keyTipOverlay is null)
        {
            return;
        }

        _keyTipOverlay.Children.Clear();
        _keyTipTargets.Clear();

        foreach (var (keyTip, control, anchor) in EnumerateKeyTipTargets())
        {
            if (_keyTipTargets.ContainsKey(keyTip))
            {
                continue;
            }

            var keyTipBorder = new Border
            {
                Classes = { "ribbon-keytip" },
                Child = new TextBlock
                {
                    Classes = { "ribbon-keytip-text" },
                    Text = keyTip
                }
            };

            var left = Math.Max(0, anchor.X + control.Bounds.Width - 18);
            var top = Math.Max(0, anchor.Y - 8);
            Canvas.SetLeft(keyTipBorder, left);
            Canvas.SetTop(keyTipBorder, top);
            _keyTipOverlay.Children.Add(keyTipBorder);
            _keyTipTargets[keyTip] = control;
        }
    }

    private IEnumerable<(string KeyTip, Control Control, Point Anchor)> EnumerateKeyTipTargets()
    {
        foreach (var control in this.GetVisualDescendants().OfType<Control>())
        {
            if (!control.IsVisible)
            {
                continue;
            }

            if (control is Border border && border.Classes.Contains("ribbon-group-header"))
            {
                if (border.DataContext is RibbonGroup group && !group.IsCollapsed && !string.IsNullOrWhiteSpace(group.KeyTip))
                {
                    var origin = border.TranslatePoint(new Point(0, 0), this);
                    if (origin is not null)
                    {
                        yield return (group.KeyTip!, border, origin.Value);
                    }
                }

                continue;
            }

            if (control.Classes.Contains("ribbon-gallery-dropdown"))
            {
                continue;
            }

            if (control.Classes.Contains("ribbon-split-arrow"))
            {
                continue;
            }

            if (control is TabItem tabItem)
            {
                if (tabItem.DataContext is RibbonTab tab && !string.IsNullOrWhiteSpace(tab.KeyTip))
                {
                    var tabOrigin = tabItem.TranslatePoint(new Point(0, 0), this);
                    if (tabOrigin is null)
                    {
                        continue;
                    }

                    yield return (tab.KeyTip!, tabItem, tabOrigin.Value);
                }

                continue;
            }

            if (control is not Button
                && control is not ToggleButton
                && control is not ComboBox
                && control is not TextBox
                && control is not ListBox)
            {
                continue;
            }

            if (control.DataContext is RibbonGroupLauncher launcher && !string.IsNullOrWhiteSpace(launcher.KeyTip))
            {
                var launcherOrigin = control.TranslatePoint(new Point(0, 0), this);
                if (launcherOrigin is null)
                {
                    continue;
                }

                yield return (launcher.KeyTip!, control, launcherOrigin.Value);
                continue;
            }

            if (control.DataContext is not IRibbonControl ribbonControl)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(ribbonControl.KeyTip))
            {
                continue;
            }

            var controlOrigin = control.TranslatePoint(new Point(0, 0), this);
            if (controlOrigin is null)
            {
                continue;
            }

            yield return (ribbonControl.KeyTip!, control, controlOrigin.Value);
        }
    }

    private void ActivateKeyTip(string keyTip)
    {
        if (!_keyTipTargets.TryGetValue(keyTip, out var control))
        {
            return;
        }

        if (control is TabItem tabItem)
        {
            tabItem.IsSelected = true;
            return;
        }

        if (control.DataContext is RibbonGroup)
        {
            if (TryFocusFirstGroupControl(control))
            {
                return;
            }
        }

        if (control is Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        else if (control is ToggleButton toggle)
        {
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private bool TryFocusFirstGroupControl(Control control)
    {
        var groupContainer = control.GetVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(ancestor => ancestor.Classes.Contains("ribbon-group"));
        if (groupContainer is null)
        {
            return false;
        }

        foreach (var target in groupContainer.GetVisualDescendants().OfType<Control>())
        {
            if (!target.Classes.Contains("ribbon-control"))
            {
                continue;
            }

            if (target.Classes.Contains("ribbon-split-arrow") || target.Classes.Contains("ribbon-spinner-arrow"))
            {
                continue;
            }

            if (!target.IsVisible || !target.IsEnabled)
            {
                continue;
            }

            return target.Focus();
        }

        return false;
    }

    private static bool TryGetKeyTipCharacter(Key key, out char keyTipChar)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            keyTipChar = (char)('A' + (key - Key.A));
            return true;
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            keyTipChar = (char)('0' + (key - Key.D0));
            return true;
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            keyTipChar = (char)('0' + (key - Key.NumPad0));
            return true;
        }

        keyTipChar = default;
        return false;
    }

    private void OnRibbonControlContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Model is null || sender is not Control { DataContext: IRibbonControl control } anchor)
        {
            return;
        }

        var menu = BuildQuickAccessMenu(control);
        menu.Open(anchor);
        e.Handled = true;
    }

    private ContextMenu BuildQuickAccessMenu(IRibbonControl control)
    {
        var inQuickAccess = Model?.ContainsQuickAccess(control.Id) ?? false;
        var menuItem = new MenuItem
        {
            Header = inQuickAccess
                ? "Remove from Quick Access Toolbar"
                : "Add to Quick Access Toolbar",
            DataContext = control
        };
        menuItem.Click += OnQuickAccessMenuClick;

        var menu = new ContextMenu();
        menu.Items.Add(menuItem);
        menu.Items.Add(new Separator());
        var customizeItem = new MenuItem
        {
            Header = "Customize Quick Access Toolbar..."
        };
        customizeItem.Click += OnCustomizeQuickAccessMenuClick;
        menu.Items.Add(customizeItem);
        return menu;
    }

    private void OnQuickAccessMenuClick(object? sender, RoutedEventArgs e)
    {
        if (Model is null || sender is not MenuItem { DataContext: IRibbonControl control })
        {
            return;
        }

        if (Model.ContainsQuickAccess(control.Id))
        {
            Model.RemoveQuickAccess(control.Id);
        }
        else
        {
            Model.AddQuickAccess(control);
        }
    }

    private void OnCustomizeQuickAccessMenuClick(object? sender, RoutedEventArgs e)
    {
        CustomizeQuickAccessRequested?.Invoke(this, EventArgs.Empty);
    }
}
