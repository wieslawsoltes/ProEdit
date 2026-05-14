namespace ProEdit.Editing;

public interface IProofingEngineFactory
{
    string EngineId { get; }
    string DisplayName { get; }
    ProofingEngineKind Kind { get; }
    object? Create(ProofingEngineContext context);
}
