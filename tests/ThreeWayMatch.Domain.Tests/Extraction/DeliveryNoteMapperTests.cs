using System.Text.Json.Nodes;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Infrastructure.DocumentIntelligence;

namespace ThreeWayMatch.Domain.Tests.Extraction;

public class DeliveryNoteMapperTests
{
    // The text and tables of the four supplier layouts, as pdftotext shows them in the generated PDFs.
    private static string EstonianNote(IReadOnlyDictionary<string, double>? confidence = null, string quantity = "13,53") => LayoutResultBuilder.Build(
        "Sillaotsa Saeveski OÜ\nSAATELEHT\nSaatelehe nr: 2959\nTarnekuupäev: 07.04.2026\nKMKR nr EE102412824\nTellimus nr: PO-2026-0001\nRegistrikood 11812518\nSaaja\nNäidis Puit OÜ\nKMKR EE100000001 · Reg. 10000001",
        [["Nr", "Kood", "Nimetus", "Kogus", "Ühik"], ["1", "KS-0253", "Kaselaud 25x100 mm, kamberkuivatatud", quantity, "m³"], ["2", "KS-0297", "Tammelaud 27 mm, kuivatatud", "24,81", "m³"]],
        "Kauba vastu võttis:\nTarnekuupäev:\nSillaotsa Saeveski OÜ · KMKR nr EE102412824 · 1 / 1",
        confidence);

    private static ExtractedDeliveryNote Map(string json, string model = "prebuilt-layout") => DeliveryNoteMapper.Map(json, model);

    [Fact]
    public void Reads_the_header_of_an_estonian_note_with_rules()
    {
        var note = Map(EstonianNote());

        Assert.Equal("2959", note.DeliveryNoteNumber.Value);
        Assert.Equal("PO-2026-0001", note.PurchaseOrderReference.Value);
        Assert.Equal(new DateOnly(2026, 4, 7), note.DeliveryDate.Value);
        Assert.Equal("Sillaotsa Saeveski OÜ", note.SupplierName.Value);
    }

    [Fact]
    public void The_supplier_vat_number_is_never_the_buyers_own()
    {
        Assert.Equal("EE102412824", Map(EstonianNote()).SupplierVatNumber.Value);
    }

    [Fact]
    public void Reads_the_lines_with_decimal_comma_quantities_and_units_as_printed()
    {
        var note = Map(EstonianNote());

        Assert.Equal(2, note.Lines.Count);
        Assert.Equal(1, note.Lines[0].LineNumber);
        Assert.Equal("KS-0253", note.Lines[0].ItemCode.Value);
        Assert.Equal("Kaselaud 25x100 mm, kamberkuivatatud", note.Lines[0].Description.Value);
        Assert.Equal(13.53m, note.Lines[0].Quantity.Value);
        Assert.Equal("m³", note.Lines[0].Unit.Value);
        Assert.Equal(24.81m, note.Lines[1].Quantity.Value);
    }

    [Fact]
    public void Reads_an_english_note_whose_unit_column_comes_before_the_quantity()
    {
        var json = LayoutResultBuilder.Build(
            "DELIVERY NOTE Nordic Pack Solutions OÜ\nDelivery note no.: 1860\nDelivery date: 14/04/2026\nVAT no. EE101657339\nPO ref: PO-2026-0005\nDeliver to\nNäidis Puit OÜ\nKMKR EE100000001 · Reg. 10000001",
            [["Item", "Description", "Unit", "Qty"], ["NPS-624", "EUR pallet 1200x800", "pcs", "80"], ["NPS-640", "Corner protector 40 mm", "pack", "4"]],
            "Goods received by:\nDelivery date:\nNordic Pack Solutions OÜ · VAT no. EE101657339 · 1 / 1");

        var note = Map(json);

        Assert.Equal("1860", note.DeliveryNoteNumber.Value);
        Assert.Equal(new DateOnly(2026, 4, 14), note.DeliveryDate.Value);
        Assert.Equal("EE101657339", note.SupplierVatNumber.Value);
        Assert.Equal("PO-2026-0005", note.PurchaseOrderReference.Value);
        Assert.Equal("Nordic Pack Solutions OÜ", note.SupplierName.Value);
        Assert.Equal(["NPS-624", "NPS-640"], note.Lines.Select(l => l.ItemCode.Value));
        Assert.Equal([80m, 4m], note.Lines.Select(l => l.Quantity.Value));
        Assert.Equal("pcs", note.Lines[0].Unit.Value);
    }

