using System.Globalization;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Tests.Normalisation;

public class ExtractionCrossCheckTests
{
    private static readonly CultureInfo Estonian = CultureInfo.GetCultureInfo("et-EE");

    private static ExtractedField<string> Text(string value, string? raw = null, double confidence = 0.95) => new(value, raw ?? value, confidence);

    private static ExtractedField<decimal?> Amount(decimal value, string raw, double confidence = 0.95) => new(value, raw, confidence);

    private static ExtractedInvoice Invoice(
        ExtractedField<decimal?>? gross = null,
        ExtractedField<DateOnly?>? date = null,
        ExtractedField<string>? iban = null,
        ExtractedField<string>? vat = null,
        ExtractedInvoiceLine? line = null) => new()
    {
        ModelId = "prebuilt-invoice",
        RawJson = "{}",
        SupplierVatNumber = vat ?? Text("EE109532815", "KMKR EE109532815"),
        InvoiceNumber = Text("A-1"),
        PurchaseOrderReference = Text("PO-2026-0001"),
        NetTotal = Amount(1000m, "1 000,00"),
        VatTotal = Amount(240m, "240,00"),
        GrossTotal = gross ?? Amount(1240m, "1 240,00"),
        InvoiceDate = date ?? new ExtractedField<DateOnly?>(new DateOnly(2026, 4, 2), "02.04.2026", 0.95),
        Iban = iban ?? Text("EE780400150311134947", "EE78 0400 1503 1113 4947"),
        Lines = [line ?? new ExtractedInvoiceLine(1, Text("X1"), Text("Board"), Amount(5m, "5"), Text("m³"), Amount(200m, "200,00"), ExtractedField.Missing<decimal?>(), Amount(1000m, "1 000,00"))],
    };

    [Fact]
    public void Leaves_everything_alone_when_our_own_reading_agrees_with_the_models()
    {
        var checkedInvoice = ExtractionCrossCheck.Apply(Invoice(), Estonian);

        Assert.Empty(checkedInvoice.LowConfidenceFields(0.80));
        Assert.Equal(0.95, checkedInvoice.MinKeyFieldConfidence(), precision: 2);
    }

    [Fact]
    public void An_amount_the_model_and_we_read_differently_becomes_unreliable()
    {
        // The model says 1.234, but on an Estonian invoice "1.234" means 1234.
        var invoice = Invoice(gross: Amount(1.234m, "1.234"));

        var result = ExtractionCrossCheck.Apply(invoice, Estonian);

        Assert.Contains("GrossTotal", result.LowConfidenceFields(0.80));
        Assert.Equal(0, result.GrossTotal.Confidence);
    }

    [Fact]
    public void An_amount_we_cannot_parse_at_all_becomes_unreliable()
    {
        var result = ExtractionCrossCheck.Apply(Invoice(gross: Amount(1240m, "12a40")), Estonian);

        Assert.Equal(0, result.GrossTotal.Confidence);
    }

    [Fact]
    public void A_date_the_model_read_with_day_and_month_swapped_becomes_unreliable()
    {
        var swapped = new ExtractedField<DateOnly?>(new DateOnly(2026, 2, 4), "02.04.2026", 0.95);

        var result = ExtractionCrossCheck.Apply(Invoice(date: swapped), Estonian);

        Assert.Equal(0, result.InvoiceDate.Confidence);
    }

    [Fact]
    public void An_iban_with_a_wrong_checksum_becomes_unreliable_because_it_is_probably_a_misread()
    {
        var result = ExtractionCrossCheck.Apply(Invoice(iban: Text("EE780400150311134948", "EE78 0400 1503 1113 4948")), Estonian);

        Assert.Equal(0, result.Iban.Confidence);
        Assert.Contains("Iban", result.LowConfidenceFields(0.80));
    }

    [Fact]
    public void A_valid_iban_keeps_its_confidence_even_with_spaces_in_the_source_text()
    {
        var result = ExtractionCrossCheck.Apply(Invoice(), Estonian);

        Assert.Equal(0.95, result.Iban.Confidence);
    }

    [Fact]
    public void A_vat_number_that_cannot_be_normalised_becomes_unreliable()
    {
        var result = ExtractionCrossCheck.Apply(Invoice(vat: Text("EE10953281", "EE10953281")), Estonian);

        Assert.Equal(0, result.SupplierVatNumber.Confidence);
    }

    [Fact]
    public void Line_quantities_and_prices_are_checked_too()
    {
        var line = new ExtractedInvoiceLine(1, Text("X1"), Text("Board"), Amount(5m, "5"), Text("m³"), Amount(2.5m, "2.500"), ExtractedField.Missing<decimal?>(), Amount(1000m, "1 000,00"));

        var result = ExtractionCrossCheck.Apply(Invoice(line: line), Estonian);

        Assert.Equal(0, result.Lines[0].UnitPrice.Confidence);
        Assert.Contains("Line1.UnitPrice", result.LowConfidenceFields(0.80));
    }

    [Fact]
    public void A_value_without_source_text_cannot_be_compared_and_keeps_the_models_confidence()
    {
        var withoutRaw = new ExtractedField<decimal?>(1240m, null, 0.91);

        var result = ExtractionCrossCheck.Apply(Invoice(gross: withoutRaw), Estonian);

        Assert.Equal(0.91, result.GrossTotal.Confidence);
    }

    [Fact]
    public void Does_not_change_the_invoice_it_was_given()
    {
        var original = Invoice(gross: Amount(1.234m, "1.234"));

        _ = ExtractionCrossCheck.Apply(original, Estonian);

        Assert.Equal(0.95, original.GrossTotal.Confidence);
    }

    [Fact]
    public void Uses_the_suppliers_culture_so_the_same_text_can_agree_in_one_culture_and_not_in_another()
    {
        var invoice = Invoice(gross: Amount(1.234m, "1.234"));

        Assert.Equal(0, ExtractionCrossCheck.Apply(invoice, Estonian).GrossTotal.Confidence);
        Assert.Equal(0.95, ExtractionCrossCheck.Apply(invoice, CultureInfo.GetCultureInfo("en-GB")).GrossTotal.Confidence);
    }
}
