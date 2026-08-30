using MatchingEngine.Core;
using MatchingEngine.Domain.Entities;
using MatchingEngine.Domain.Enums;

// Everything below runs entirely in memory -- no SQL Server, no connection
// string, nothing to stand up first. This is deliberately the one part of
// this repo you can `dotnet run` with nothing but the SDK: it exercises the
// exact same ScoringEngine/CriterionEvaluator that MatchingEngine.Data and
// the batch SQL path also use, just fed hand-built objects instead of rows
// from a database.

var criteriaSet = new CriteriaSet
{
    Id = 1,
    Name = "SupplierEligibility",
    Version = 1,
    Criteria =
    [
        new Criterion
        {
            Id = 1, AttributeName = "certification_tier", Operator = CriterionOperator.Equals,
            ValueType = AttributeValueType.String, TargetValue = "Gold", Weight = 3,
        },
        new Criterion
        {
            Id = 2, AttributeName = "annual_volume", Operator = CriterionOperator.GreaterThanOrEqual,
            ValueType = AttributeValueType.Number, TargetValue = "1000", Weight = 2,
        },
        new Criterion
        {
            Id = 3, AttributeName = "region", Operator = CriterionOperator.In,
            ValueType = AttributeValueType.String, TargetValueList = ["NL", "DE", "BE"], Weight = 2,
        },
        new Criterion
        {
            Id = 4, AttributeName = "notes", Operator = CriterionOperator.Contains,
            ValueType = AttributeValueType.String, TargetValue = "priority", Weight = 1,
        },
        new Criterion
        {
            // Weight 0 + IsRequired -- a hard gate that contributes nothing
            // to the score but can still fail the whole match.
            Id = 5, AttributeName = "blacklisted", Operator = CriterionOperator.Equals,
            ValueType = AttributeValueType.String, TargetValue = "false", Weight = 0, IsRequired = true,
        },
    ],
};

var entities = new List<Entity>
{
    Entity("SUP-001", ("certification_tier", "Gold"), ("annual_volume", "1500"),
        ("region", "NL"), ("notes", "high priority account"), ("blacklisted", "false")),
    Entity("SUP-002", ("certification_tier", "Silver"), ("annual_volume", "2200"),
        ("region", "DE"), ("notes", "standard account"), ("blacklisted", "false")),
    Entity("SUP-003", ("certification_tier", "Gold"), ("annual_volume", "5000"),
        ("region", "FR"), ("notes", "priority review pending"), ("blacklisted", "true")),
    Entity("SUP-004", ("certification_tier", "Gold"), ("annual_volume", "not_collected"),
        ("region", "BE"), ("blacklisted", "false")), // missing annual_volume, missing notes
};

Console.WriteLine($"Scoring {entities.Count} entities against '{criteriaSet.Name}' v{criteriaSet.Version}"
                   + $" ({criteriaSet.Criteria.Count} criteria)\n");

foreach (var entity in entities)
{
    var result = ScoringEngine.Evaluate(entity, criteriaSet);
    Console.WriteLine($"{entity.ExternalRef}: {result.TotalScore}/{result.MaxPossibleScore}"
                       + $"  Passed={result.Passed}");

    foreach (var detail in result.Details)
    {
        var criterion = criteriaSet.Criteria.Single(c => c.Id == detail.CriterionId);
        var mark = detail.Satisfied ? "PASS" : "fail";
        var required = criterion.IsRequired ? " [required]" : "";
        Console.WriteLine($"    [{mark}] {criterion.AttributeName} {criterion.Operator}"
                           + $" -> +{detail.PointsAwarded}{required}");
    }

    Console.WriteLine();
}

return;

static Entity Entity(string externalRef, params (string Name, string Value)[] attrs) => new()
{
    ExternalRef = externalRef,
    EntityType = "Supplier",
    Attributes = attrs.Select(a => new EntityAttribute
    {
        AttributeName = a.Name,
        AttributeValue = a.Value,
    }).ToList(),
};
