using MatchingEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MatchingEngine.Data.Configurations;

public class CriteriaSetConfiguration : IEntityTypeConfiguration<CriteriaSet>
{
    public void Configure(EntityTypeBuilder<CriteriaSet> builder)
    {
        builder.ToTable("CriteriaSets");

        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();

        builder.HasIndex(c => new { c.Name, c.Version }).IsUnique();

        builder.HasMany(c => c.Criteria)
            .WithOne(cr => cr.CriteriaSet)
            .HasForeignKey(cr => cr.CriteriaSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
