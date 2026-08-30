using MatchingEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MatchingEngine.Data.Configurations;

public class EntityAttributeConfiguration : IEntityTypeConfiguration<EntityAttribute>
{
    public void Configure(EntityTypeBuilder<EntityAttribute> builder)
    {
        builder.ToTable("EntityAttributes");

        builder.Property(a => a.AttributeName).HasMaxLength(150).IsRequired();
        // NVARCHAR(MAX) because this column stores every attribute of every
        // EntityType -- a free-text "notes" field and a "credit_limit"
        // number both land here as a string. See the README's EAV section
        // for what that costs at query time and how the batch scorer works
        // around not being able to index a typed value.
        builder.Property(a => a.AttributeValue).HasColumnType("nvarchar(max)");

        // The index the whole engine's read performance rides on: every
        // Criterion evaluation is "find this Entity's value for this
        // AttributeName". Unique because an attribute is current-state, not
        // a history -- re-collecting "annual_volume" for an entity should
        // overwrite the row, not append one; see MatchResultDetail for
        // where history actually belongs (a scored snapshot, not raw facts).
        builder.HasIndex(a => new { a.EntityId, a.AttributeName }).IsUnique();
    }
}
