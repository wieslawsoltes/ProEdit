namespace ProEdit.Ribbon;

public interface IRibbonCommand
{
    bool CanExecute();
    ValueTask ExecuteAsync();
}
