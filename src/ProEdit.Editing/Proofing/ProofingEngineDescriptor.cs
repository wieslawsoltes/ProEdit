using System;

namespace ProEdit.Editing;

public sealed record ProofingEngineDescriptor(
    string Id,
    string DisplayName,
    ProofingEngineKind Kind,
    Func<ProofingEngineContext, object?> Factory);
