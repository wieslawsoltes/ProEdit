namespace Vibe.Office.Ribbon;

public interface IRibbonExtension
{
    void Build(RibbonModelBuilder builder, RibbonExtensionContext context);
}
