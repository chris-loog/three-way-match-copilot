using System.Globalization;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Tests.Normalisation;

public class AmountParsingTests
{
    private static decimal? Parse(string? text, string culture) =>
        Normaliser.TryParseAmount(text, CultureInfo.GetCultureInfo(culture), out var value) ? value : null;

    [Theory]
    // Estonian: space (or non-breaking space) between thousands, comma for decimals.
    [InlineData("1 234,56", "et-EE", "1234.56")]
    [InlineData("1 234,56", "et-EE", "1234.56")]
    [InlineData("1 234,56", "et-EE", "1234.56")]
    [InlineData("1 234 567,89", "et-EE", "1234567.89")]
    [InlineData("1234,56", "et-EE", "1234.56")]
    [InlineData("0,04", "et-EE", "0.04")]
    [InlineData("12", "et-EE", "12")]
    // A dot as thousands separator and a comma as decimal also appears on Estonian paperwork.
    [InlineData("1.234,56", "et-EE", "1234.56")]
    // The ambiguous cases: the supplier's culture decides.
    [InlineData("1.234", "et-EE", "1234")]
    [InlineData("1,234", "et-EE", "1.234")]
    [InlineData("1.234", "en-GB", "1.234")]
    [InlineData("1,234", "en-GB", "1234")]
    // A separator that cannot be a thousands separator is a decimal one, whatever the culture.
    [InlineData("1.5", "et-EE", "1.5")]
    [InlineData("1,5", "en-GB", "1.5")]
    // English: comma for thousands, point for decimals.
    [InlineData("1,234.56", "en-GB", "1234.56")]
    [InlineData("1,234,567.89", "en-GB", "1234567.89")]
    [InlineData("1234.56", "en-GB", "1234.56")]
    [InlineData("1 234.56", "en-GB", "1234.56")]
    [InlineData("12.50", "en-GB", "12.5")]
    // Currency marks and negative amounts.
    [InlineData("€ 12,50", "et-EE", "12.5")]
    [InlineData("12,50 €", "et-EE", "12.5")]
    [InlineData("12,50 EUR", "et-EE", "12.5")]
    [InlineData("EUR 12.50", "en-GB", "12.5")]
    [InlineData("-12,50", "et-EE", "-12.5")]
    [InlineData("−12,50", "et-EE", "-12.5")]
    [InlineData("(12,50)", "et-EE", "-12.5")]
    [InlineData("  1 450,35  ", "et-EE", "1450.35")]
    public void Parses_amounts_in_the_suppliers_format(string text, string culture, string expected)
    {
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), Parse(text, culture));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12a3")]
    [InlineData("12,34,56")]
    [InlineData("1..2")]
    [InlineData("--5")]
    [InlineData("1.2.3,4")]
    [InlineData("1 2 3")]
    [InlineData("1234 56")]
    [InlineData("0.123.456")]
    [InlineData("12,")]
    [InlineData(",5x")]
    public void Refuses_text_that_is_not_an_amount_instead_of_guessing(string? text)
    {
        Assert.Null(Parse(text, "et-EE"));
        Assert.Null(Parse(text, "en-GB"));
    }

    [Fact]
    public void A_leading_zero_before_a_thousands_group_is_refused_because_it_is_ambiguous()
    {
        Assert.Null(Parse("0.123.456", "et-EE"));
        Assert.Null(Parse("01.234", "et-EE"));
    }
}
