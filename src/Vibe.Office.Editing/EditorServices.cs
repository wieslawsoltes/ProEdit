namespace Vibe.Office.Editing;

public sealed class EditorServices
{
    private readonly Dictionary<Type, object> _services = new();

    public void Register<TService>(TService service) where TService : class
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(TService)] = service;
    }

    public bool Remove<TService>() where TService : class
    {
        return _services.Remove(typeof(TService));
    }

    public bool Remove(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _services.Remove(serviceType);
    }

    public bool TryGet<TService>(out TService service) where TService : class
    {
        if (_services.TryGetValue(typeof(TService), out var value))
        {
            service = (TService)value;
            return true;
        }

        service = null!;
        return false;
    }

    public bool TryGet(Type serviceType, out object? service)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (_services.TryGetValue(serviceType, out var value))
        {
            service = value;
            return true;
        }

        service = null;
        return false;
    }

    public TService GetRequired<TService>() where TService : class
    {
        if (TryGet<TService>(out var service))
        {
            return service;
        }

        throw new InvalidOperationException($"Service not registered: {typeof(TService).Name}");
    }
}

public interface IEditorModule
{
    void Register(EditorModuleContext context);
}

public sealed class EditorModuleContext
{
    public EditorServices Services { get; }
    public EditorCommandDispatcher Commands { get; }

    public EditorModuleContext(EditorServices services, EditorCommandDispatcher commands)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }
}
