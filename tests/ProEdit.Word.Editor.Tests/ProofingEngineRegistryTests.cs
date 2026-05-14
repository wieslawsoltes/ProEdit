using System;
using ProEdit.Editing;
using Xunit;

namespace ProEdit.Word.Editor.Tests;

public class ProofingEngineRegistryTests
{
    [Fact]
    public void ReloadPlugins_IgnoresFactoriesThatFailToConstruct()
    {
        var registry = new ProofingEngineRegistry(SpellDictionaryRegistry.CreateDefault());
        var assemblyPath = typeof(ThrowingProofingEngineFactory).Assembly.Location;

        registry.ReloadPlugins(new[] { assemblyPath });

        Assert.DoesNotContain(registry.Engines, item => item.Id == "throwing");
    }

    public sealed class ThrowingProofingEngineFactory : IProofingEngineFactory
    {
        public string EngineId => "throwing";
        public string DisplayName => "Throwing";
        public ProofingEngineKind Kind => ProofingEngineKind.Spell;

        public ThrowingProofingEngineFactory()
        {
            throw new InvalidOperationException("boom");
        }

        public object? Create(ProofingEngineContext context)
        {
            throw new NotSupportedException("Should not be invoked.");
        }
    }
}
