namespace ThreeWayMatch.Domain.Extraction;

/// <summary>
/// One value read from a document: the typed value Document Intelligence returned, the text it was read from,
/// and how sure the model is (0 to 1). Normalisation (step 3.2) parses <see cref="RawText"/> itself and compares.
/// </summary>
public sealed record ExtractedField<T>(T? Value, string? RawText, double? Confidence)
{
    public bool HasValue => Value is not null;
}

/// <summary>Helpers for <see cref="ExtractedField{T}"/>. Kept outside the generic type so the type argument is inferred once.</summary>
public static class ExtractedField
{
    /// <summary>A field the model did not return at all.</summary>
    public static ExtractedField<T> Missing<T>() => new(default, null, null);
}
