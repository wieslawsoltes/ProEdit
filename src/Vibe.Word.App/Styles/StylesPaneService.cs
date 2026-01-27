using System;
using Avalonia.Controls;
using Vibe.Office.Editing;

namespace Vibe.Word.App;

internal sealed class StylesPaneService : IStylePaneService
{
    private readonly Window _owner;
    private readonly Func<IStyleManagerService?> _styleServiceResolver;
    private readonly Func<IFontService?>? _fontServiceResolver;
    private StylesPaneWindow? _window;
    private ManageStylesDialog? _manageDialog;

    public StylesPaneService(Window owner, Func<IStyleManagerService?> styleServiceResolver, Func<IFontService?>? fontServiceResolver = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _styleServiceResolver = styleServiceResolver ?? throw new ArgumentNullException(nameof(styleServiceResolver));
        _fontServiceResolver = fontServiceResolver;
    }

    public void OpenStylesPane()
    {
        var styleService = _styleServiceResolver();
        if (styleService is null)
        {
            return;
        }

        if (_window is null)
        {
            _window = new StylesPaneWindow(styleService, ResolveFontService(), OpenManageStylesDialog);
            _window.Closed += (_, _) => _window = null;
        }
        else
        {
            _window.SetServices(styleService, ResolveFontService());
        }

        if (!_window.IsVisible)
        {
            _window.Show(_owner);
        }

        _window.Activate();
    }

    public void OpenStylesManager()
    {
        OpenManageStylesDialog();
    }

    private void OpenManageStylesDialog()
    {
        var styleService = _styleServiceResolver();
        if (styleService is null)
        {
            return;
        }

        if (_manageDialog is null)
        {
            _manageDialog = new ManageStylesDialog(styleService, ResolveFontService());
            _manageDialog.Closed += (_, _) => _manageDialog = null;
        }
        else
        {
            _manageDialog.SetServices(styleService, ResolveFontService());
        }

        if (!_manageDialog.IsVisible)
        {
            _manageDialog.Show(_owner);
        }

        _manageDialog.Activate();
    }

    private IFontService? ResolveFontService()
    {
        return _fontServiceResolver?.Invoke();
    }
}
