using ThreeWayMatch.Domain.Extraction;

namespace ThreeWayMatch.Domain.Tests.Extraction;

public class ExtractedInvoiceTests
{
    private static ExtractedField<string> Text(string value, double confidence) => new(value, value, confidence);

    private static ExtractedField<decimal?> Amount(decimal value, double confidence) => new(value, value.ToString(System.Globalization.CultureInfo.InvariantCulture), confidence);

    private static ExtractedInvoice Invoice(double iban = 0.95, double unitPrice = 0.95, bool withPoReference = true) => new()
    {
        ModelId = "prebuilt-invoice",
        RawJson = "{}",
        SupplierVatNumber = Text("EE109532815", 0.95),
        InvoiceNumber = Text("A-1", 0.95),
        PurchaseOrderReference = withPoReference ? Text("PO-2026-0001", 0.95) : ExtractedField.Missing<string>(),
        NetTotal = Amount(100m, 0.95),
        VatTotal = Amount(24m, 0.95),
        GrossTotal = Amount(124m, 0.95),
        Iban = Text("EE780400150311134947", iban),
        Lines =
        [
            new ExtractedInvoiceLine(1, Text("X1", 0.9), Text("Board", 0.9), Amount(5m, 0.95), Text("m³", 0.9), Amount(20m, unitPrice), ExtractedField.Missing<decimal?>(), Amount(100m, 0.95)),
        ],
    };

    [Fact]
    public void Key_fields_cover_the_header_and_every_line_quantity_and_price()
    {
        var keys = Invoice().KeyFieldConfidences().Keys;

        Assert.Contains("SupplierVatNumber", keys);
        Assert.Contains("Iban", keys);
        Assert.Contains("Line1.Quantity", keys);
        Assert.Contains("Line1.UnitPrice", keys);
        Assert.DoesNotContain("SupplierName", keys);
    }

    [Fact]
    public void A_field_the_model_did_not_return_counts_as_unreadable()
    {
        var invoice = Invoice(withPoReference: false);

        Assert.Equal(0, invoice.KeyFieldConfidences()["PurchaseOrderReference"]);
        Assert.Equal(0, invoice.MinKeyFieldConfidence());
    }

    [Fact]
    public void Low_confidence_fields_are_listed_worst_first_and_only_below_the_threshold()
    {
        var invoice = Invoice(iban: 0.55, unitPrice: 0.70);

        Assert.Equal(["Iban", "Line1.UnitPrice"], invoice.LowConfidenceFields(0.80));
        Assert.Empty(Invoice().LowConfidenceFields(0.80));
    }

    [Fact]
    public void A_value_exactly_at_the_threshold_is_not_low()
    {
        Assert.Empty(Invoice(iban: 0.80).LowConfidenceFields(0.80));
    }

    [Fact]
    public void Min_key_field_confidence_is_the_lowest_of_all_key_fields()
    {
        Assert.Equal(0.55, Invoice(iban: 0.55).MinKeyFieldConfidence());
    }
}
