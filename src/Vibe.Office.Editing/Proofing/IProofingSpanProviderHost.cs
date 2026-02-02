using Vibe.Office.Layout;

namespace Vibe.Office.Editing;

public interface IProofingSpanProviderHost
{
    void SetProofingSpanProvider(IProofingSpanProvider? provider);
}
