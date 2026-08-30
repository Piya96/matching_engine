using MatchingEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MatchingEngine.Data.Configurations;

public class MatchResultConfiguration : IEntityTypeConfiguration<MatchResult>
{
    public void Configure(EntityTypeBuilder<MatchResult> builder)
    {
        builder.ToTable("MatchResults");

        // Covers the "most recent match for this entity against this rule
        // set" read -- descending on EvaluatedUtc so that query is a
        // backwards index seek (TOP 1 ... ORDER BY EvaluatedUtc DESC)
        // instead of a scan-and-sort.
        builder.HasIndex(m => new { m.EntityId, m.CriteriaSetId, m.EvaluatedUtc })
            .IsDescending(false, false, true);

        builder.HasOne(m => m.Entity)
            .WithMany()
            .HasForeignKey(m => m.EntityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.CriteriaSet)
            .WithMany()
            .HasForeignKey(m => m.CriteriaSetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.Details)
            .WithOne(d => d.MatchResult)
            .HasForeignKey(d => d.MatchResultId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
