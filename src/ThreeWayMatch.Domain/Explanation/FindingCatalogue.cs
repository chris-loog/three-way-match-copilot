namespace ThreeWayMatch.Domain.Explanation;

/// <summary>
/// What each finding code means, what the reviewer must do about it, and whether the supplier may be written to.
/// One table feeds the facts the model sees, the fixed text added to the reviewer's next step, and the template fallback,
/// so the three cannot disagree. Meanings contain no digits: the number guard would otherwise read them as invented numbers.
/// </summary>
public static class FindingCatalogue
{
    private sealed record Entry(string Meaning, string? ReviewerInstruction = null, bool AllowsSupplierDraft = true);

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal)
    {
        ["QTY_OVER_INVOICED"] = new("The invoice bills more than the goods received and not yet invoiced, and no further delivery is expected. Expected is the quantity still open on the receipts, actual is the quantity on the invoice."),
        ["PRICE_ABOVE_PO"] = new("The unit price on the invoice is higher than the price on the purchase order, beyond the tolerance. Expected is the order price, actual is the invoice price."),
        ["PRICE_BELOW_PO"] = new("The unit price on the invoice is lower than the price on the purchase order. Information only: paying less is not a risk."),
        ["ITEM_NOT_ON_PO"] = new("An invoice line matches no line on the purchase order. Actual is the line description."),
        ["NO_PO_REFERENCE"] = new("The invoice has no purchase order number, or one the system does not know. Expected is the order the system suggests, actual is what is printed."),
        ["DUPLICATE_INVOICE"] = new("The same file or the same invoice number from this supplier was already processed."),
        ["POSSIBLE_DUPLICATE"] = new("An earlier invoice from this supplier has the same amount and a close date but a different number. Expected is the earlier invoice number."),
        ["TOTALS_MISMATCH"] = new("The lines, the VAT and the totals on the invoice do not add up within the rounding tolerance."),
        ["DELIVERY_NOTE_MISMATCH"] = new("The quantity on the supplier's delivery note differs from what the warehouse booked. Expected is the booked quantity, actual is the quantity on the note."),
        ["UOM_MISMATCH"] = new("The unit of measure on the invoice differs from the supplier's unit on the order and cannot be converted. Expected is the order unit, actual is the invoice unit."),
        ["LOW_CONFIDENCE"] = new("The text recognition was not sure about one or more key fields, so the values may be misread. Actual lists the fields."),
        ["BANK_ACCOUNT_CHANGED"] = new(
            "The bank account on the invoice is a valid account but differs from the one in the supplier master data. This is a fraud signal. Expected is the master data account, actual is the account on the invoice.",
            "Before approving, phone the supplier on the number in the supplier master data, not a number or address from this invoice, and confirm the new bank account. Do not reply to the sender of the invoice.",
            AllowsSupplierDraft: false),
        ["PARTIAL_DELIVERY"] = new(
            "The invoice arrived before all ordered goods were received. Expected is the quantity received and not yet invoiced, actual is the quantity on the invoice. The document waits on hold until the goods receipt is booked. This is not over-delivery.",
            "Do not contact the supplier. Wait for the rest of the goods to be booked: the system re-checks documents on hold every day.",
            AllowsSupplierDraft: false),
    };

    public static string Meaning(string code) => Entries.TryGetValue(code, out var entry) ? entry.Meaning : string.Empty;

    public static bool AllowsSupplierDraft(string code) => !Entries.TryGetValue(code, out var entry) || entry.AllowsSupplierDraft;

    /// <summary>The fixed text the reviewer must read for this code, or null when the code needs none.</summary>
    public static string? ReviewerInstruction(string code) => Entries.TryGetValue(code, out var entry) ? entry.ReviewerInstruction : null;

    public static IReadOnlyCollection<string> Codes => Entries.Keys;
}
