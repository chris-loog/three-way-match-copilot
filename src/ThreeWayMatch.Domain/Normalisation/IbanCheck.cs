namespace ThreeWayMatch.Domain.Normalisation;

public enum IbanStatus
{
    /// <summary>Right shape and the mod-97 check passes.</summary>
    Valid,

    /// <summary>Right shape but the check digits do not add up: most likely a misread character, not a different account.</summary>
    InvalidChecksum,

    /// <summary>Not shaped like an IBAN at all (wrong length, letters where digits belong, missing).</summary>
    Malformed,
}

/// <summary>The result of looking at IBAN text. <see cref="Normalised"/> has no spaces and is upper case; it is null when nothing IBAN-like was found.</summary>
public sealed record IbanCheck(IbanStatus Status, string? Normalised);
