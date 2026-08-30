namespace MatchingEngine.Domain.Entities;

/// <summary>
/// A named, versioned bundle of <see cref="Criterion"/> rows evaluated
/// together against an Entity to produce one <see cref="MatchResult"/>.
/// Versioned rather than mutated in place so a MatchResult from last month
/// still points at the rule set that actually produced it -- re-scoring
/// history against today's rules would misrepresent what happened.
/// </summary>
public class CriteriaSet
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<Criterion> Criteria { get; set; } = new();
}
