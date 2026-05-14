using System.Collections.Generic;

namespace ProEdit.Editing;

public interface IProofingEngineRegistry
{
    IReadOnlyList<ProofingEngineDescriptor> Engines { get; }
    bool TryGet(string id, out ProofingEngineDescriptor descriptor);
}
