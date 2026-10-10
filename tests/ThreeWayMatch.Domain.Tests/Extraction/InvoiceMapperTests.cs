using System.Text.Json.Nodes;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Infrastructure.DocumentIntelligence;

namespace ThreeWayMatch.Domain.Tests.Extraction;

/// <summary>
/// The fixture is a hand-built result in the exact shape the service returns, because tests never call Azure.
/// Replace it with a result saved from a real run once one exists (the cache folder holds them).
/// </summary>
public class InvoiceMapperTests
{
    private static string Fixture() => File.ReadAllText(Path.Combine("Fixtures", "analyze-result-invoice.json"));

    private static ExtractedInvoice Map(string? json = null) => InvoiceMapper.Map(json ?? Fixture(), "prebuilt-invoice");

    [Fact]
    public void Reads_the_header_fields_with_their_confidence_and_source_text()
    {
        var invoice = Map();

        Assert.Equal("Baltic Board Supply OÜ", invoice.SupplierName.Value);
        Assert.Equal("EE109532815", invoice.SupplierVatNumber.Value);
        Assert.Equal(0.93, invoice.SupplierVatNumber.Confidence!.Value, precision: 2);
        Assert.Equal("BBS/2026/0354", invoice.InvoiceNumber.Value);
        Assert.Equal("PO-2026-0003", invoice.PurchaseOrderReference.Value);
        Assert.Equal(new DateOnly(2026, 4, 16), invoice.InvoiceDate.Value);
        Assert.Equal("16 Apr 2026", invoice.InvoiceDate.RawText);
    }

    [Fact]
    public void Reads_the_totals_as_decimals_and_the_currency_code()
    {
        var invoice = Map();

        Assert.Equal(16563.00m, invoice.NetTotal.Value);
        Assert.Equal(3975.12m, invoice.VatTotal.Value);
        Assert.Equal(20538.12m, invoice.GrossTotal.Value);
        Assert.Equal("EUR", invoice.Currency.Value);
    }

    [Fact]
    public void Reads_every_line_in_order_with_numbers_units_and_tax_rate()
    {
        var invoice = Map();

        Assert.Equal(3, invoice.Lines.Count);
        var first = invoice.Lines[0];
        Assert.Equal(1, first.LineNumber);
        Assert.Equal("BBS00157", first.ItemCode.Value);
        Assert.Equal("Spruce plywood 15 mm, 1220x2440", first.Description.Value);
        Assert.Equal(283m, first.Quantity.Value);
        Assert.Equal("pcs", first.Unit.Value);
        Assert.Equal(36.00m, first.UnitPrice.Value);
        Assert.Equal(10188.00m, first.LineTotal.Value);
        Assert.Equal(0.24m, first.VatRate.Value);
        Assert.Equal(3, invoice.Lines[2].LineNumber);
    }

    [Fact]
    public void Finds_the_iban_in_the_page_text_when_the_model_returned_none()
    {
        var invoice = Map();

        Assert.Equal("EE78 0400 1503 1113 4947", invoice.Iban.Value);
    }

    [Fact]
    public void A_text_found_iban_is_only_as_sure_as_its_least_sure_word()
    {
        // The fixture has one smudged group (0.55) inside the IBAN.
        Assert.Equal(0.55, Map().Iban.Confidence!.Value, precision: 2);
        Assert.Contains("Iban", Map().LowConfidenceFields(0.80));
    }

    [Fact]
    public void Finds_the_registry_code_in_the_page_text()
    {
        Assert.Equal("10776889", Map().SupplierRegistryCode.Value);
    }

    [Fact]
    public void Prefers_an_iban_from_the_query_field_over_the_text_search()
    {
        var json = JsonNode.Parse(Fixture())!;
        json["documents"]![0]!["fields"]!["Iban"] = JsonNode.Parse("""{"type":"string","valueString":"EE780400150311134947","content":"EE780400150311134947","confidence":0.88}""");

        var invoice = Map(json.ToJsonString());

        Assert.Equal("EE780400150311134947", invoice.Iban.Value);
        Assert.Equal(0.88, invoice.Iban.Confidence!.Value, precision: 2);
    }

    [Fact]
    public void Accepts_the_studio_export_that_wraps_the_result()
    {
        var wrapped = $$"""{"status":"succeeded","analyzeResult":{{Fixture()}}}""";

        Assert.Equal("BBS/2026/0354", Map(wrapped).InvoiceNumber.Value);
    }

    [Fact]
    public void Keeps_the_raw_json_and_the_model_id()
    {
        var json = Fixture();
        var invoice = Map(json);

        Assert.Equal(json, invoice.RawJson);
        Assert.Equal("prebuilt-invoice", invoice.ModelId);
    }

    [Fact]
    public void A_result_without_a_document_gives_missing_fields_and_no_lines_instead_of_failing()
    {
        var json = JsonNode.Parse(Fixture())!;
        json["documents"] = new JsonArray();

        var invoice = Map(json.ToJsonString());

        Assert.False(invoice.InvoiceNumber.HasValue);
        Assert.Empty(invoice.Lines);
        Assert.Equal(0, invoice.MinKeyFieldConfidence());
    }

    [Fact]
    public void Garbage_input_throws_instead_of_returning_an_empty_invoice()
    {
        Assert.ThrowsAny<Exception>(() => InvoiceMapper.Map("not json", "prebuilt-invoice"));
    }
}
