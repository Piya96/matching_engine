namespace MatchingEngine.Domain.Enums;

/// <summary>
/// The declared type of an attribute value. Deliberately not named
/// "ValueType" -- that's a real BCL type (System.ValueType) and shadowing
/// it invites exactly the kind of confusing bug this repo's README calls
/// out elsewhere (see the EAV notes on why attribute values are stored as
/// strings and typed out-of-band).
///
/// Only two members by design. Date, Boolean, etc. are real needs in a
/// production version of this engine, but each one adds its own casting
/// and comparison rules to both the in-memory evaluator and the batch SQL
/// -- see "What I'd do differently" in the README for how this is meant to
/// extend, and why it wasn't done here without a compiler or a database to
/// verify the extra cases against.
/// </summary>
public enum AttributeValueType
{
    String = 0,
    Number = 1,
}
