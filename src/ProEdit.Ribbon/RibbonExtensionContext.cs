namespace ProEdit.Ribbon;

public sealed class RibbonExtensionContext
{
    private readonly Func<Type, object?>? _serviceAccessor;

    public RibbonExtensionContext(Func<Type, object?>? serviceAccessor = null)
    {
        _serviceAccessor = serviceAccessor;
    }

    public bool TryGetService<T>(out T service) where T : class
    {
        if (_serviceAccessor is null)
        {
            service = null!;
            return false;
        }

        var value = _serviceAccessor(typeof(T));
        if (value is T typed)
        {
            service = typed;
            return true;
        }

        service = null!;
        return false;
    }

    public T? GetService<T>() where T : class
    {
        return TryGetService(out T service) ? service : null;
    }

    public object? GetService(Type serviceType)
    {
        return _serviceAccessor?.Invoke(serviceType);
    }
}
