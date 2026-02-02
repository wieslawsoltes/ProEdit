namespace Vibe.Office.Editing;

public interface IProofingOptionsStore
{
    ProofingOptions Load();
    void Save(ProofingOptions options);
}
