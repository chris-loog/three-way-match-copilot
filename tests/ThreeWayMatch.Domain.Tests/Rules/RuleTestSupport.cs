using System.Globalization;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Matching;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Domain.Tests.Matching;

namespace ThreeWayMatch.Domain.Tests.Rules;

/// <summary>
/// Builds an extracted invoice the way a perfect extraction would: totals add up, the IBAN is the supplier's, every field is
/// read with 0.95 confidence from text printed the Estonian way. Each test then changes the one thing it is about.
/// </summary>
internal sealed class TestInvoice
{
    private static readonly CultureInfo Estonian = CultureInfo.GetCultureInfo("et-EE");

    private readonly List<(string Code, string Description, decimal Quantity, string Unit, decimal Price)> _lines = [];

    public string Vat { get; set; } = MatchingWorld.TimberVat;
    public string SupplierName { get; set; } = "Sillaotsa Saeveski OÜ";
    public string? PoReference { get; set; } = "PO-2026-0001";
    public string Number { get; set; } = "A-1";
    public DateOnly Date { get; set; } = new(2026, 4, 2);
    public string Iban { get; set; } = "EE78 0400 1503 1113 4947";
    public double Confidence { get; set; } = 0.95;
    public decimal? NetOverride { get; set; }
    public decimal? VatOverride { get; set; }
    public decimal? GrossOverride { get; set; }

    public static TestInvoice Board(decimal quantity = 10m, decimal price = 100m) =>
        new TestInvoice().Line("KS-0101", "Kuuselaud 25x150 mm, kuivatatud, 4,8 m", quantity, "m³", price);

    public TestInvoice Line(string code, string description, decimal quantity, string unit, decimal price)
    {
        _lines.Add((code, description, quantity, unit, price));
        return this;
    }

    public TestInvoice On(string? poReference)
    {
        PoReference = poReference;
        return this;
    }

    public ExtractedInvoice Build()
    {
        var lineTotals = _lines.Select(l => Math.Round(l.Quantity * l.Price, 2)).ToList();
        var net = lineTotals.Sum();
        var vat = lineTotals.Sum(t => Math.Round(t * 0.24m, 2));

        ExtractedField<decimal?> Amount(decimal value) => new(value, value.ToString("N2", Estonian), Confidence);
        ExtractedField<string> Text(string value) => new(value, value, Confidence);

        return new ExtractedInvoice
        {
            ModelId = "prebuilt-invoice",
            RawJson = "{}",
            SupplierName = Text(SupplierName),
            SupplierVatNumber = new ExtractedField<string>(Vat, "KMKR " + Vat, Confidence),
            InvoiceNumber = Text(Number),
            PurchaseOrderReference = PoReference is null ? ExtractedField.Missing<string>() : Text(PoReference),
            InvoiceDate = new ExtractedField<DateOnly?>(Date, Date.ToString("dd.MM.yyyy", Estonian), Confidence),
            NetTotal = Amount(NetOverride ?? net),
            VatTotal = Amount(VatOverride ?? vat),
            GrossTotal = Amount(GrossOverride ?? (NetOverride ?? net) + (VatOverride ?? vat)),
            Iban = new ExtractedField<string>(Iban.Replace(" ", string.Empty, StringComparison.Ordinal), Iban, Confidence),
            Lines = _lines.Select((l, i) => new ExtractedInvoiceLine(
                i + 1,
                Text(l.Code),
                Text(l.Description),
                new ExtractedField<decimal?>(l.Quantity, l.Quantity.ToString(l.Quantity == Math.Truncate(l.Quantity) ? "N0" : "N2", Estonian), Confidence),
                Text(l.Unit),
                new ExtractedField<decimal?>(l.Price, l.Price.ToString("N2", Estonian), Confidence),
                ExtractedField.Missing<decimal?>(),
                Amount(lineTotals[i]))).ToList(),
        };
    }
}

internal static class RuleTestSupport
{
    public static Assessment Assess(
        ExtractedInvoice invoice, MatchingWorld world, string? hash = "hash-new",
        IReadOnlyList<ProcessedInvoice>? processed = null, MatchingSnapshot? snapshot = null, DiscrepancyOptions? options = null) =>
        DocumentAssessor.CreateDefault().AssessInvoice(invoice, hash, snapshot ?? world.Snapshot(), processed ?? [], options);

    public static Assessment Assess(TestInvoice invoice, MatchingWorld world, string? hash = "hash-new",
        IReadOnlyList<ProcessedInvoice>? processed = null, MatchingSnapshot? snapshot = null, DiscrepancyOptions? options = null) =>
        Assess(invoice.Build(), world, hash, processed, snapshot, options);

    public static IEnumerable<DiscrepancyCode> Codes(this Assessment assessment) => assessment.Findings.Select(f => f.Code);

    public static Finding Single(this Assessment assessment, DiscrepancyCode code) => assessment.Findings.Single(f => f.Code == code);
}
