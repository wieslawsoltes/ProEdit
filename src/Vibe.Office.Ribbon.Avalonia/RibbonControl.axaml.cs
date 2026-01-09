using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vibe.Office.Ribbon;

namespace Vibe.Office.Ribbon.Avalonia;

public partial class RibbonControl : UserControl
{
    private readonly Dictionary<string, Control> _keyTipTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ComboBox> _comboBoxUserInput = new();
    private Canvas? _keyTipOverlay;
    private bool _keyTipsVisible;
    private string _keyTipBuffer = string.Empty;
    private ScrollViewer? _groupScrollViewer;
    private bool _isUpdatingGroupLayout;
    private RibbonTab? _lastLayoutTab;
    private int _stateUpdateDepth;

    public event EventHandler? CustomizeQuickAccessRequested;

    public static readonly StyledProperty<RibbonModel?> ModelProperty =
        AvaloniaProperty.Register<RibbonControl, RibbonModel?>(nameof(Model));

    public RibbonModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public RibbonControl()
    {
        InitializeComponent();
        ModelProperty.Changed.AddClassHandler<RibbonControl>((control, _) => control.OnModelChanged());
        _keyTipOverlay = this.FindControl<Canvas>("KeyTipOverlay");
        _groupScrollViewer = this.FindControl<ScrollViewer>("RibbonGroupsScrollViewer");
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnModelChanged()
    {
        using var _ = BeginStateUpdate();

        DataContext = Model;
        if (Model is { SelectedTab: null, Tabs.Count: > 0 })
        {
            Model.SelectedTab = Model.Tabs[0];
        }

        Model?.RefreshState();
    }

    private async void OnRibbonButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RibbonButton button })
        {
            await button.ExecuteAsync();
            RefreshState();
        }
    }

    private async void OnRibbonToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { DataContext: RibbonToggleButton toggle } button)
        {
            await toggle.ToggleAsync(button.IsChecked ?? false);
            RefreshState();
        }
    }

    private async void OnRibbonSplitPrimaryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RibbonSplitButton split })
        {
            await split.ExecutePrimaryAsync();
            RefreshState();
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
    }

    private async void OnRibbonSpinnerIncreaseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RibbonSpinner spinner })
        {
            return;
        }

        await spinner.IncreaseAsync();
        RefreshState();
    }

    private async void OnRibbonSpinnerDecreaseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: RibbonSpinner spinner })
        {
            return;
        }

        await spinner.DecreaseAsync();
        RefreshState();
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
            return;
        }

        if (double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            await spinner.SetValueAsync(value);
            RefreshState();
        }
    }

    private async void OnRibbonGallerySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { DataContext: RibbonGallery gallery })
        {
            return;
        }

        if (_stateUpdateDepth > 0)
        {
            return;
        }

        await gallery.SelectAsync(gallery.SelectedItem as RibbonGalleryItem);
        RefreshState();
    }

    private async void OnRibbonColorSplitPrimaryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RibbonColorSplitButton button })
        {
            await button.ExecutePrimaryAsync();
            RefreshState();
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

    private async void OnRibbonMenuItemClick(object? sender, RoutedEventArgs e)
    {
        switch (sender)
        {
            case MenuItem { DataContext: RibbonMenuItem item }:
                await item.ExecuteAsync();
                RefreshState();
                break;
            case MenuItem { DataContext: RibbonMenuToggleItem toggle }:
                await toggle.ExecuteAsync();
                RefreshState();
                break;
        }
    }

    public void RefreshState()
    {
        using var _ = BeginStateUpdate();
        Model?.RefreshState();
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
            if (extentWidth > viewportWidth + 1)
            {
                ShrinkGroups(tab.Groups);
                return;
            }

            if (extentWidth < viewportWidth - 24)
            {
                ExpandGroups(tab.Groups);
            }
        }
        finally
        {
            _isUpdatingGroupLayout = false;
        }
    }

    private static void ShrinkGroups(IReadOnlyList<RibbonGroup> groups)
    {
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i].TryStepLayoutMode(shrink: true))
            {
                return;
            }
        }
    }

    private static void ExpandGroups(IReadOnlyList<RibbonGroup> groups)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].TryStepLayoutMode(shrink: false))
            {
                return;
            }
        }
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

        if (control is Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        else if (control is ToggleButton toggle)
        {
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
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
