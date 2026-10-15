using System.Globalization;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Tests.Normalisation;

public class InvoiceReconciliationTests
{
    private const decimal Tolerance = 0.05m;

    private static ExtractedField<decimal?> Amount(decimal? value, double? confidence = 0.2) => new(value, value?.ToString(CultureInfo.InvariantCulture), confidence);

    private static ExtractedField<string> Text(string value) => new(value, value, 0.95);

    private static ExtractedInvoiceLine Line(int number, decimal? quantity, decimal? unitPrice, decimal? total, double? quantityConfidence = 0.2) =>
        new(number, Text("X"), Text("Board"), Amount(quantity, quantityConfidence), Text("pcs"), Amount(unitPrice), ExtractedField.Missing<decimal?>(), Amount(total));

    private static ExtractedInvoice Invoice(
        decimal? net = 1000m, decimal? vat = 240m, decimal? gross = 1240m, params ExtractedInvoiceLine[] lines) => new()
    {
        ModelId = "prebuilt-invoice",
        RawJson = "{}",
        NetTotal = Amount(net),
        VatTotal = Amount(vat),
        GrossTotal = Amount(gross),
        Lines = lines.Length > 0 ? lines : [Line(1, 5m, 150m, 750m), Line(2, 10m, 25m, 250m)],
    };

    [Fact]
    public void An_invoice_that_adds_up_reconciles_even_when_the_model_was_unsure()
    {
        Assert.True(InvoiceReconciliation.Reconciles(Invoice(), Tolerance));
    }

    [Fact]
    public void A_line_where_quantity_times_price_is_not_the_line_total_does_not_reconcile()
    {
        // 1.292 pieces instead of 1292: the line total no longer matches.
        var invoice = Invoice(net: 1162.80m, vat: 279.07m, gross: 1441.87m, Line(1, 1.292m, 0.90m, 1162.80m));

        Assert.False(InvoiceReconciliation.Reconciles(invoice, Tolerance));
    }

    [Fact]
    public void Lines_that_do_not_add_up_to_the_net_total_do_not_reconcile()
    {
        Assert.False(InvoiceReconciliation.Reconciles(Invoice(net: 1100m, vat: 264m, gross: 1364m), Tolerance));
    }

    [Fact]
    public void Net_plus_vat_that_is_not_the_gross_total_does_not_reconcile()
    {
        Assert.False(InvoiceReconciliation.Reconciles(Invoice(gross: 1300m), Tolerance));
    }

    [Fact]
    public void A_small_rounding_difference_within_tolerance_still_reconciles()
    {
        Assert.True(InvoiceReconciliation.Reconciles(Invoice(gross: 1240.04m), Tolerance));
        Assert.False(InvoiceReconciliation.Reconciles(Invoice(gross: 1240.06m), Tolerance));
    }

    [Fact]
    public void A_missing_figure_does_not_reconcile()
    {
        Assert.False(InvoiceReconciliation.Reconciles(Invoice(vat: null), Tolerance));
        Assert.False(InvoiceReconciliation.Reconciles(Invoice(lines: Line(1, null, 150m, 750m)), Tolerance));
    }

    [Fact]
    public void A_figure_the_cross_check_distrusted_does_not_reconcile()
    {
        // Confidence 0 means our own reading of the printed text disagreed with the model's value.
        var invoice = Invoice(net: 750m, vat: 180m, gross: 930m, Line(1, 5m, 150m, 750m, quantityConfidence: 0));

        Assert.False(InvoiceReconciliation.Reconciles(invoice, Tolerance));
    }

    [Fact]
    public void An_invoice_without_lines_does_not_reconcile()
    {
        var invoice = Invoice() with { Lines = [] };

        Assert.False(InvoiceReconciliation.Reconciles(invoice, Tolerance));
    }
}
