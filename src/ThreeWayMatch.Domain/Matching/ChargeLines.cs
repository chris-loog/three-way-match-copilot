namespace ThreeWayMatch.Domain.Matching;

/// <summary>
/// Freight, fuel surcharges and discounts are not goods, so they have no item code to match on.
/// They are recognised by a keyword in the description (English and Estonian).
/// </summary>
public static class ChargeLines
{
    private static readonly string[] Keywords =
    [
        "transport", "delivery", "freight", "shipping", "fuel", "surcharge", "discount",
        "kütuselisa", "kutuselisa", "allahindlus", "veo", "kohaletoimetamine", "tarne",
    ];

    public static bool LooksLikeCharge(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var text = description.ToLowerInvariant();
        return Keywords.Any(k => text.Contains(k, StringComparison.Ordinal));
    }
}
