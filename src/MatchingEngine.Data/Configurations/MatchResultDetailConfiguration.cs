using MatchingEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MatchingEngine.Data.Configurations;

public class MatchResultDetailConfiguration : IEntityTypeConfiguration<MatchResultDetail>
{
    public void Configure(EntityTypeBuilder<MatchResultDetail> builder)
    {
        builder.ToTable("MatchResultDetails");

        builder.HasOne(d => d.Criterion)
            .WithMany()
            .HasForeignKey(d => d.CriterionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
