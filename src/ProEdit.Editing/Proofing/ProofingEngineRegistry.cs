using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;

namespace ProEdit.Editing;

public sealed class ProofingEngineRegistry : IProofingEngineRegistry
{
    private readonly Dictionary<string, ProofingEngineDescriptor> _engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProofingEngineDescriptor> _engineList = new();
    private readonly HashSet<string> _builtInIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pluginIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _settingsByEngine = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISpellDictionaryRegistry _dictionaryRegistry;
    private readonly IServiceProvider? _services;

    public IReadOnlyList<ProofingEngineDescriptor> Engines => _engineList;

    public ProofingEngineRegistry(ISpellDictionaryRegistry dictionaryRegistry, IServiceProvider? services = null)
    {
        _dictionaryRegistry = dictionaryRegistry ?? throw new ArgumentNullException(nameof(dictionaryRegistry));
        _services = services;
    }

    public void RegisterBuiltIn(IProofingEngineFactory factory)
    {
        Register(factory, isBuiltIn: true);
    }

    public void Register(IProofingEngineFactory factory)
    {
        Register(factory, isBuiltIn: false);
    }

    private void Register(IProofingEngineFactory factory, bool isBuiltIn)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (string.IsNullOrWhiteSpace(factory.EngineId))
        {
            return;
        }

        var descriptor = new ProofingEngineDescriptor(
            factory.EngineId.Trim(),
            string.IsNullOrWhiteSpace(factory.DisplayName) ? factory.EngineId.Trim() : factory.DisplayName.Trim(),
            factory.Kind,
            context => factory.Create(context));

        if (isBuiltIn)
        {
            _builtInIds.Add(descriptor.Id);
            _pluginIds.Remove(descriptor.Id);
        }
        else
        {
            _pluginIds.Add(descriptor.Id);
        }

