using System.Collections.Generic;

namespace Vibe.Office.Editing;

public interface IProofingEngineRegistry
{
    IReadOnlyList<ProofingEngineDescriptor> Engines { get; }
    bool TryGet(string id, out ProofingEngineDescriptor descriptor);
}
