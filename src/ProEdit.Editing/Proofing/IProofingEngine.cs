namespace ProEdit.Editing;

public interface IProofingEngine
{
    string EngineId { get; }

    Task<IReadOnlyList<ProofingMatch>> CheckAsync(
        string text,
        string language,
        CancellationToken cancellationToken = default);
}
