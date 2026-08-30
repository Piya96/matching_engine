namespace MatchingEngine.Domain.Entities;

/// <summary>
/// The outcome of scoring one Entity against one CriteriaSet at one point
/// in time. Stored (not just returned in-memory) so a caller can query
/// "who passed criteria set X last week" without re-running the engine --
/// the whole reason MaxPossibleScore is persisted alongside TotalScore
/// rather than left for the caller to recompute from a CriteriaSet that
/// may have since moved to a new Version.
/// </summary>
public class MatchResult
{
    public int Id { get; set; }

    public int EntityId { get; set; }
    public Entity? Entity { get; set; }

    public int CriteriaSetId { get; set; }
    public CriteriaSet? CriteriaSet { get; set; }

    public double TotalScore { get; set; }
    public double MaxPossibleScore { get; set; }
    public bool Passed { get; set; }
    public DateTime EvaluatedUtc { get; set; } = DateTime.UtcNow;

    public List<MatchResultDetail> Details { get; set; } = new();
}
