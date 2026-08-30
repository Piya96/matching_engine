namespace MatchingEngine.Domain.Entities;

/// <summary>
/// The per-criterion breakdown behind one MatchResult's total -- what a
/// case worker actually needs to see to explain "why did this entity score
/// 7/10", not just the number.
/// </summary>
public class MatchResultDetail
{
    public int Id { get; set; }

    public int MatchResultId { get; set; }
    public MatchResult? MatchResult { get; set; }

    public int CriterionId { get; set; }
    public Criterion? Criterion { get; set; }

    public bool Satisfied { get; set; }
    public double PointsAwarded { get; set; }
}
