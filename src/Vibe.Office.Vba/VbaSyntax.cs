namespace Vibe.Office.Vba;

public sealed record VbaModuleSyntax(IReadOnlyList<VbaModuleMemberSyntax> Members);

public abstract record VbaModuleMemberSyntax;

public sealed record VbaSubroutineSyntax(
    string Name,
    IReadOnlyList<VbaParameterSyntax> Parameters,
    IReadOnlyList<VbaStatementSyntax> Statements) : VbaModuleMemberSyntax;

public sealed record VbaFunctionSyntax(
    string Name,
    IReadOnlyList<VbaParameterSyntax> Parameters,
    IReadOnlyList<VbaStatementSyntax> Statements) : VbaModuleMemberSyntax;

public sealed record VbaParameterSyntax(string Name);

public readonly record struct VbaSourceSpan(int Line, int Column);

public abstract record VbaStatementSyntax(VbaSourceSpan Span);

public sealed record VbaAssignmentStatementSyntax(
    VbaExpressionSyntax Target,
    VbaExpressionSyntax Value,
    bool IsSet,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaCallStatementSyntax(
    VbaExpressionSyntax Target,
    IReadOnlyList<VbaExpressionSyntax> Arguments,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaExpressionStatementSyntax(
    VbaExpressionSyntax Expression,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaIfStatementSyntax(
    VbaExpressionSyntax Condition,
    IReadOnlyList<VbaStatementSyntax> ThenStatements,
    IReadOnlyList<VbaStatementSyntax> ElseStatements,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaForStatementSyntax(
    string IteratorName,
    VbaExpressionSyntax Start,
    VbaExpressionSyntax End,
    VbaExpressionSyntax? Step,
    IReadOnlyList<VbaStatementSyntax> Body,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaDoWhileStatementSyntax(
    VbaExpressionSyntax Condition,
    IReadOnlyList<VbaStatementSyntax> Body,
    bool IsUntil,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaWhileStatementSyntax(
    VbaExpressionSyntax Condition,
    IReadOnlyList<VbaStatementSyntax> Body,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaSelectStatementSyntax(
    VbaExpressionSyntax Selector,
    IReadOnlyList<VbaSelectCaseSyntax> Cases,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaSelectCaseSyntax(
    IReadOnlyList<VbaSelectCaseClauseSyntax> Clauses,
    IReadOnlyList<VbaStatementSyntax> Body,
    bool IsElse);

public abstract record VbaSelectCaseClauseSyntax;

public sealed record VbaSelectCaseValueClauseSyntax(VbaExpressionSyntax Value) : VbaSelectCaseClauseSyntax;

public sealed record VbaSelectCaseRangeClauseSyntax(
    VbaExpressionSyntax Start,
    VbaExpressionSyntax End) : VbaSelectCaseClauseSyntax;

public sealed record VbaSelectCaseComparisonClauseSyntax(
    VbaSelectComparisonOperator Operator,
    VbaExpressionSyntax Value) : VbaSelectCaseClauseSyntax;

public enum VbaSelectComparisonOperator
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual
}

public sealed record VbaWithStatementSyntax(
    VbaExpressionSyntax Target,
    IReadOnlyList<VbaStatementSyntax> Body,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaVariableDeclaratorSyntax(
    string Name,
    IReadOnlyList<VbaArrayBoundSyntax> Bounds);

public sealed record VbaArrayBoundSyntax(
    VbaExpressionSyntax? Lower,
    VbaExpressionSyntax Upper);

public sealed record VbaDimStatementSyntax(
    IReadOnlyList<VbaVariableDeclaratorSyntax> Declarators,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaRedimStatementSyntax(
    IReadOnlyList<VbaVariableDeclaratorSyntax> Declarators,
    bool Preserve,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaExitStatementSyntax(
    VbaExitKind Kind,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public sealed record VbaOnErrorStatementSyntax(
    VbaOnErrorMode Mode,
    VbaSourceSpan Span) : VbaStatementSyntax(Span);

public enum VbaExitKind
{
    Sub,
    Function,
    For,
    Do
}

public enum VbaOnErrorMode
{
    None,
    ResumeNext,
    GoTo0
}

public abstract record VbaExpressionSyntax;

public sealed record VbaLiteralExpressionSyntax(VbaLiteral Value) : VbaExpressionSyntax;

public sealed record VbaIdentifierExpressionSyntax(string Name) : VbaExpressionSyntax;

public sealed record VbaMemberAccessExpressionSyntax(
    VbaExpressionSyntax Target,
    string MemberName) : VbaExpressionSyntax;

public sealed record VbaCallExpressionSyntax(
    VbaExpressionSyntax Target,
    IReadOnlyList<VbaExpressionSyntax> Arguments) : VbaExpressionSyntax;

public sealed record VbaUnaryExpressionSyntax(
    VbaUnaryOperator Operator,
    VbaExpressionSyntax Operand) : VbaExpressionSyntax;

public sealed record VbaBinaryExpressionSyntax(
    VbaExpressionSyntax Left,
    VbaBinaryOperator Operator,
    VbaExpressionSyntax Right) : VbaExpressionSyntax;

public sealed record VbaParenthesizedExpressionSyntax(VbaExpressionSyntax Expression) : VbaExpressionSyntax;

public sealed record VbaWithReferenceExpressionSyntax : VbaExpressionSyntax;

public enum VbaUnaryOperator
{
    Negate,
    Not
}

public enum VbaBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Concat,
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    And,
    Or
}

public readonly record struct VbaLiteral(
    VbaLiteralKind Kind,
    string Text);

public enum VbaLiteralKind
{
    Number,
    String,
    Boolean,
    Empty
}
