using System;
using Avalonia.Controls;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

internal sealed class StylesPaneService : IStylePaneService
{
    private readonly Window _owner;
    private readonly Func<IStyleService?> _styleServiceResolver;
    private StylesPaneWindow? _window;

    public StylesPaneService(Window owner, Func<IStyleService?> styleServiceResolver)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _styleServiceResolver = styleServiceResolver ?? throw new ArgumentNullException(nameof(styleServiceResolver));
    }

    public void OpenStylesPane()
    {
        Open(false);
    }

    public void OpenStylesManager()
    {
        Open(true);
    }

    private void Open(bool manageMode)
    {
        var styleService = _styleServiceResolver();
        if (styleService is null)
        {
            return;
        }

        if (_window is null)
        {
            _window = new StylesPaneWindow(styleService);
            _window.Closed += (_, _) => _window = null;
        }
        else
        {
            _window.SetService(styleService);
        }

        _window.SetMode(manageMode);
        if (!_window.IsVisible)
        {
            _window.Show(_owner);
        }

        _window.Activate();
    }
}
