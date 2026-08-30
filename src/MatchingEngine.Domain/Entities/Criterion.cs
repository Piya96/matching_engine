using MatchingEngine.Domain.Enums;

namespace MatchingEngine.Domain.Entities;

/// <summary>
/// One rule within a <see cref="CriteriaSet"/>: "does Entity.AttributeName
/// satisfy Operator against TargetValue/TargetValueList", worth Weight
/// points if satisfied, and optionally a hard gate on the whole match via
/// IsRequired. The exact semantics of every Operator x ValueType
/// combination are pinned down in verification/oracle.py -- this class is
/// just data, on purpose; see CriterionEvaluator for where the semantics
/// actually live in C#.
/// </summary>
public class Criterion
{
    public int Id { get; set; }

    public int CriteriaSetId { get; set; }
    public CriteriaSet? CriteriaSet { get; set; }

    public string AttributeName { get; set; } = default!;
    public CriterionOperator Operator { get; set; }
    public AttributeValueType ValueType { get; set; }

    /// <summary>Used by every operator except <see cref="CriterionOperator.In"/>.</summary>
    public string? TargetValue { get; set; }

    /// <summary>Used only by <see cref="CriterionOperator.In"/>. Persisted as a
    /// JSON array column -- see CriterionConfiguration for the value
    /// converter -- rather than a child table, because it's small,
    /// authored as a unit, and never queried independently of its
    /// Criterion.</summary>
    public List<string> TargetValueList { get; set; } = new();

    /// <summary>Points contributed to TotalScore when satisfied. A required
    /// criterion can legitimately have Weight = 0 -- it gates Passed
    /// without needing to also move the score (see the "blacklisted" style
    /// criterion in the tests and README).</summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>If true and this criterion is not satisfied, the whole
    /// MatchResult.Passed is false regardless of TotalScore. Required
    /// criteria disqualify; non-required criteria just rank.</summary>
    public bool IsRequired { get; set; }
}
