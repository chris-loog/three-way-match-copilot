using System.Globalization;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Tests.Normalisation;

public class DateParsingTests
{
    private static DateOnly? Parse(string? text, string culture) =>
        Normaliser.TryParseDate(text, CultureInfo.GetCultureInfo(culture), out var value) ? value : null;

    [Theory]
    [InlineData("02.04.2026", "et-EE", 2026, 4, 2)]
    [InlineData("2.4.2026", "et-EE", 2026, 4, 2)]
    [InlineData("2026-04-02", "et-EE", 2026, 4, 2)]
    [InlineData("2026-04-02", "en-GB", 2026, 4, 2)]
    [InlineData("02.04.26", "et-EE", 2026, 4, 2)]
    [InlineData("  02.04.2026  ", "et-EE", 2026, 4, 2)]
    [InlineData("02/04/2026", "en-GB", 2026, 4, 2)]
    [InlineData("2/4/2026", "en-GB", 2026, 4, 2)]
    [InlineData("3 Apr 2026", "en-GB", 2026, 4, 3)]
    [InlineData("03 April 2026", "en-GB", 2026, 4, 3)]
    [InlineData("April 3, 2026", "en-GB", 2026, 4, 3)]
    [InlineData("3. aprill 2026", "et-EE", 2026, 4, 3)]
    [InlineData("31.12.2026", "et-EE", 2026, 12, 31)]
    public void Parses_the_date_formats_the_layouts_use(string text, string culture, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), Parse(text, culture));
    }

    [Fact]
    public void The_same_slash_date_means_different_days_in_different_cultures()
    {
        Assert.Equal(new DateOnly(2026, 4, 3), Parse("03/04/2026", "en-GB"));
        Assert.Equal(new DateOnly(2026, 3, 4), Parse("03/04/2026", "en-US"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("31.02.2026")]
    [InlineData("32.01.2026")]
    [InlineData("00.01.2026")]
    [InlineData("2026-13-01")]
    [InlineData("02.04")]
    [InlineData("1850-01-01")]
    public void Refuses_impossible_or_unreadable_dates(string? text)
    {
        Assert.Null(Parse(text, "et-EE"));
        Assert.Null(Parse(text, "en-GB"));
    }
}
