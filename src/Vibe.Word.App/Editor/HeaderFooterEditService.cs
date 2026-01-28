using Vibe.Office.Editing;
using Vibe.Office.Rendering;

namespace Vibe.Word.App;

public sealed class HeaderFooterEditService : IHeaderFooterEditService
{
    private readonly DocumentView _view;

    public HeaderFooterEditService(DocumentView view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public bool IsEditing => _view.IsHeaderFooterEditing;
    public bool IsHeaderEditing => _view.HeaderFooterMode == HeaderFooterEditMode.Header;
    public bool IsFooterEditing => _view.HeaderFooterMode == HeaderFooterEditMode.Footer;
    public int SectionIndex => _view.HeaderFooterSectionIndex;
    public int SectionCount => _view.Document.SectionCount;
    public HeaderFooterVariant Variant => _view.HeaderFooterVariant;

    public bool DifferentFirstPage
    {
        get => _view.HeaderFooterDifferentFirstPage;
        set => _view.SetHeaderFooterDifferentFirstPage(value);
    }

    public bool DifferentOddEven
    {
        get => _view.Document.EvenAndOddHeaders;
        set => _view.SetHeaderFooterDifferentOddEven(value);
    }

    public void BeginHeader() => _view.BeginHeaderFooterEdit(HeaderFooterEditMode.Header);

    public void BeginFooter() => _view.BeginHeaderFooterEdit(HeaderFooterEditMode.Footer);

    public void Close() => _view.EndHeaderFooterEdit();

    public void GoToPreviousSection() => _view.NavigateHeaderFooterSection(-1);

    public void GoToNextSection() => _view.NavigateHeaderFooterSection(1);

    public void SetVariant(HeaderFooterVariant variant) => _view.SetHeaderFooterVariant(variant);
}
