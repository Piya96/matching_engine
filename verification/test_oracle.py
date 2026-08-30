"""Unit tests for oracle.py -- the ground truth this whole repo's scoring
logic is checked against. Run with: pytest verification/"""
from oracle import Criterion, Operator, ValueType, evaluate_criterion, score_entity


def crit(**kwargs) -> Criterion:
    defaults = dict(id=1, attribute_name="attr", operator=Operator.EQUALS,
                     value_type=ValueType.STRING, weight=1.0, is_required=False)
    defaults.update(kwargs)
    return Criterion(**defaults)


# ---- string operators ----------------------------------------------------

def test_equals_string_case_insensitive():
    c = crit(operator=Operator.EQUALS, value_type=ValueType.STRING, target_value="Gold")
    assert evaluate_criterion("gold", c) is True
    assert evaluate_criterion("GOLD ", c) is True
    assert evaluate_criterion("silver", c) is False


def test_not_equals_string():
    c = crit(operator=Operator.NOT_EQUALS, value_type=ValueType.STRING, target_value="gold")
    assert evaluate_criterion("silver", c) is True
    assert evaluate_criterion("gold", c) is False


def test_contains_case_insensitive_substring():
    c = crit(operator=Operator.CONTAINS, value_type=ValueType.STRING, target_value="rej")
    assert evaluate_criterion("Pre-Rejection Batch", c) is True
    assert evaluate_criterion("Approved Batch", c) is False


def test_in_membership():
    c = crit(operator=Operator.IN, value_type=ValueType.STRING, target_list=["NL", "DE", "BE"])
    assert evaluate_criterion("de", c) is True
    assert evaluate_criterion("FR", c) is False


# ---- numeric operators ----------------------------------------------------

def test_numeric_equals():
    c = crit(operator=Operator.EQUALS, value_type=ValueType.NUMBER, target_value="42")
    assert evaluate_criterion("42.0", c) is True
    assert evaluate_criterion("42.01", c) is False


def test_greater_than_or_equal():
    c = crit(operator=Operator.GREATER_THAN_OR_EQUAL, value_type=ValueType.NUMBER, target_value="90")
    assert evaluate_criterion("90", c) is True
    assert evaluate_criterion("90.5", c) is True
    assert evaluate_criterion("89.9", c) is False


def test_less_than():
    c = crit(operator=Operator.LESS_THAN, value_type=ValueType.NUMBER, target_value="5")
    assert evaluate_criterion("4.999", c) is True
    assert evaluate_criterion("5", c) is False


# ---- edge cases: this is the part a naive CAST() implementation gets wrong

def test_non_numeric_value_against_numeric_operator_fails_closed_not_exception():
    c = crit(operator=Operator.GREATER_THAN, value_type=ValueType.NUMBER, target_value="10")
    # attribute value is garbage / not collected for this entity -- must not
    # raise, and must not silently compare as if it were 0.
    assert evaluate_criterion("n/a", c) is False


def test_numeric_operator_against_string_value_type_fails_closed():
    # authoring mistake: GreaterThan paired with a String-typed attribute.
    # Must fail closed at evaluation time rather than throw.
    c = crit(operator=Operator.GREATER_THAN, value_type=ValueType.STRING, target_value="10")
    assert evaluate_criterion("99", c) is False


def test_missing_attribute_never_satisfies_any_operator():
    for op, vt in [
        (Operator.EQUALS, ValueType.STRING), (Operator.NOT_EQUALS, ValueType.STRING),
        (Operator.CONTAINS, ValueType.STRING), (Operator.IN, ValueType.STRING),
        (Operator.EQUALS, ValueType.NUMBER), (Operator.GREATER_THAN, ValueType.NUMBER),
    ]:
        c = crit(operator=op, value_type=vt, target_value="x", target_list=["x"])
        assert evaluate_criterion(None, c) is False, f"{op}/{vt} should fail closed on missing attribute"


# ---- scoring: weights, required gating -----------------------------------

def test_score_sums_weights_of_satisfied_criteria_only():
    criteria = [
        crit(id=1, attribute_name="tier", operator=Operator.EQUALS, target_value="gold", weight=3),
        crit(id=2, attribute_name="score", operator=Operator.GREATER_THAN,
             value_type=ValueType.NUMBER, target_value="80", weight=2),
        crit(id=3, attribute_name="region", operator=Operator.IN, target_list=["NL", "DE"], weight=1),
    ]
    result = score_entity({"tier": "gold", "score": "75", "region": "NL"}, criteria)
    assert result.total_score == 4  # tier (3) + region (1), not score
    assert result.max_possible_score == 6
    assert [d.satisfied for d in result.details] == [True, False, True]


def test_required_criterion_failing_fails_the_whole_match_regardless_of_score():
    criteria = [
        crit(id=1, attribute_name="blacklisted", operator=Operator.EQUALS,
             target_value="false", weight=0, is_required=True),
        crit(id=2, attribute_name="tier", operator=Operator.EQUALS, target_value="gold", weight=10),
    ]
    # scores maximally on the optional criterion but is blacklisted
    result = score_entity({"blacklisted": "true", "tier": "gold"}, criteria)
    assert result.total_score == 10
    assert result.passed is False


def test_required_criterion_satisfied_and_optional_criteria_score_normally():
    criteria = [
        crit(id=1, attribute_name="blacklisted", operator=Operator.EQUALS,
             target_value="false", weight=0, is_required=True),
        crit(id=2, attribute_name="tier", operator=Operator.EQUALS, target_value="gold", weight=10),
    ]
    result = score_entity({"blacklisted": "false", "tier": "silver"}, criteria)
    assert result.total_score == 0
    assert result.passed is True  # required criterion satisfied; just scores low
