namespace ProEdit.Printing;

public sealed record PrintPreviewPage(int PageNumber, byte[] ImageBytes, float Width, float Height);