        _engines[descriptor.Id] = descriptor;
        RebuildList();
    }

    public bool TryGet(string id, out ProofingEngineDescriptor descriptor)
    {
        descriptor = null!;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return _engines.TryGetValue(id.Trim(), out descriptor!);
    }

    public void UpdateEngineSettings(IDictionary<string, Dictionary<string, string>>? settings)
    {
        _settingsByEngine.Clear();
        if (settings is null)
        {
            return;
        }

        foreach (var (engineId, map) in settings)
        {
            if (string.IsNullOrWhiteSpace(engineId) || map is null)
            {
                continue;
            }

            _settingsByEngine[engineId.Trim()] = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void ReloadPlugins(IEnumerable<string>? assemblyPaths)
    {
        ClearPlugins();
        if (assemblyPaths is null)
        {
            return;
        }

        foreach (var path in assemblyPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            TryRegisterFactoriesFromAssembly(path.Trim());
        }

        RebuildList();
    }

    public bool TryCreateSpellEngine(string id, out ISpellEngine engine)
    {
        engine = null!;
        if (!TryGet(id, out var descriptor))
        {
            return false;
        }

        if (!TryCreateEngine(id, out var instance))
        {
            return false;
        }

        if (instance is ISpellEngine spell)
        {
            engine = spell;
            return true;
        }

        if (descriptor.Kind.HasFlag(ProofingEngineKind.Spell) && instance is IProofingEngine proofing)
        {
            engine = new ProofingSpellEngineAdapter(proofing);
            return true;
        }

        return false;
    }

    public bool TryCreateGrammarEngine(string id, out IGrammarEngine engine)
    {
        engine = null!;
        if (!TryCreateEngine(id, out var instance))
        {
            return false;
        }

        if (instance is IGrammarEngine grammar)
        {
            engine = grammar;
            return true;
        }

        if (instance is IProofingEngine proofing)
        {
            engine = new ProofingEngineAdapter(proofing);
            return true;
        }

        return false;
    }

    public bool TryCreateStyleEngine(string id, out IStyleEngine engine)
    {
        engine = null!;
        if (!TryCreateEngine(id, out var instance))
        {
            return false;
        }

        if (instance is IStyleEngine style)
        {
            engine = style;
            return true;
        }

        if (instance is IProofingEngine proofing)
        {
            engine = new ProofingEngineAdapter(proofing);
            return true;
        }

        return false;
    }

    private bool TryCreateEngine(string id, out object? instance)
    {
        instance = null;
        if (!TryGet(id, out var descriptor))
        {
            return false;
        }

        var settings = _settingsByEngine.TryGetValue(descriptor.Id, out var map)
            ? map
            : null;
        var context = new ProofingEngineContext(_dictionaryRegistry, settings, _services);
        instance = descriptor.Factory(context);
        return instance is not null;
    }

    private void TryRegisterFactoriesFromAssembly(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var pluginDirectory = Path.GetDirectoryName(fullPath);
        Assembly? assembly = null;
        AssemblyLoadContext? loadContext = null;
        Func<AssemblyLoadContext, AssemblyName, Assembly?>? resolver = null;

        try
        {
            loadContext = AssemblyLoadContext.Default;
            resolver = (_, name) =>
            {
                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (AssemblyName.ReferenceMatchesDefinition(loaded.GetName(), name))
                    {
                        return loaded;
                    }
                }

                if (!string.IsNullOrWhiteSpace(pluginDirectory))
                {
                    var candidate = Path.Combine(pluginDirectory, name.Name + ".dll");
                    if (File.Exists(candidate))
                    {
                        return loadContext.LoadFromAssemblyPath(candidate);
                    }
                }

                var appCandidate = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
                if (File.Exists(appCandidate))
                {
                    return loadContext.LoadFromAssemblyPath(appCandidate);
                }

                return null;
            };
            loadContext.Resolving += resolver;
            assembly = loadContext.LoadFromAssemblyPath(fullPath);
        }
        catch
        {
            return;
        }
        finally
        {
            if (loadContext is not null && resolver is not null)
            {
                loadContext.Resolving -= resolver;
            }
        }

        if (assembly is null)
        {
            return;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(static type => type is not null).ToArray()!;
        }
        catch
        {
            return;
        }

        foreach (var type in types)
        {
            if (type is null || type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            if (!typeof(IProofingEngineFactory).IsAssignableFrom(type))
            {
                continue;
            }

            try
            {
                if (Activator.CreateInstance(type) is not IProofingEngineFactory factory)
                {
                    continue;
                }

                Register(factory, isBuiltIn: false);
            }
            catch
            {
                // Ignore factories that fail to construct.
            }
        }
    }

    private void ClearPlugins()
    {
        if (_pluginIds.Count == 0)
        {
            return;
        }

        foreach (var id in _pluginIds)
        {
            _engines.Remove(id);
        }

        _pluginIds.Clear();
        RebuildList();
    }

    private void RebuildList()
    {
        _engineList.Clear();
        foreach (var descriptor in _engines.Values.OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _engineList.Add(descriptor);
        }
    }

    private sealed class ProofingEngineAdapter : IGrammarEngine, IStyleEngine
    {
        private readonly IProofingEngine _engine;

        public string EngineId => _engine.EngineId;

        public ProofingEngineAdapter(IProofingEngine engine)
        {
            _engine = engine;
        }

        public Task<IReadOnlyList<ProofingMatch>> CheckAsync(
            string text,
            string language,
            CancellationToken cancellationToken = default)
        {
            return _engine.CheckAsync(text, language, cancellationToken);
        }
    }

    private sealed class ProofingSpellEngineAdapter : ISpellEngine, IProofingEngine
    {
        private readonly IProofingEngine _engine;

        public string EngineId => _engine.EngineId;

        public ProofingSpellEngineAdapter(IProofingEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public bool Check(ReadOnlySpan<char> word, string language)
        {
            // Proofing engines are evaluated in batch via CheckAsync; avoid per-word remote calls here.
            return true;
        }

        public IReadOnlyList<string> Suggest(ReadOnlySpan<char> word, string language, int maxSuggestions = 5)
        {
            return Array.Empty<string>();
        }

        public Task<IReadOnlyList<ProofingMatch>> CheckAsync(
            string text,
            string language,
            CancellationToken cancellationToken = default)
        {
            return _engine.CheckAsync(text, language, cancellationToken);
        }
    }
}
