using System;

namespace Vibe.Office.Editing;

public interface IProofingProfileManager : IProofingProfileRegistry
{
    ProofingOptions Options { get; }
    IReadOnlyList<ProofingProfileDefinition> Profiles { get; }
    IReadOnlyList<ProofingEngineDescriptor> Engines { get; }
    event EventHandler? OptionsChanged;
    void UpdateOptions(ProofingOptions options);
}
