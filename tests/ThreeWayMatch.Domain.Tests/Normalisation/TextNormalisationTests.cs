using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Tests.Normalisation;

public class TextNormalisationTests
{
    [Theory]
    [InlineData("  Spruce   PLANK 25 x 150 ", "spruce plank 25 x 150")]
    [InlineData("Kuuselaud\t25x150 mm", "kuuselaud 25x150 mm")]
    [InlineData("MÄNNILAUD", "männilaud")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_description_is_trimmed_lower_cased_and_has_single_spaces(string? text, string expected)
    {
        Assert.Equal(expected, Normaliser.NormaliseDescription(text));
    }

    [Theory]
    [InlineData("PO-2026-0001", "PO20260001")]
    [InlineData("po 2026 0001.", "PO20260001")]
    [InlineData("KS-0101", "KS0101")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void A_reference_key_keeps_only_letters_and_digits_in_upper_case(string? text, string expected)
    {
        Assert.Equal(expected, Normaliser.ReferenceKey(text));
    }
}
