using Vibe.Office.Documents;
using Vibe.Office.Layout;

namespace Vibe.Office.Editing;

public interface IEditorSessionFactory
{
    IEditorMutableSession Create(ITextMeasurer measurer, Document? document = null);
}

public sealed class EditorKernel
{
    public EditorServices Services { get; } = new EditorServices();
    public EditorCommandDispatcher Commands { get; } = new EditorCommandDispatcher();
    public IEditorSessionFactory SessionFactory { get; }

    public EditorKernel(IEditorSessionFactory sessionFactory)
    {
        SessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public void AddModule(IEditorModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        module.Register(new EditorModuleContext(Services, Commands));
    }

    public IEditorMutableSession CreateSession(ITextMeasurer measurer, Document? document = null)
    {
        return SessionFactory.Create(measurer, document);
    }
}
