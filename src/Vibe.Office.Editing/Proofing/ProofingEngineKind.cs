using System;

namespace Vibe.Office.Editing;

[Flags]
public enum ProofingEngineKind
{
    None = 0,
    Spell = 1,
    Grammar = 2,
    Style = 4
}
