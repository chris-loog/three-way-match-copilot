using System.Globalization;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Tests.Normalisation;

/// <summary>
/// The document generator prints amounts with .NET's own culture formatting. Whatever it prints, the parser must read back
/// exactly, in both supplier cultures, for small prices, large totals and whole numbers.
/// </summary>
public class AmountRoundTripTests
{
    [Theory]
    [InlineData("et-EE")]
    [InlineData("en-GB")]
    public void Every_formatted_amount_parses_back_to_the_same_value(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var random = new Random(20261011);

        for (var i = 0; i < 1000; i++)
        {
            var cents = random.NextInt64(1, 100_000_000); // 0.01 to 999 999.99
            var money = cents / 100m;
            var quantity = random.Next(1, 100_000);
            var smallPrice = random.Next(1, 100_000) / 10_000m; // 0.0001 to 9.9999

            AssertRoundTrip(money.ToString("N2", culture), money, culture);
            AssertRoundTrip(quantity.ToString("N0", culture), quantity, culture);
            AssertRoundTrip(smallPrice.ToString("N4", culture), smallPrice, culture);
        }
    }

    private static void AssertRoundTrip(string printed, decimal expected, CultureInfo culture)
    {
        Assert.True(Normaliser.TryParseAmount(printed, culture, out var parsed), $"'{printed}' was not parsed ({culture.Name})");
        Assert.True(expected == parsed, $"'{printed}' parsed as {parsed}, expected {expected} ({culture.Name})");
    }
}
