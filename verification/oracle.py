"""
Reference oracle for the matching engine's scoring semantics.

This is the ground truth. It is deliberately written in plain Python with no
framework magic, so that both the in-memory C# path (MatchingEngine.Core) and
the set-based T-SQL batch path (MatchingEngine.Core/Batch) can be checked
against exactly the same rules before either of them is trusted.

Why this exists at all: the sandbox this repo was built in has no .NET SDK
and no SQL Server, so the C# and T-SQL cannot be compiled or executed here.
Rather than ship unverified scoring logic, the actual *algorithm* -- operator
semantics, required-criterion gating, weighted scoring -- is nailed down and
tested here first, in a language that *can* run, and then translated
mechanically into C# and T-SQL. See sqlite_batch_verify.py for the companion
check that the set-based SQL aggregation pattern produces identical results
to this oracle, row for row.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum


class ValueType(str, Enum):
    STRING = "String"
    NUMBER = "Number"


class Operator(str, Enum):
    EQUALS = "Equals"
    NOT_EQUALS = "NotEquals"
    GREATER_THAN = "GreaterThan"
    GREATER_THAN_OR_EQUAL = "GreaterThanOrEqual"
    LESS_THAN = "LessThan"
    LESS_THAN_OR_EQUAL = "LessThanOrEqual"
    CONTAINS = "Contains"
    IN = "In"


_NUMERIC_OPERATORS = {
    Operator.GREATER_THAN,
    Operator.GREATER_THAN_OR_EQUAL,
    Operator.LESS_THAN,
    Operator.LESS_THAN_OR_EQUAL,
}


def try_cast_number(value: str | None) -> float | None:
    """Mirrors T-SQL's TRY_CAST(value AS FLOAT): a clean parse or NULL, never
    an exception and never a silent 0. This distinction matters -- see the
    note in sqlite_batch_verify.py about why SQLite's own CAST() is NOT a
    safe stand-in for this."""
    if value is None:
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


@dataclass
class Criterion:
    id: int
    attribute_name: str
    operator: Operator
    value_type: ValueType
    target_value: str | None = None
    target_list: list[str] = field(default_factory=list)
    weight: float = 1.0
    is_required: bool = False


@dataclass
class CriterionResult:
    criterion_id: int
    satisfied: bool
    points_awarded: float


@dataclass
class MatchResult:
    entity_id: int
    total_score: float
    max_possible_score: float
    passed: bool
    details: list[CriterionResult]


def evaluate_criterion(attr_value: str | None, criterion: Criterion) -> bool:
    """The single source of truth for operator semantics. A missing
    attribute (attr_value is None) never satisfies a criterion -- there is
    no three-valued "unknown" state here by design; see the README's
    "Decisions and trade-offs" section for why that's the safer default for
    a scoring engine (vs. real SQL NULL propagation, which would make
    NotEquals against a missing attribute evaluate as satisfied)."""
    if attr_value is None:
        return False

    op = criterion.operator

    if criterion.value_type == ValueType.NUMBER and op in _NUMERIC_OPERATORS | {
        Operator.EQUALS,
        Operator.NOT_EQUALS,
    }:
        left = try_cast_number(attr_value)
        right = try_cast_number(criterion.target_value)
        if left is None or right is None:
            return False
        if op == Operator.EQUALS:
            return left == right
        if op == Operator.NOT_EQUALS:
            return left != right
        if op == Operator.GREATER_THAN:
            return left > right
        if op == Operator.GREATER_THAN_OR_EQUAL:
            return left >= right
        if op == Operator.LESS_THAN:
            return left < right
        if op == Operator.LESS_THAN_OR_EQUAL:
            return left <= right

    if op in _NUMERIC_OPERATORS:
        # Numeric comparison operator against a String-typed attribute: not
        # a supported combination. Authoring-time validation should catch
        # this before it reaches evaluation; at evaluation time we fail
        # closed (not satisfied) rather than raise, so one bad criterion in
        # a set doesn't take down the whole batch.
        return False

    if op == Operator.EQUALS:
        return attr_value.strip().lower() == (criterion.target_value or "").strip().lower()

    if op == Operator.NOT_EQUALS:
        return attr_value.strip().lower() != (criterion.target_value or "").strip().lower()

    if op == Operator.CONTAINS:
        return (criterion.target_value or "").strip().lower() in attr_value.strip().lower()

    if op == Operator.IN:
        needle = attr_value.strip().lower()
        return needle in {v.strip().lower() for v in criterion.target_list}

    raise ValueError(f"Unhandled operator: {op}")  # pragma: no cover


def score_entity(attributes: dict[str, str], criteria: list[Criterion], entity_id: int = 0) -> MatchResult:
    """Weighted scoring with hard-gate required criteria: TotalScore sums
    the weight of every satisfied criterion (required or not); Passed is
    false if *any* required criterion is unsatisfied, regardless of how
    high the resulting score is. This is the "hard filter + soft score"
    shape most eligibility/matching engines actually want -- required
    criteria disqualify, optional criteria rank."""
    details: list[CriterionResult] = []
    total_score = 0.0
    max_possible = 0.0
    passed = True

    for c in criteria:
        max_possible += c.weight
        satisfied = evaluate_criterion(attributes.get(c.attribute_name), c)
        points = c.weight if satisfied else 0.0
        total_score += points
        details.append(CriterionResult(c.id, satisfied, points))
        if c.is_required and not satisfied:
            passed = False

    return MatchResult(entity_id, total_score, max_possible, passed, details)
