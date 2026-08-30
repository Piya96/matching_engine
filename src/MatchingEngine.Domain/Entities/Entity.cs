namespace MatchingEngine.Domain.Entities;

/// <summary>
/// A generic thing being matched against criteria -- a supplier, an
/// application, a candidate, a claim, whatever the caller's domain is. This
/// repo intentionally knows nothing about what an Entity represents:
/// <see cref="EntityType"/> is the only domain hook, and it's an opaque
/// string the caller defines, not an enum this repo owns.
/// </summary>
public class Entity
{
    public int Id { get; set; }

    /// <summary>The caller's own identifier for this thing (their primary
    /// key, not this engine's). Never interpreted, only stored and
    /// returned on match results so callers can join back to their data.</summary>
    public string ExternalRef { get; set; } = default!;

    /// <summary>Caller-defined discriminator, e.g. "Supplier", "Applicant".
    /// Not a foreign key to anything -- deliberately just a string, since
    /// this engine has no business modelling the caller's domain types.</summary>
    public string EntityType { get; set; } = default!;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<EntityAttribute> Attributes { get; set; } = new();
}
