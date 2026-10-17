using System.Text.Json.Nodes;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Infrastructure.DocumentIntelligence;

namespace ThreeWayMatch.Domain.Tests.Extraction;

/// <summary>
/// The fixture is a real result saved from Document Intelligence (prebuilt-invoice) for generated invoice inv-0018,
/// an Estonian-layout clean PDF. Tests never call Azure.
/// </summary>
public class InvoiceMapperTests
{
    private static string Fixture() => File.ReadAllText(Path.Combine("Fixtures", "real-invoice-inv-0018.json"));

    private static ExtractedInvoice Map(string? json = null) => InvoiceMapper.Map(json ?? Fixture(), "prebuilt-invoice");

    private static string WithoutFields(params string[] names)
    {
        var json = JsonNode.Parse(Fixture())!;
        var fields = json["documents"]![0]!["fields"]!.AsObject();
        foreach (var name in names)
        {
            fields.Remove(name);
        }

        return json.ToJsonString();
    }

    [Fact]
    public void Reads_the_header_fields_with_their_confidence_and_source_text()
    {
        var invoice = Map();

        Assert.Equal("EE102412824", invoice.SupplierVatNumber.Value);
        Assert.Equal(0.945, invoice.SupplierVatNumber.Confidence!.Value, precision: 3);
        Assert.Equal("A-221", invoice.InvoiceNumber.Value);
        Assert.Equal("PO-2026-0018", invoice.PurchaseOrderReference.Value);
        Assert.Equal(0.552, invoice.PurchaseOrderReference.Confidence!.Value, precision: 3);
        Assert.Equal(new DateOnly(2026, 4, 28), invoice.InvoiceDate.Value);
        Assert.Equal("28.04.2026", invoice.InvoiceDate.RawText);
    }

    [Fact]
    public void Reads_the_supplier_name_as_printed_on_the_page()
    {
        // The page footer prints "OU" without the umlaut; the model keeps what it read.
        Assert.Equal("Sillaotsa Saeveski OU", Map().SupplierName.Value);
    }

    [Fact]
    public void Reads_the_totals_as_decimals_and_the_currency_code()
    {
        var invoice = Map();

        Assert.Equal(6326.40m, invoice.NetTotal.Value);
        Assert.Equal(1518.34m, invoice.VatTotal.Value);
        Assert.Equal(7844.74m, invoice.GrossTotal.Value);
        Assert.Equal("EUR", invoice.Currency.Value);
        Assert.Equal("7 844,74 EUR", invoice.GrossTotal.RawText);
    }

    [Fact]
    public void Reads_every_line_in_order_with_numbers_and_units_as_printed()
    {
        var invoice = Map();

        Assert.Equal(2, invoice.Lines.Count);
        var first = invoice.Lines[0];
        Assert.Equal(1, first.LineNumber);
        Assert.Equal("KS-0259", first.ItemCode.Value);
        Assert.Equal("Kaselaud 40x120 mm, kamberkuivatatud", first.Description.Value);
        Assert.Equal(5.62m, first.Quantity.Value);
        Assert.Equal("m3", first.Unit.Value);
        Assert.Equal(720.00m, first.UnitPrice.Value);
        Assert.Equal(4046.40m, first.LineTotal.Value);
        Assert.Equal(2, invoice.Lines[1].LineNumber);
        Assert.Equal("pakk", invoice.Lines[1].Unit.Value);
        Assert.Equal(15m, invoice.Lines[1].Quantity.Value);
    }

    [Fact]
    public void Keeps_the_collapsed_table_confidence_the_model_reported()
    {
        // The model read every line value correctly but scored the table at about 0.002.
        // The mapper must not invent confidence; the reconciliation rule decides what to trust.
        var first = Map().Lines[0];

        Assert.True(first.Quantity.Confidence < 0.01);
        Assert.True(first.UnitPrice.Confidence < 0.01);
    }

    [Fact]
    public void Takes_the_iban_from_the_query_field()
    {
        var invoice = Map();

        Assert.Equal("EE25 0400 4927 1129 8646", invoice.Iban.Value);
        Assert.Equal(0.995, invoice.Iban.Confidence!.Value, precision: 3);
    }

    [Fact]
    public void Finds_the_iban_in_the_page_text_when_the_model_returned_none()
    {
        var invoice = Map(WithoutFields("Iban"));

        Assert.Equal("EE25 0400 4927 1129 8646", invoice.Iban.Value);
    }

    [Fact]
    public void A_text_found_iban_is_only_as_sure_as_its_least_sure_word()
    {
        var json = JsonNode.Parse(WithoutFields("Iban"))!;
        var smudged = json["pages"]!.AsArray().SelectMany(p => p!["words"]!.AsArray())
            .First(w => w!["content"]!.GetValue<string>() == "4927");
        smudged!["confidence"] = 0.55;

        var invoice = Map(json.ToJsonString());

        Assert.Equal(0.55, invoice.Iban.Confidence!.Value, precision: 2);
        Assert.Contains("Iban", invoice.LowConfidenceFields(0.80));
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
    public void Reads_the_registry_code_from_the_query_field()
    {
        Assert.Equal("11812518", Map().SupplierRegistryCode.Value);
    }

    [Fact]
    public void Finds_the_registry_code_in_the_page_text_when_the_model_returned_none()
    {
        Assert.Equal("11812518", Map(WithoutFields("RegistryCode")).SupplierRegistryCode.Value);
    }

    [Fact]
    public void Finds_the_purchase_order_reference_in_the_page_text_when_the_model_returned_none()
    {
        var invoice = Map(WithoutFields("PurchaseOrder"));

        Assert.Equal("PO-2026-0018", invoice.PurchaseOrderReference.Value);
        Assert.True(invoice.PurchaseOrderReference.Confidence > 0.9);
    }

    [Fact]
    public void Accepts_the_studio_export_that_wraps_the_result()
    {
        var wrapped = $$"""{"status":"succeeded","analyzeResult":{{Fixture()}}}""";

        Assert.Equal("A-221", Map(wrapped).InvoiceNumber.Value);
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
