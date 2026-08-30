using MatchingEngine.Domain.Enums;

namespace MatchingEngine.Domain.Entities;

/// <summary>
/// One (name, value) fact about an <see cref="Entity"/> -- the "AV" half of
/// the EAV schema. Values are always stored as strings and typed out of
/// band via <see cref="ValueType"/>, which is what lets one Entities table
/// hold arbitrarily different attribute shapes for different EntityTypes
/// without a schema migration every time a caller adds a new attribute.
/// See the README's EAV section for what that trade-off actually costs at
/// query time, and "attribute promotion" for the escape hatch.
/// </summary>
public class EntityAttribute
{
    public int Id { get; set; }

    public int EntityId { get; set; }
    public Entity? Entity { get; set; }

    public string AttributeName { get; set; } = default!;
    public string? AttributeValue { get; set; }
    public AttributeValueType ValueType { get; set; }
}
