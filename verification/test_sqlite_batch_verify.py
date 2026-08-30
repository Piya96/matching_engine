"""
Verifies the SET-BASED batch scoring pattern -- the one meant to run as a
single T-SQL query over SQL Server rather than row-by-row in C# -- against
the oracle in oracle.py, using SQLite as a stand-in database engine.

Why SQLite as the stand-in: no SQL Server / Docker daemon is available in
this sandbox. SQLite can't run the real T-SQL (no TRY_CAST built-in, no
STRING_SPLIT), but it can run the same *shape* of query -- a CROSS JOIN
entities x criteria, LEFT JOIN their attribute values, one big per-operator
CASE expression, aggregated with SUM/MIN GROUP BY entity -- which is exactly
the part of the design that's actually risky. If that shape produces correct
scores here, the SQL Server translation (see README "Translating this to
SQL Server") is mechanical.

This file also documents a real bug this verification step caught: see
`test_naive_cast_is_wrong_try_cast_is_right` below.

Run with: pytest verification/
"""
from __future__ import annotations

import json
import re
import sqlite3

from oracle import Criterion, Operator, ValueType, score_entity

_NUMERIC_RE = re.compile(r"^\s*[-+]?\d+(\.\d+)?\s*$")


def _is_num(value: str | None) -> int:
    """The SQLite UDF standing in for "would TRY_CAST(value AS FLOAT)
    succeed?". SQLite's own CAST() does NOT raise or return NULL on a bad
    numeric string -- CAST('n/a' AS REAL) silently returns 0.0. That is the
    opposite of TRY_CAST's contract (NULL on failure) and, left unguarded,
    turns "attribute missing/garbage" into "attribute equals zero" -- see
    the test below for a case where that actually flips the answer."""
    if value is None:
        return 0
    return 1 if _NUMERIC_RE.match(value) else 0


def _build_db(entities: dict[int, dict[str, str]], criteria: list[Criterion]) -> sqlite3.Connection:
    conn = sqlite3.connect(":memory:")
    conn.create_function("is_num", 1, _is_num)
    conn.executescript(
        """
        CREATE TABLE entities (id INTEGER PRIMARY KEY);
        CREATE TABLE entity_attributes (
            entity_id INTEGER, attribute_name TEXT, attribute_value TEXT
        );
        CREATE TABLE criteria (
            id INTEGER PRIMARY KEY, attribute_name TEXT, operator TEXT,
            value_type TEXT, target_value TEXT, target_value_list TEXT,
            weight REAL, is_required INTEGER
        );
        """
    )
    for eid, attrs in entities.items():
        conn.execute("INSERT INTO entities (id) VALUES (?)", (eid,))
        conn.executemany(
            "INSERT INTO entity_attributes VALUES (?, ?, ?)",
            [(eid, k, v) for k, v in attrs.items()],
        )
    conn.executemany(
        "INSERT INTO criteria VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
        [
            (
                c.id, c.attribute_name, c.operator.value, c.value_type.value,
                c.target_value, json.dumps(c.target_list) if c.target_list else None,
                c.weight, int(c.is_required),
            )
            for c in criteria
        ],
    )
    conn.commit()
    return conn


