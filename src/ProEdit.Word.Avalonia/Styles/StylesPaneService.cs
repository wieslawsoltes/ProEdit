using System;
using Avalonia.Controls;
using ProEdit.Editing;

namespace ProEdit.Word.Avalonia;

internal sealed class StylesPaneService : IStylePaneService
{
    private readonly Func<Window?> _ownerProvider;
    private readonly Func<IStyleManagerService?> _styleServiceResolver;
    private readonly Func<IFontService?>? _fontServiceResolver;
    private StylesPaneWindow? _window;
    private ManageStylesDialog? _manageDialog;

    public StylesPaneService(
        Func<Window?> ownerProvider,
        Func<IStyleManagerService?> styleServiceResolver,
        Func<IFontService?>? fontServiceResolver = null)
    {
        _ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
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
            var owner = _ownerProvider();
            if (owner is not null)
            {
                _window.Show(owner);
            }
            else
            {
                _window.Show();
            }
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
            var owner = _ownerProvider();
            if (owner is not null)
            {
                _manageDialog.Show(owner);
            }
            else
            {
                _manageDialog.Show();
            }
        }

        _manageDialog.Activate();
    }

    private IFontService? ResolveFontService()
    {
        return _fontServiceResolver?.Invoke();
    }
}