    [Fact]
    public void Reads_a_note_without_item_numbers_and_with_the_description_first()
    {
        var json = LayoutResultBuilder.Build(
            "Liimimaailm OÜ\nKMKR nr EE108209119\nTARNEDOKUMENT\nSaatelehe nr: 1858\nTarnekuupäev: 2026-04-12\nTeie tellimus: PO-2026-0004\nSaaja\nNäidis Puit OÜ\nKMKR EE100000001",
            [["Nimetus", "Art. nr", "Ühik", "Kogus"], ["PU ehitusliim", "LM-679-A", "purk", "15"]],
            "Liimimaailm OÜ · KMKR nr EE108209119 · 1 / 1");

        var note = Map(json);

        Assert.Equal("1858", note.DeliveryNoteNumber.Value);
        Assert.Equal(new DateOnly(2026, 4, 12), note.DeliveryDate.Value);
        Assert.Equal("LM-679-A", note.Lines.Single().ItemCode.Value);
        Assert.Equal("PU ehitusliim", note.Lines.Single().Description.Value);
        Assert.Equal(15m, note.Lines.Single().Quantity.Value);
    }

    [Theory]
    [InlineData("Delivery note no .: 3399")]
    [InlineData("Delivery note no. : 3399")]
    [InlineData("Delivery note no.: 3399")]
    [InlineData("Saatelehe nr : 3399")]
    public void The_number_is_found_when_the_ocr_puts_a_space_before_the_dot_or_colon(string label)
    {
        var json = LayoutResultBuilder.Build(
            $"DELIVERY NOTE Nordic Pack Solutions OÜ\n{label}\nDelivery date: 14/04/2026\nVAT no. EE101657339",
            [["Item", "Qty"], ["NPS-624", "80"]], "Nordic Pack Solutions OÜ · VAT no. EE101657339 · 1 / 1");

        Assert.Equal("3399", Map(json).DeliveryNoteNumber.Value);
    }

    [Fact]
    public void A_space_as_thousand_separator_is_read_as_one_number()
    {
        Assert.Equal(1292m, Map(EstonianNote(quantity: "1 292")).Lines[0].Quantity.Value);
        Assert.Equal(1292m, Map(EstonianNote(quantity: "1 292")).Lines[0].Quantity.Value);
    }

    [Fact]
    public void A_quantity_that_cannot_be_read_is_kept_as_text_with_confidence_zero()
    {
        var note = Map(EstonianNote(quantity: "l3,5З"));

        Assert.False(note.Lines[0].Quantity.HasValue);
        Assert.Equal(0, note.Lines[0].Quantity.Confidence);
        Assert.Contains("Line1.Quantity", note.LowConfidenceFields(0.80));
    }

    [Fact]
    public void A_value_found_by_rules_is_only_as_sure_as_its_least_sure_word()
    {
        var note = Map(EstonianNote(new Dictionary<string, double> { ["2959"] = 0.55, ["24,81"] = 0.60 }));

        Assert.Equal(0.55, note.DeliveryNoteNumber.Confidence!.Value, precision: 2);
        Assert.Equal(0.60, note.Lines[1].Quantity.Confidence!.Value, precision: 2);
        Assert.Equal(0.99, note.Lines[0].Quantity.Confidence!.Value, precision: 2);
        Assert.Equal(["DeliveryNoteNumber", "Line2.Quantity"], note.LowConfidenceFields(0.80));
    }

    [Fact]
    public void A_clean_note_has_no_low_confidence_fields()
    {
        Assert.Empty(Map(EstonianNote()).LowConfidenceFields(0.80));
    }

    [Fact]
    public void A_note_without_a_table_has_no_lines_and_says_so()
    {
        var json = JsonNode.Parse(EstonianNote())!;
        json["tables"] = new JsonArray();

        var note = Map(json.ToJsonString());

        Assert.Empty(note.Lines);
        Assert.Contains("Lines", note.LowConfidenceFields(0.80));
    }

    [Fact]
    public void A_missing_header_field_counts_as_unreadable()
    {
        var json = LayoutResultBuilder.Build("Some text without labels", [["Kogus"], ["5"]], "end");

        var note = Map(json);

        Assert.False(note.DeliveryNoteNumber.HasValue);
        Assert.Contains("DeliveryNoteNumber", note.LowConfidenceFields(0.80));
        Assert.Contains("SupplierVatNumber", note.LowConfidenceFields(0.80));
    }

