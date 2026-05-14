using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ProEdit.Ribbon;

namespace ProEdit.Ribbon.Avalonia;

public sealed class RibbonGroupContainer : HeaderedItemsControl
{
    private Button? _launcherButton;
    private bool _hasLauncher;
    private string _launcherIconText = string.Empty;

    public static readonly StyledProperty<RibbonGroupLauncher?> LauncherProperty =
        AvaloniaProperty.Register<RibbonGroupContainer, RibbonGroupLauncher?>(nameof(Launcher));

    public static readonly DirectProperty<RibbonGroupContainer, bool> HasLauncherProperty =
        AvaloniaProperty.RegisterDirect<RibbonGroupContainer, bool>(
            nameof(HasLauncher),
            o => o.HasLauncher);

    public static readonly DirectProperty<RibbonGroupContainer, string> LauncherIconTextProperty =
        AvaloniaProperty.RegisterDirect<RibbonGroupContainer, string>(
            nameof(LauncherIconText),
            o => o.LauncherIconText);

    static RibbonGroupContainer()
    {
        LauncherProperty.Changed.AddClassHandler<RibbonGroupContainer>((control, e) => control.OnLauncherChanged(e));
    }

    public RibbonGroupContainer()
    {
        UpdateLauncherState();
    }

    public RibbonGroupLauncher? Launcher
    {
        get => GetValue(LauncherProperty);
        set => SetValue(LauncherProperty, value);
    }

    public bool HasLauncher
    {
        get => _hasLauncher;
        private set => SetAndRaise(HasLauncherProperty, ref _hasLauncher, value);
    }

    public string LauncherIconText
    {
        get => _launcherIconText;
        private set => SetAndRaise(LauncherIconTextProperty, ref _launcherIconText, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_launcherButton is not null)
        {
            _launcherButton.Click -= OnLauncherButtonClick;
        }

        _launcherButton = e.NameScope.Find<Button>("PART_LauncherButton");
        if (_launcherButton is not null)
        {
            _launcherButton.Click += OnLauncherButtonClick;
        }
    }

    private void OnLauncherChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is RibbonGroupLauncher oldLauncher)
        {
            oldLauncher.PropertyChanged -= OnLauncherPropertyChanged;
        }

        if (e.NewValue is RibbonGroupLauncher newLauncher)
        {
            newLauncher.PropertyChanged += OnLauncherPropertyChanged;
        }

        UpdateLauncherState();
    }

    private void OnLauncherPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RibbonGroupLauncher.IsVisible) or nameof(RibbonGroupLauncher.IconKey))
        {
            UpdateLauncherState();
        }
    }

    private void UpdateLauncherState()
    {
        var launcher = Launcher;
        HasLauncher = launcher?.IsVisible == true;
        LauncherIconText = ResolveLauncherIconText(launcher);
    }

    private static string ResolveLauncherIconText(RibbonGroupLauncher? launcher)
    {
        var iconKey = launcher?.IconKey ?? "RibbonIcon.Launcher";
        return RibbonIconResolver.ResolveText(iconKey) ?? string.Empty;
    }

    private async void OnLauncherButtonClick(object? sender, RoutedEventArgs e)
    {
        var launcher = Launcher;
        if (launcher is null || !launcher.IsEnabled)
        {
            return;
        }

        await launcher.ExecuteAsync();
        var ribbon = this.GetVisualAncestors().OfType<RibbonControl>().FirstOrDefault();
        ribbon?.RefreshState();
    }
}
