using MatchingEngine.Domain.Entities;
using MatchingEngine.Domain.Enums;

namespace MatchingEngine.Core;

/// <summary>
/// The single source of truth for "does this attribute value satisfy this
/// criterion" in the in-memory path. This is a line-for-line translation of
/// verification/oracle.py's evaluate_criterion -- if you're changing an
/// operator's behavior, change the oracle and re-run
/// `pytest verification/` first, then bring the same change here and to
/// Batch/BatchScoreQueryBuilder.cs. All three are supposed to agree on
/// every input; nothing here should be "the C# is right, the oracle is
/// just approximate" -- it's the other way around, since the oracle is
/// what's actually been executed and tested.
/// </summary>
public static class CriterionEvaluator
{
    private static readonly HashSet<CriterionOperator> NumericComparisonOperators =
    [
        CriterionOperator.GreaterThan,
        CriterionOperator.GreaterThanOrEqual,
        CriterionOperator.LessThan,
        CriterionOperator.LessThanOrEqual,
    ];

    /// <summary>
    /// Mirrors T-SQL's TRY_CAST(value AS FLOAT): a clean parse or null,
    /// never an exception. <see cref="double.TryParse(string?, out double)"/>
    /// is the correct BCL equivalent here -- unlike SQLite's bare CAST(),
    /// it already fails closed instead of coercing garbage to 0, so no
    /// extra guard is needed on the C# side the way one was needed for the
    /// SQLite verification query (see test_sqlite_batch_verify.py's
    /// test_naive_cast_is_wrong_try_cast_is_right for why that guard
    /// mattered there).
    /// </summary>
    public static double? TryCastNumber(string? value) =>
        value is not null && double.TryParse(value, out var parsed) ? parsed : null;

    public static bool Satisfied(string? attributeValue, Criterion criterion)
    {
        // No three-valued NULL propagation here by design: a missing
        // attribute never satisfies any operator, including NotEquals.
        // Real SQL NULL semantics would make NotEquals against a NULL
        // attribute evaluate true, which is the wrong default for a scoring
        // engine -- see the README's "Decisions and trade-offs".
        if (attributeValue is null)
        {
            return false;
        }

        var op = criterion.Operator;
        var isNumericTyped = criterion.ValueType == AttributeValueType.Number;

        if (isNumericTyped && (NumericComparisonOperators.Contains(op)
                                || op is CriterionOperator.Equals or CriterionOperator.NotEquals))
        {
            var left = TryCastNumber(attributeValue);
            var right = TryCastNumber(criterion.TargetValue);
            if (left is null || right is null)
            {
                return false;
            }

            return op switch
            {
                CriterionOperator.Equals => left == right,
                CriterionOperator.NotEquals => left != right,
                CriterionOperator.GreaterThan => left > right,
                CriterionOperator.GreaterThanOrEqual => left >= right,
                CriterionOperator.LessThan => left < right,
                CriterionOperator.LessThanOrEqual => left <= right,
                _ => false,
            };
        }

        if (NumericComparisonOperators.Contains(op))
        {
            // Authoring mistake: a numeric comparison operator paired with
            // a String-typed attribute. Fail closed rather than throw, so
            // one bad Criterion doesn't take down evaluation of an entire
            // CriteriaSet -- see CriterionEvaluatorTests for the case this
            // guards against.
            return false;
        }

        var target = (criterion.TargetValue ?? string.Empty).Trim();
        var value = attributeValue.Trim();

        return op switch
        {
            CriterionOperator.Equals => string.Equals(value, target, StringComparison.OrdinalIgnoreCase),
            CriterionOperator.NotEquals => !string.Equals(value, target, StringComparison.OrdinalIgnoreCase),
            CriterionOperator.Contains => value.Contains(target, StringComparison.OrdinalIgnoreCase),
            CriterionOperator.In => criterion.TargetValueList.Any(
                v => string.Equals(v.Trim(), value, StringComparison.OrdinalIgnoreCase)),
            _ => throw new ArgumentOutOfRangeException(nameof(criterion), op, "Unhandled operator"),
        };
    }
}
