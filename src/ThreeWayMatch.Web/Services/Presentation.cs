using System.Globalization;
using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Web.Services;

/// <summary>Plain-language names for codes and statuses, so the screens never show an enum name.</summary>
public static class Presentation
{
    public static string Title(DiscrepancyCode code) => code switch
    {
        DiscrepancyCode.QtyOverInvoiced => "Billed more than received",
        DiscrepancyCode.PriceAbovePo => "Price above the order",
        DiscrepancyCode.ItemNotOnPo => "Line not on the order",
        DiscrepancyCode.NoPoReference => "No order reference",
        DiscrepancyCode.DuplicateInvoice => "Duplicate invoice",
        DiscrepancyCode.TotalsMismatch => "Totals do not add up",
        DiscrepancyCode.BankAccountChanged => "Bank account changed",
        DiscrepancyCode.DeliveryNoteMismatch => "Delivery note differs from the receipt",
        DiscrepancyCode.UomMismatch => "Units do not match",
        DiscrepancyCode.PartialDelivery => "Goods not fully received",
        DiscrepancyCode.PossibleDuplicate => "Possible duplicate",
        DiscrepancyCode.PriceBelowPo => "Price below the order",
        DiscrepancyCode.LowConfidence => "Hard to read",
        _ => code.ToString(),
    };

    public static string Word(Severity severity) => severity switch
    {
        Severity.Block => "Blocks payment",
        Severity.Warning => "Check",
        _ => "Note",
    };

    public static string Css(Severity severity) => severity.ToString().ToLowerInvariant();

    public static string Label(DocumentStatus status, bool reviewed) => reviewed
        ? "Approved by reviewer"
        : status switch
        {
            DocumentStatus.AutoApproved => "Approved automatically",
            DocumentStatus.OnHold => "On hold",
            DocumentStatus.NeedsReview => "Needs review",
            DocumentStatus.Rejected => "Rejected",
            DocumentStatus.Failed => "Could not be read",
            DocumentStatus.Received => "Waiting to be read",
            DocumentStatus.Extracted => "Waiting to be matched",
            _ => status.ToString(),
        };

    public static string Money(decimal? value, string? currency = "EUR") =>
        value is null ? "-" : value.Value.ToString("N2", CultureInfo.InvariantCulture) + (currency == "EUR" || currency is null ? " €" : " " + currency);

    public static string Quantity(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    public static string Age(DateTimeOffset when, DateTimeOffset now)
    {
        var span = now - when;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
            : $"{(int)span.TotalDays} d ago";
    }

    /// <summary>A share of a scale as a CSS width, e.g. <c>41.7%</c>, always with a point.</summary>
    public static string Pct(decimal part, decimal scale) =>
        (scale <= 0 ? 0 : Math.Clamp(part / scale * 100m, 0m, 100m)).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    public static string Method(MatchMethod method) => method switch
    {
        MatchMethod.SupplierItemCode => "matched by the supplier's item code",
        MatchMethod.ItemCode => "matched by our item code",
        MatchMethod.LearnedAlias => "matched by wording we learned earlier",
        MatchMethod.FuzzyDescription => "matched by similar wording (a guess)",
        MatchMethod.ChargeKeyword => "matched as a charge line",
        _ => "not matched to the order",
    };

    public static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
