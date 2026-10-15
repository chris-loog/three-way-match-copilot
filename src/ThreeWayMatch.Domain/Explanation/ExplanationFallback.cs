using System.Globalization;
using System.Text;

namespace ThreeWayMatch.Domain.Explanation;

/// <summary>
/// The answer shown when the model is unavailable or fails the guards twice. Built from the facts with fixed wording,
/// so every number and code in it is true by construction.
/// </summary>
public static class ExplanationFallback
{
    public static ExplanationReply Build(ExplanationFacts facts)
    {
        var summary = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"{facts.Findings.Count} finding(s) on {facts.DocumentType.ToLowerInvariant()} {facts.DocumentNumber ?? "(no number)"} from {facts.Supplier ?? "an unknown supplier"}")
            .Append(facts.AmountAtStake > 0 ? string.Create(CultureInfo.InvariantCulture, $", {facts.AmountAtStake:0.00} {facts.Currency ?? "EUR"} at stake") : string.Empty)
            .Append(". ");
        foreach (var finding in facts.Findings)
        {
            summary.Append(Describe(finding, facts.Currency)).Append(' ');
        }

        var blocking = facts.Findings.Any(f => f.Severity == "Block");
        return new ExplanationReply(
            summary.ToString().Trim(),
            "No cause can be named without the model. The findings above list which checks failed.",
            blocking
                ? "Do not approve before the blocking findings are cleared. Check them against the order, the goods receipt and the supplier."
                : "Check the findings against the order and the goods receipt, then approve or ask the supplier.",
            SupplierMessage(facts));
    }

    private static string Describe(FactFinding finding, string? currency)
    {
        var where = finding.Line is { } line ? $" (line {line})" : string.Empty;
        var detail = finding.Expected is not null || finding.Actual is not null
            ? string.Create(CultureInfo.InvariantCulture, $": expected {finding.Expected ?? "-"}, found {finding.Actual ?? "-"}")
            : string.Empty;
        var money = finding.AmountAtStake > 0 ? string.Create(CultureInfo.InvariantCulture, $", {finding.AmountAtStake:0.00} {currency ?? "EUR"}") : string.Empty;
        return $"{finding.Code}{where}{detail}{money}.";
    }

    private static string SupplierMessage(ExplanationFacts facts)
    {
        var estonian = facts.SupplierLanguage.Equals("Estonian", StringComparison.OrdinalIgnoreCase);
        var number = facts.DocumentNumber ?? "-";
        var order = facts.PurchaseOrder ?? "-";
        var lines = string.Join('\n', facts.Findings.Select(f =>
            "- " + (estonian ? PhraseEt(f.Code) : PhraseEn(f.Code)) + (f.Line is { } l ? (estonian ? $" (rida {l})" : $" (line {l})") : string.Empty)
            + (f.Expected is not null || f.Actual is not null
                ? (estonian ? $": peaks olema {f.Expected ?? "-"}, arvel on {f.Actual ?? "-"}" : $": expected {f.Expected ?? "-"}, on the document {f.Actual ?? "-"}")
                : string.Empty)));

        return estonian
            ? $"Tere!\n\nKontrollime teie dokumenti {number} (tellimus {order}) ja leidsime järgmist:\n{lines}\n\nPalun kinnitage või saatke parandatud dokument.\n\nLugupidamisega"
            : $"Hello,\n\nWe are checking your document {number} (order {order}) and found the following:\n{lines}\n\nPlease confirm or send a corrected document.\n\nKind regards";
    }

    private static string PhraseEn(string code) => code switch
    {
        "QTY_OVER_INVOICED" => "More is billed than we have received",
        "PRICE_ABOVE_PO" => "The price is above the order price",
        "PRICE_BELOW_PO" => "The price is below the order price",
        "ITEM_NOT_ON_PO" => "A line is not on our order",
        "NO_PO_REFERENCE" => "There is no order reference",
        "DUPLICATE_INVOICE" => "This looks like a document we already have",
        "POSSIBLE_DUPLICATE" => "This may be a document we already have",
        "TOTALS_MISMATCH" => "The totals do not add up",
        "BANK_ACCOUNT_CHANGED" => "The bank account differs from the one we have on file",
        "DELIVERY_NOTE_MISMATCH" => "The delivery note differs from what our warehouse booked",
        "UOM_MISMATCH" => "The unit of measure differs from the order",
        "PARTIAL_DELIVERY" => "Not all goods have arrived yet",
        "LOW_CONFIDENCE" => "Parts of the document were hard to read",
        _ => code,
    };

    private static string PhraseEt(string code) => code switch
    {
        "QTY_OVER_INVOICED" => "Arvel on rohkem kui oleme kätte saanud",
        "PRICE_ABOVE_PO" => "Hind on tellimuse hinnast kõrgem",
        "PRICE_BELOW_PO" => "Hind on tellimuse hinnast madalam",
        "ITEM_NOT_ON_PO" => "Rida ei ole meie tellimusel",
        "NO_PO_REFERENCE" => "Tellimuse number puudub",
        "DUPLICATE_INVOICE" => "See dokument on meil juba olemas",
        "POSSIBLE_DUPLICATE" => "See dokument võib olla meil juba olemas",
        "TOTALS_MISMATCH" => "Summad ei klapi",
        "BANK_ACCOUNT_CHANGED" => "Pangakonto erineb meie andmetes olevast",
        "DELIVERY_NOTE_MISMATCH" => "Saateleht erineb sellest, mis meie ladu vastu võttis",
        "UOM_MISMATCH" => "Ühik erineb tellimusest",
        "PARTIAL_DELIVERY" => "Kõik kaup ei ole veel kohale jõudnud",
        "LOW_CONFIDENCE" => "Dokumendi osad olid raskesti loetavad",
        _ => code,
    };
}
