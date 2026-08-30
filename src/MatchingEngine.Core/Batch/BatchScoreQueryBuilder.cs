using Microsoft.Data.SqlClient;

namespace MatchingEngine.Core.Batch;

/// <summary>
/// One entity, one criterion, one call to <see cref="ScoringEngine"/>: fine
/// for a single applicant landing on a "check my eligibility" page, ruinous
/// for "score all 400,000 entities against this rule set tonight" -- that's
/// N entities x M criteria round trips of attribute-lookup dictionary
/// building, or N+1 queries if the attributes aren't already loaded. This
/// class produces one SQL Server query that scores every entity against
/// every criterion in a CriteriaSet in a single set-based pass: CROSS JOIN
/// entities against criteria, LEFT JOIN each entity's value for that
/// criterion's attribute, one big per-operator CASE, then SUM/MIN GROUP BY
/// entity. See verification/test_sqlite_batch_verify.py for where this
/// exact shape of query was proven correct against the same oracle
/// CriterionEvaluator implements -- SQLite doesn't speak T-SQL, so this
/// file's SQL text and that test's SQL text differ in dialect (TRY_CAST vs.
/// a guarded CAST, OPENJSON vs. json_each, CHARINDEX vs. INSTR) but encode
/// the identical CASE-per-operator logic. There's no compiler or SQL
/// Server in this sandbox to run this exact string against, which is why
/// it's kept this close, line for line, to the query that already passed.
///
/// Trade-off this path makes on purpose: it returns only the aggregate
/// (TotalScore, MaxPossibleScore, Passed) per entity, not a
/// MatchResultDetail row per criterion -- the per-criterion "why" that
/// ScoringEngine produces. Getting that breakdown at batch scale means
/// either re-running ScoringEngine for just the entities this pass flagged
/// (small set, in-memory path is fine again), or extending the query below
/// to also project per-criterion Satisfied flags and unpivoting them --
/// more expensive, and not worth it until something actually needs the
/// breakdown for every one of 400,000 entities, not just the ones a human
/// will look at.
/// </summary>
public static class BatchScoreQueryBuilder
{
    public const string Sql = """
        WITH Satisfaction AS (
            SELECT
                e.Id AS EntityId,
                c.Weight AS Weight,
                c.IsRequired AS IsRequired,
                CASE
                    WHEN ea.AttributeValue IS NULL THEN 0

                    WHEN c.Operator = 'Equals' AND c.ValueType = 'Number' THEN
                        CASE WHEN TRY_CAST(ea.AttributeValue AS FLOAT) IS NOT NULL
                                  AND TRY_CAST(c.TargetValue AS FLOAT) IS NOT NULL
                                  AND TRY_CAST(ea.AttributeValue AS FLOAT) = TRY_CAST(c.TargetValue AS FLOAT)
                             THEN 1 ELSE 0 END
                    WHEN c.Operator = 'Equals' THEN
                        -- NVARCHAR comparison is case-insensitive under the default
                        -- SQL_Latin1_General_CP1_CI_AS collation -- no LOWER() needed
                        -- here the way the SQLite verification query needed it.
                        -- If your database uses a case-sensitive (_CS) collation,
                        -- wrap both sides in LOWER().
                        CASE WHEN LTRIM(RTRIM(ea.AttributeValue)) = LTRIM(RTRIM(c.TargetValue)) THEN 1 ELSE 0 END

                    WHEN c.Operator = 'NotEquals' AND c.ValueType = 'Number' THEN
                        CASE WHEN TRY_CAST(ea.AttributeValue AS FLOAT) IS NOT NULL
                                  AND TRY_CAST(c.TargetValue AS FLOAT) IS NOT NULL
                                  AND TRY_CAST(ea.AttributeValue AS FLOAT) <> TRY_CAST(c.TargetValue AS FLOAT)
                             THEN 1 ELSE 0 END
                    WHEN c.Operator = 'NotEquals' THEN
                        CASE WHEN LTRIM(RTRIM(ea.AttributeValue)) <> LTRIM(RTRIM(c.TargetValue)) THEN 1 ELSE 0 END

                    WHEN c.Operator IN ('GreaterThan', 'GreaterThanOrEqual', 'LessThan', 'LessThanOrEqual') THEN
                        CASE WHEN c.ValueType = 'Number'
                                  AND TRY_CAST(ea.AttributeValue AS FLOAT) IS NOT NULL
                                  AND TRY_CAST(c.TargetValue AS FLOAT) IS NOT NULL THEN
                            CASE c.Operator
                                WHEN 'GreaterThan' THEN
                                    CASE WHEN TRY_CAST(ea.AttributeValue AS FLOAT) > TRY_CAST(c.TargetValue AS FLOAT) THEN 1 ELSE 0 END
                                WHEN 'GreaterThanOrEqual' THEN
                                    CASE WHEN TRY_CAST(ea.AttributeValue AS FLOAT) >= TRY_CAST(c.TargetValue AS FLOAT) THEN 1 ELSE 0 END
                                WHEN 'LessThan' THEN
                                    CASE WHEN TRY_CAST(ea.AttributeValue AS FLOAT) < TRY_CAST(c.TargetValue AS FLOAT) THEN 1 ELSE 0 END
                                WHEN 'LessThanOrEqual' THEN
                                    CASE WHEN TRY_CAST(ea.AttributeValue AS FLOAT) <= TRY_CAST(c.TargetValue AS FLOAT) THEN 1 ELSE 0 END
                            END
                        ELSE 0 END

                    WHEN c.Operator = 'Contains' THEN
                        CASE WHEN CHARINDEX(c.TargetValue, ea.AttributeValue) > 0 THEN 1 ELSE 0 END

                    WHEN c.Operator = 'In' THEN
                        CASE WHEN EXISTS (
                            SELECT 1 FROM OPENJSON(c.TargetValueList) j
                            WHERE LTRIM(RTRIM(j.value)) = LTRIM(RTRIM(ea.AttributeValue))
                        ) THEN 1 ELSE 0 END

                    ELSE 0
                END AS Satisfied
            FROM Entities e
            CROSS JOIN Criteria c
            LEFT JOIN EntityAttributes ea
                ON ea.EntityId = e.Id AND ea.AttributeName = c.AttributeName
            WHERE c.CriteriaSetId = @CriteriaSetId
        )
        SELECT
            EntityId,
            SUM(CASE WHEN Satisfied = 1 THEN Weight ELSE 0 END) AS TotalScore,
            SUM(Weight) AS MaxPossibleScore,
            CAST(MIN(CASE WHEN IsRequired = 1 AND Satisfied = 0 THEN 0 ELSE 1 END) AS BIT) AS Passed
        FROM Satisfaction
        GROUP BY EntityId
        ORDER BY EntityId;
        """;

    public record BatchMatchResult(int EntityId, double TotalScore, double MaxPossibleScore, bool Passed);

    /// <summary>
    /// Runs the query directly against a SqlConnection rather than through
    /// the DbContext -- this is exactly the kind of dynamic, data-driven
    /// predicate (which CASE branch fires depends on a row's own Operator
    /// column) that LINQ-to-Entities cannot translate, so
    /// FromSqlInterpolated/raw ADO.NET is the honest escape hatch rather
    /// than fighting the LINQ provider. @CriteriaSetId is still a real
    /// parameter, not string-interpolated -- the CriteriaSetId is the only
    /// caller-supplied value in this query, everything else is schema.
    /// </summary>
    public static async Task<IReadOnlyList<BatchMatchResult>> RunAsync(
        string connectionString, int criteriaSetId, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(Sql, connection);
        command.Parameters.AddWithValue("@CriteriaSetId", criteriaSetId);

        var results = new List<BatchMatchResult>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new BatchMatchResult(
                reader.GetInt32(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetBoolean(3)));
        }

        return results;
    }
}
