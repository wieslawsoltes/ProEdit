using ProEdit.Layout;

namespace ProEdit.Editing;

public interface IProofingSpanProviderHost
{
    void SetProofingSpanProvider(IProofingSpanProvider? provider);
}
