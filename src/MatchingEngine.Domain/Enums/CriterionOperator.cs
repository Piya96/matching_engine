namespace MatchingEngine.Domain.Enums;

/// <summary>
/// Every member here has an exact, independently-verified semantics in
/// verification/oracle.py -- CriterionEvaluator (the in-memory path) and
/// BatchScoreQueryBuilder (the set-based SQL path) both implement exactly
/// that oracle, not their own interpretation of what these names should
/// mean. If you add a member here, add it to the oracle first.
/// </summary>
public enum CriterionOperator
{
    Equals = 0,
    NotEquals = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5,
    Contains = 6,
    In = 7,
}
