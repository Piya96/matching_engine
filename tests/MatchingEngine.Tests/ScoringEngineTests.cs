using MatchingEngine.Domain.Entities;
using MatchingEngine.Domain.Enums;
using Xunit;

namespace MatchingEngine.Core.Tests;

// Mirrors verification/test_oracle.py's score_entity tests.
public class ScoringEngineTests
{
    private static Entity EntityWith(params (string Name, string Value)[] attrs) => new()
    {
        Id = 1,
        ExternalRef = "test",
        EntityType = "TestEntity",
        Attributes = attrs.Select(a => new EntityAttribute { AttributeName = a.Name, AttributeValue = a.Value }).ToList(),
    };

    [Fact]
    public void Score_sums_weights_of_satisfied_criteria_only()
    {
        var criteriaSet = new CriteriaSet
        {
            Id = 1,
            Criteria =
            [
                new Criterion { Id = 1, AttributeName = "tier", Operator = CriterionOperator.Equals, TargetValue = "gold", Weight = 3 },
                new Criterion { Id = 2, AttributeName = "score", Operator = CriterionOperator.GreaterThan, ValueType = AttributeValueType.Number, TargetValue = "80", Weight = 2 },
                new Criterion { Id = 3, AttributeName = "region", Operator = CriterionOperator.In, TargetValueList = ["NL", "DE"], Weight = 1 },
            ],
        };
        var entity = EntityWith(("tier", "gold"), ("score", "75"), ("region", "NL"));

        var result = ScoringEngine.Evaluate(entity, criteriaSet);

        Assert.Equal(4, result.TotalScore); // tier (3) + region (1), not score
        Assert.Equal(6, result.MaxPossibleScore);
        Assert.Equal([true, false, true], result.Details.Select(d => d.Satisfied));
    }

    [Fact]
    public void Required_criterion_failing_fails_the_whole_match_regardless_of_score()
    {
        var criteriaSet = new CriteriaSet
        {
            Id = 1,
            Criteria =
            [
                new Criterion { Id = 1, AttributeName = "blacklisted", Operator = CriterionOperator.Equals, TargetValue = "false", Weight = 0, IsRequired = true },
                new Criterion { Id = 2, AttributeName = "tier", Operator = CriterionOperator.Equals, TargetValue = "gold", Weight = 10 },
            ],
        };
        var entity = EntityWith(("blacklisted", "true"), ("tier", "gold"));

        var result = ScoringEngine.Evaluate(entity, criteriaSet);

        Assert.Equal(10, result.TotalScore);
        Assert.False(result.Passed);
    }

    [Fact]
    public void Required_criterion_satisfied_and_optional_criteria_score_normally()
    {
        var criteriaSet = new CriteriaSet
        {
            Id = 1,
            Criteria =
            [
                new Criterion { Id = 1, AttributeName = "blacklisted", Operator = CriterionOperator.Equals, TargetValue = "false", Weight = 0, IsRequired = true },
                new Criterion { Id = 2, AttributeName = "tier", Operator = CriterionOperator.Equals, TargetValue = "gold", Weight = 10 },
            ],
        };
        var entity = EntityWith(("blacklisted", "false"), ("tier", "silver"));

        var result = ScoringEngine.Evaluate(entity, criteriaSet);

        Assert.Equal(0, result.TotalScore);
        Assert.True(result.Passed);
    }

    [Fact]
    public void Missing_attribute_produces_an_unsatisfied_detail_row_not_a_skipped_one()
    {
        // A criterion referencing an attribute the entity never collected
        // still needs a MatchResultDetail row -- "not satisfied because we
        // never got this data" is a different, actionable answer from
        // "this criterion silently didn't run".
        var criteriaSet = new CriteriaSet
        {
            Id = 1,
            Criteria = [new Criterion { Id = 1, AttributeName = "missing_attr", Operator = CriterionOperator.Equals, TargetValue = "x", Weight = 5 }],
        };
        var entity = EntityWith(("other_attr", "y"));

        var result = ScoringEngine.Evaluate(entity, criteriaSet);

        Assert.Single(result.Details);
        Assert.False(result.Details[0].Satisfied);
        Assert.Equal(0, result.TotalScore);
        Assert.Equal(5, result.MaxPossibleScore);
    }
}
