namespace Vibe.Office.Ribbon;

public interface IRibbonCommand
{
    bool CanExecute();
    ValueTask ExecuteAsync();
}
