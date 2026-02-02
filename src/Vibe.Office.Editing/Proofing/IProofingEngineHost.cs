namespace Vibe.Office.Editing;

public interface IProofingEngineHost
{
    string EngineId { get; }
    Task<ProofingHostResponse> CheckAsync(ProofingHostRequest request, CancellationToken cancellationToken = default);
}
