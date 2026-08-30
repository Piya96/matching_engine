using MatchingEngine.Domain.Entities;

namespace MatchingEngine.Core;

/// <summary>
/// The in-memory evaluation path: score one Entity against one CriteriaSet
/// entirely in C#, no database round-trip involved beyond whatever already
/// loaded the Entity's attributes and the CriteriaSet's criteria into
/// memory. This is the correctness-first, easy-to-unit-test path -- see
/// Batch/BatchScoreQueryBuilder.cs for the set-based SQL path this is
/// deliberately NOT trying to replace, and the README's "In-memory vs.
/// batch" section for when to reach for which.
/// </summary>
public static class ScoringEngine
{
    public static MatchResult Evaluate(Entity entity, CriteriaSet criteriaSet)
    {
        var attributesByName = entity.Attributes
            .GroupBy(a => a.AttributeName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().AttributeValue, StringComparer.Ordinal);

        var result = new MatchResult
        {
            EntityId = entity.Id,
            CriteriaSetId = criteriaSet.Id,
            Passed = true,
        };

        foreach (var criterion in criteriaSet.Criteria)
        {
            result.MaxPossibleScore += criterion.Weight;

            attributesByName.TryGetValue(criterion.AttributeName, out var attributeValue);
            var satisfied = CriterionEvaluator.Satisfied(attributeValue, criterion);
            var points = satisfied ? criterion.Weight : 0.0;

            result.TotalScore += points;
            result.Details.Add(new MatchResultDetail
            {
                CriterionId = criterion.Id,
                Satisfied = satisfied,
                PointsAwarded = points,
            });

            if (criterion.IsRequired && !satisfied)
            {
                // Keep scoring the remaining criteria even after a required
                // one fails -- the caller still wants the full breakdown
                // (MatchResultDetail rows) to explain *why* it failed, not
                // just that it did.
                result.Passed = false;
            }
        }

        return result;
    }

    public static IReadOnlyList<MatchResult> EvaluateAll(IEnumerable<Entity> entities, CriteriaSet criteriaSet) =>
        entities.Select(e => Evaluate(e, criteriaSet)).ToList();
}
