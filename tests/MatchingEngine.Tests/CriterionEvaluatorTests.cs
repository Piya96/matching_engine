using MatchingEngine.Domain.Entities;
using MatchingEngine.Domain.Enums;
using Xunit;

namespace MatchingEngine.Core.Tests;

// One-to-one with verification/test_oracle.py -- same case names, same
// inputs, so a divergence between the C# and the Python oracle is easy to
// spot by diffing test names, not just by staring at assertion failures.
public class CriterionEvaluatorTests
{
    private static Criterion Crit(
        CriterionOperator op = CriterionOperator.Equals,
        AttributeValueType valueType = AttributeValueType.String,
        string? targetValue = null,
        List<string>? targetList = null,
        double weight = 1.0,
        bool isRequired = false) => new()
    {
        Id = 1,
        AttributeName = "attr",
        Operator = op,
        ValueType = valueType,
        TargetValue = targetValue,
        TargetValueList = targetList ?? [],
        Weight = weight,
        IsRequired = isRequired,
    };

    [Fact]
    public void Equals_string_is_case_insensitive()
    {
        var c = Crit(CriterionOperator.Equals, AttributeValueType.String, "Gold");
        Assert.True(CriterionEvaluator.Satisfied("gold", c));
        Assert.True(CriterionEvaluator.Satisfied("GOLD ", c));
        Assert.False(CriterionEvaluator.Satisfied("silver", c));
    }

    [Fact]
    public void NotEquals_string()
    {
        var c = Crit(CriterionOperator.NotEquals, AttributeValueType.String, "gold");
        Assert.True(CriterionEvaluator.Satisfied("silver", c));
        Assert.False(CriterionEvaluator.Satisfied("gold", c));
    }

    [Fact]
    public void Contains_is_case_insensitive_substring()
    {
        var c = Crit(CriterionOperator.Contains, AttributeValueType.String, "rej");
        Assert.True(CriterionEvaluator.Satisfied("Pre-Rejection Batch", c));
        Assert.False(CriterionEvaluator.Satisfied("Approved Batch", c));
    }

    [Fact]
    public void In_membership()
    {
        var c = Crit(CriterionOperator.In, targetList: ["NL", "DE", "BE"]);
        Assert.True(CriterionEvaluator.Satisfied("de", c));
        Assert.False(CriterionEvaluator.Satisfied("FR", c));
    }

    [Fact]
    public void Numeric_equals()
    {
        var c = Crit(CriterionOperator.Equals, AttributeValueType.Number, "42");
        Assert.True(CriterionEvaluator.Satisfied("42.0", c));
        Assert.False(CriterionEvaluator.Satisfied("42.01", c));
    }

    [Fact]
    public void GreaterThanOrEqual()
    {
        var c = Crit(CriterionOperator.GreaterThanOrEqual, AttributeValueType.Number, "90");
        Assert.True(CriterionEvaluator.Satisfied("90", c));
        Assert.True(CriterionEvaluator.Satisfied("90.5", c));
        Assert.False(CriterionEvaluator.Satisfied("89.9", c));
    }

    [Fact]
    public void LessThan()
    {
        var c = Crit(CriterionOperator.LessThan, AttributeValueType.Number, "5");
        Assert.True(CriterionEvaluator.Satisfied("4.999", c));
        Assert.False(CriterionEvaluator.Satisfied("5", c));
    }

    [Fact]
    public void Non_numeric_value_against_numeric_operator_fails_closed_not_exception()
    {
        var c = Crit(CriterionOperator.GreaterThan, AttributeValueType.Number, "10");
        Assert.False(CriterionEvaluator.Satisfied("n/a", c));
    }

    [Fact]
    public void Numeric_operator_against_string_value_type_fails_closed()
    {
        var c = Crit(CriterionOperator.GreaterThan, AttributeValueType.String, "10");
        Assert.False(CriterionEvaluator.Satisfied("99", c));
    }

    [Theory]
    [InlineData(CriterionOperator.Equals, AttributeValueType.String)]
    [InlineData(CriterionOperator.NotEquals, AttributeValueType.String)]
    [InlineData(CriterionOperator.Contains, AttributeValueType.String)]
    [InlineData(CriterionOperator.In, AttributeValueType.String)]
    [InlineData(CriterionOperator.Equals, AttributeValueType.Number)]
    [InlineData(CriterionOperator.GreaterThan, AttributeValueType.Number)]
    public void Missing_attribute_never_satisfies_any_operator(CriterionOperator op, AttributeValueType valueType)
    {
        var c = Crit(op, valueType, targetValue: "x", targetList: ["x"]);
        Assert.False(CriterionEvaluator.Satisfied(null, c));
    }
}
