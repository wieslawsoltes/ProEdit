using PdfSharp.Drawing;
using PdfSharp.Pdf;
using ProEdit.Pdf;

namespace ProEdit.Pdf.PdfSharp;

public sealed class PdfSharpWriter : IPdfWriter
{
    public string ProviderId => PdfProviderIds.PdfSharp;
    public bool SupportsIncrementalUpdate => false;

    public void Write(PdfDocumentAst document, Stream output, PdfWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        var pdf = new PdfDocument();
        foreach (var page in document.Pages)
        {
            var pdfPage = pdf.AddPage();
            if (page.Width > 0 && page.Height > 0)
            {
                pdfPage.Width = XUnit.FromPoint(page.Width);
                pdfPage.Height = XUnit.FromPoint(page.Height);
            }
        }

        pdf.Save(output, false);
    }
}
