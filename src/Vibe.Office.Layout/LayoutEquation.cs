using Vibe.Office.Documents;

namespace Vibe.Office.Layout;

public sealed record LayoutEquation(EquationInline Equation, MathLayout Layout, float X, float Width, float Height, int Length);
