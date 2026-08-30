using MatchingEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MatchingEngine.Data.Configurations;

public class EntityConfiguration : IEntityTypeConfiguration<Entity>
{
    public void Configure(EntityTypeBuilder<Entity> builder)
    {
        builder.ToTable("Entities");

        builder.Property(e => e.ExternalRef).HasMaxLength(200).IsRequired();
        builder.Property(e => e.EntityType).HasMaxLength(100).IsRequired();

        // A caller looking up "the Entity for my Supplier #4471" is the
        // single most common read this table serves -- index it, and make
        // it unique so ingestion can safely upsert on (EntityType, ExternalRef)
        // instead of needing its own dedupe pass first.
        builder.HasIndex(e => new { e.EntityType, e.ExternalRef }).IsUnique();

        builder.HasMany(e => e.Attributes)
            .WithOne(a => a.Entity)
            .HasForeignKey(a => a.EntityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