# The query under test: the set-based aggregation pattern this repo's batch
# scorer generates (see MatchingEngine.Core/Batch), translated to SQLite
# syntax for verification. TRY_CAST -> "is_num(x) AND CAST(x AS REAL)",
# STRING_SPLIT -> json_each, CHARINDEX -> INSTR. See README for the T-SQL
# these map back to.
_GUARDED_QUERY = """
WITH satisfaction AS (
    SELECT
        e.id AS entity_id,
        c.weight AS weight,
        c.is_required AS is_required,
        CASE
            WHEN ea.attribute_value IS NULL THEN 0

            WHEN c.operator = 'Equals' AND c.value_type = 'Number' THEN
                CASE WHEN is_num(ea.attribute_value) AND is_num(c.target_value)
                          AND CAST(ea.attribute_value AS REAL) = CAST(c.target_value AS REAL)
                     THEN 1 ELSE 0 END
            WHEN c.operator = 'Equals' THEN
                CASE WHEN LOWER(TRIM(ea.attribute_value)) = LOWER(TRIM(c.target_value)) THEN 1 ELSE 0 END

            WHEN c.operator = 'NotEquals' AND c.value_type = 'Number' THEN
                CASE WHEN is_num(ea.attribute_value) AND is_num(c.target_value)
                          AND CAST(ea.attribute_value AS REAL) <> CAST(c.target_value AS REAL)
                     THEN 1 ELSE 0 END
            WHEN c.operator = 'NotEquals' THEN
                CASE WHEN LOWER(TRIM(ea.attribute_value)) <> LOWER(TRIM(c.target_value)) THEN 1 ELSE 0 END

            WHEN c.operator IN ('GreaterThan', 'GreaterThanOrEqual', 'LessThan', 'LessThanOrEqual') THEN
                CASE WHEN c.value_type = 'Number' AND is_num(ea.attribute_value) AND is_num(c.target_value) THEN
                    CASE c.operator
                        WHEN 'GreaterThan' THEN
                            CASE WHEN CAST(ea.attribute_value AS REAL) > CAST(c.target_value AS REAL) THEN 1 ELSE 0 END
                        WHEN 'GreaterThanOrEqual' THEN
                            CASE WHEN CAST(ea.attribute_value AS REAL) >= CAST(c.target_value AS REAL) THEN 1 ELSE 0 END
                        WHEN 'LessThan' THEN
                            CASE WHEN CAST(ea.attribute_value AS REAL) < CAST(c.target_value AS REAL) THEN 1 ELSE 0 END
                        WHEN 'LessThanOrEqual' THEN
                            CASE WHEN CAST(ea.attribute_value AS REAL) <= CAST(c.target_value AS REAL) THEN 1 ELSE 0 END
                    END
                ELSE 0 END

            WHEN c.operator = 'Contains' THEN
                CASE WHEN INSTR(LOWER(ea.attribute_value), LOWER(c.target_value)) > 0 THEN 1 ELSE 0 END

            WHEN c.operator = 'In' THEN
                CASE WHEN EXISTS (
                    SELECT 1 FROM json_each(c.target_value_list) je
                    WHERE LOWER(TRIM(je.value)) = LOWER(TRIM(ea.attribute_value))
                ) THEN 1 ELSE 0 END

            ELSE 0
        END AS satisfied
    FROM entities e
    CROSS JOIN criteria c
    LEFT JOIN entity_attributes ea
        ON ea.entity_id = e.id AND ea.attribute_name = c.attribute_name
)
SELECT
    entity_id,
    SUM(CASE WHEN satisfied = 1 THEN weight ELSE 0 END) AS total_score,
    SUM(weight) AS max_possible_score,
    MIN(CASE WHEN is_required = 1 AND satisfied = 0 THEN 0 ELSE 1 END) AS passed
FROM satisfaction
GROUP BY entity_id
ORDER BY entity_id;
"""

# The same query with the is_num() guard removed from the Equals/Number
# branch only -- i.e. what you'd get if you translated TRY_CAST as plain
# CAST without noticing the semantic difference. Kept only to prove the
# guard is load-bearing, not to ship.
_NAIVE_QUERY = _GUARDED_QUERY.replace(
    "CASE WHEN is_num(ea.attribute_value) AND is_num(c.target_value)\n"
    "                          AND CAST(ea.attribute_value AS REAL) = CAST(c.target_value AS REAL)\n"
    "                     THEN 1 ELSE 0 END",
    "CASE WHEN CAST(ea.attribute_value AS REAL) = CAST(c.target_value AS REAL) THEN 1 ELSE 0 END",
)
assert _NAIVE_QUERY != _GUARDED_QUERY, "the naive-query patch didn't match -- update the string above"


def run_batch_query(entities, criteria, query=_GUARDED_QUERY):
    conn = _build_db(entities, criteria)
    rows = conn.execute(query).fetchall()
    conn.close()
    return {r[0]: {"total_score": r[1], "max_possible_score": r[2], "passed": bool(r[3])} for r in rows}


def _oracle_results(entities, criteria):
    out = {}
    for eid, attrs in entities.items():
        r = score_entity(attrs, criteria, entity_id=eid)
        out[eid] = {"total_score": r.total_score, "max_possible_score": r.max_possible_score, "passed": r.passed}
    return out


# ---------------------------------------------------------------------------