    [Fact]
    public void Reads_the_table_shape_of_a_real_service_result()
    {
        // The saved real invoice result carries the layout part (words, tables) the fallback reads; its documents are dropped here.
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine("Fixtures", "real-invoice-inv-0018.json")))!;
        json["documents"] = new JsonArray();

        var note = Map(json.ToJsonString());

        Assert.Equal(2, note.Lines.Count);
        Assert.Equal("KS-0259", note.Lines[0].ItemCode.Value);
        Assert.Equal(5.62m, note.Lines[0].Quantity.Value);
        Assert.Equal("m3", note.Lines[0].Unit.Value);
        Assert.Equal("EE102412824", note.SupplierVatNumber.Value);
        Assert.Equal("PO-2026-0018", note.PurchaseOrderReference.Value);
    }

    [Fact]
    public void Reads_the_fields_of_a_custom_model_when_the_result_has_them()
    {
        var json = """
            {"documents":[{"docType":"delivery-note-neural-v1:delivery-note-neural-v1","confidence":0.97,"fields":{
              "DeliveryNoteNumber":{"type":"string","valueString":"2959","content":"2959","confidence":0.96},
              "PONumber":{"type":"string","valueString":"PO-2026-0001","content":"PO-2026-0001","confidence":0.91},
              "DeliveryDate":{"type":"date","valueDate":"2026-04-07","content":"07.04.2026","confidence":0.93},
              "SupplierName":{"type":"string","valueString":"Sillaotsa Saeveski OÜ","content":"Sillaotsa Saeveski OÜ","confidence":0.9},
              "SupplierVatNumber":{"type":"string","valueString":"EE102412824","content":"EE102412824","confidence":0.94},
              "Lines":{"type":"array","valueArray":[
                {"type":"object","valueObject":{
                  "ItemCode":{"type":"string","valueString":"KS-0253","content":"KS-0253","confidence":0.9},
                  "Description":{"type":"string","valueString":"Kaselaud 25x100 mm","content":"Kaselaud 25x100 mm","confidence":0.9},
                  "Quantity":{"type":"number","valueNumber":13.53,"content":"13,53","confidence":0.88},
                  "Unit":{"type":"string","valueString":"m³","content":"m³","confidence":0.9}}},
                {"type":"object","valueObject":{
                  "ItemCode":{"type":"string","valueString":"KS-0297","content":"KS-0297","confidence":0.9},
                  "Quantity":{"type":"string","valueString":"1 292","content":"1 292","confidence":0.7},
                  "Unit":{"type":"string","valueString":"tk","content":"tk","confidence":0.9}}}]}}}]}
            """;

        var note = Map(json, "delivery-note-neural-v1");

        Assert.Equal("delivery-note-neural-v1", note.ModelId);
        Assert.Equal("2959", note.DeliveryNoteNumber.Value);
        Assert.Equal(0.96, note.DeliveryNoteNumber.Confidence!.Value, precision: 2);
        Assert.Equal("PO-2026-0001", note.PurchaseOrderReference.Value);
        Assert.Equal(new DateOnly(2026, 4, 7), note.DeliveryDate.Value);
        Assert.Equal("EE102412824", note.SupplierVatNumber.Value);
        Assert.Equal(2, note.Lines.Count);
        Assert.Equal(13.53m, note.Lines[0].Quantity.Value);
        Assert.Equal(0.88, note.Lines[0].Quantity.Confidence!.Value, precision: 2);
        Assert.Equal(1292m, note.Lines[1].Quantity.Value);
        Assert.Equal(["Line2.Quantity"], note.LowConfidenceFields(0.80));
    }

    [Fact]
    public void Keeps_the_raw_json_and_the_model_id()
    {
        var json = EstonianNote();
        var note = Map(json);

        Assert.Equal(json, note.RawJson);
        Assert.Equal("prebuilt-layout", note.ModelId);
    }

    [Fact]
    public void Accepts_the_studio_export_that_wraps_the_result()
    {
        var wrapped = $$"""{"status":"succeeded","analyzeResult":{{EstonianNote()}}}""";

        Assert.Equal("2959", Map(wrapped).DeliveryNoteNumber.Value);
    }

    [Fact]
    public void Garbage_input_throws_instead_of_returning_an_empty_note()
    {
        Assert.ThrowsAny<Exception>(() => Map("not json"));
    }
}
