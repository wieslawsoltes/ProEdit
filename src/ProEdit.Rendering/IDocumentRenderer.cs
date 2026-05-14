using ProEdit.Documents;
using ProEdit.Layout;

namespace ProEdit.Rendering;

public interface IDocumentRenderer<in TCanvas>
{
    void Render(TCanvas canvas, Document document, DocumentLayout layout, RenderOptions options);
}
