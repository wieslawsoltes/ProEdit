using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using Vibe.Office.Ribbon;

namespace Vibe.Office.Ribbon.Avalonia;

public partial class RibbonControl : UserControl
{
    private readonly Dictionary<string, Control> _keyTipTargets = new(StringComparer.OrdinalIgnoreCase);
    private Canvas? _keyTipOverlay;
    private bool _keyTipsVisible;
    private string _keyTipBuffer = string.Empty;

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
        LayoutUpdated += OnLayoutUpdated;
    }

    private void OnModelChanged()
    {
        DataContext = Model;
        if (Model is { SelectedTab: null, Tabs.Count: > 0 })
        {
            Model.SelectedTab = Model.Tabs[0];
        }

        RefreshState();
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
                case RibbonMenuSeparator separator when separator.IsVisible:
                    contextMenu.Items.Add(new Separator());
                    break;
            }
        }

        return contextMenu;
    }

    private async void OnRibbonMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: RibbonMenuItem item })
        {
            await item.ExecuteAsync();
            RefreshState();
        }
    }

    public void RefreshState()
    {
        Model?.RefreshState();
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

            if (control is not Button && control is not ToggleButton)
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
