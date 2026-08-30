using System.Text.Json;
using MatchingEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MatchingEngine.Data.Configurations;

public class CriterionConfiguration : IEntityTypeConfiguration<Criterion>
{
    public void Configure(EntityTypeBuilder<Criterion> builder)
    {
        builder.ToTable("Criteria");

        builder.Property(c => c.AttributeName).HasMaxLength(150).IsRequired();

        // Stored as their string names ("Equals", "GreaterThan", ...) rather
        // than the underlying int. Two reasons: the value is human-readable
        // when you're staring at the Criteria table deciding why a match
        // failed, and -- more importantly -- BatchScoreQueryBuilder's raw
        // SQL matches against these exact strings (see MatchingEngine.Core
        // /Batch and verification/test_sqlite_batch_verify.py, which encode
        // the same string literals). Reordering the enum must never change
        // stored data or the SQL's WHEN clauses; storing as int would make
        // that link invisible.
        builder.Property(c => c.Operator).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.ValueType).HasConversion<string>().HasMaxLength(20);

        builder.Property(c => c.TargetValue).HasMaxLength(500);

        // TargetValueList only exists for the In operator, is authored as a
        // unit, and is never queried independently of its Criterion -- a
        // child table would be normalized correctness for no actual
        // benefit. Serialized as JSON text; the batch SQL side reads it
        // with json_each/OPENJSON rather than a join to a real table -- see
        // the README's SQL Server translation notes for the equivalent
        // STRING_SPLIT/OPENJSON approaches.
        var targetListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => string.IsNullOrEmpty(v)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

        // EF Core can't tell two different List<string> instances with the
        // same elements apart without help, and will otherwise think this
        // property changed on every SaveChanges -- a genuinely easy thing
        // to miss with a converted collection property until you notice
        // UPDATE statements firing for rows nothing actually changed on.
        var targetListComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
            v => v.ToList());

        builder.Property(c => c.TargetValueList)
            .HasConversion(targetListConverter)
            .Metadata.SetValueComparer(targetListComparer);

        builder.Property(c => c.Weight).HasColumnType("float");
    }
}
