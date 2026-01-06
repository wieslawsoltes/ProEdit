using Vibe.Office.Documents;
using Vibe.Office.Layout;

namespace Vibe.Office.Rendering;

public interface IDocumentRenderer<in TCanvas>
{
    void Render(TCanvas canvas, Document document, DocumentLayout layout, RenderOptions options);
}