def test_guarded_batch_query_matches_oracle_across_varied_entities_and_operators():
    criteria = [
        Criterion(1, "tier", Operator.EQUALS, ValueType.STRING, target_value="Gold", weight=3),
        Criterion(2, "annual_volume", Operator.GREATER_THAN_OR_EQUAL, ValueType.NUMBER, target_value="1000", weight=2),
        Criterion(3, "region", Operator.IN, ValueType.STRING, target_list=["NL", "DE", "BE"], weight=2),
        Criterion(4, "notes", Operator.CONTAINS, ValueType.STRING, target_value="priority", weight=1),
        Criterion(5, "blacklisted", Operator.EQUALS, ValueType.STRING, target_value="false", weight=0, is_required=True),
        Criterion(6, "risk_score", Operator.LESS_THAN, ValueType.NUMBER, target_value="50", weight=4),
    ]
    entities = {
        1: {"tier": "gold", "annual_volume": "1500", "region": "NL", "notes": "high priority account",
            "blacklisted": "false", "risk_score": "12"},
        2: {"tier": "silver", "annual_volume": "999", "region": "FR", "notes": "standard",
            "blacklisted": "false", "risk_score": "80"},
        3: {"tier": "gold", "annual_volume": "5000", "region": "DE", "notes": "n/a",
            "blacklisted": "true", "risk_score": "5"},
        4: {"tier": "Gold", "annual_volume": "not_collected", "region": "BE", "notes": "priority flagged",
            "blacklisted": "false", "risk_score": "abc"},
        # entity 5 is missing several attributes entirely -- exercises the
        # LEFT JOIN producing NULL, not just a bad string value
        5: {"tier": "gold"},
    }

    expected = _oracle_results(entities, criteria)
    actual = run_batch_query(entities, criteria)

    assert actual == expected, f"batch SQL diverged from oracle:\nexpected={expected}\nactual={actual}"


def test_guarded_batch_query_matches_oracle_on_randomized_dataset():
    import random

    rng = random.Random(2026)
    criteria = [
        Criterion(1, "score", Operator.GREATER_THAN, ValueType.NUMBER, target_value="50", weight=5),
        Criterion(2, "status", Operator.NOT_EQUALS, ValueType.STRING, target_value="inactive", weight=3, is_required=True),
        Criterion(3, "code", Operator.IN, ValueType.STRING, target_list=["A1", "B2", "C3"], weight=2),
    ]
    garbage = ["", "n/a", "unknown", "TBD", "--"]
    entities = {}
    for eid in range(1, 51):
        attrs = {}
        if rng.random() > 0.1:
            attrs["score"] = rng.choice([str(rng.randint(0, 100)), rng.choice(garbage)])
        if rng.random() > 0.1:
            attrs["status"] = rng.choice(["active", "inactive", "Active", "pending"])
        if rng.random() > 0.1:
            attrs["code"] = rng.choice(["A1", "b2", "Z9", "c3"])
        entities[eid] = attrs

    expected = _oracle_results(entities, criteria)
    actual = run_batch_query(entities, criteria)
    assert actual == expected


def test_naive_cast_is_wrong_try_cast_is_right():
    """The bug this verification step exists to catch. SQLite's CAST('n/a'
    AS REAL) is 0.0, not an error and not NULL. So an Equals/Number
    criterion comparing a garbage attribute value against target "0" comes
    out *satisfied* under a naive CAST-only translation of TRY_CAST -- two
    wrongs (garbage cast to 0.0, "0" cast to 0.0) making a false right.
    The oracle says False (missing/garbage data never satisfies a
    criterion); the guarded query agrees; the naive query does not."""
    criteria = [Criterion(1, "balance", Operator.EQUALS, ValueType.NUMBER, target_value="0", weight=1)]
    entities = {1: {"balance": "n/a"}}

    expected = _oracle_results(entities, criteria)
    assert expected[1]["total_score"] == 0, "sanity check on the oracle itself"

    guarded = run_batch_query(entities, criteria, query=_GUARDED_QUERY)
    naive = run_batch_query(entities, criteria, query=_NAIVE_QUERY)

    assert guarded == expected, "guarded (TRY_CAST-emulating) query should match the oracle"
    assert naive != expected, (
        "expected the naive CAST-only query to DISAGREE with the oracle here -- "
        "if this assertion fails, SQLite's CAST() semantics changed, re-check this test"
    )
    assert naive[1]["total_score"] == 1, "naive query incorrectly scores the garbage value as matching 0"
