using System.Text.Json.Nodes;
using Azure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using ThreeWayMatch.Domain.Erp;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Infrastructure.DocumentIntelligence;
using ThreeWayMatch.Infrastructure.Persistence;
using ThreeWayMatch.Infrastructure.Pipeline;

namespace ThreeWayMatch.Domain.Tests.Pipeline;

/// <summary>
/// A small ERP in an in-memory database that fits the invoice in <c>Fixtures/pipeline-invoice.json</c>:
/// supplier Baltic Board Supply, order PO-2026-0003 with plywood, MDF and a delivery charge, all fully received.
/// </summary>
internal sealed class PipelineWorld : IDisposable
{
    public const string SupplierVat = "EE109532815";

    private readonly string _databaseName = Guid.NewGuid().ToString("N");
    private readonly DbContextOptions<ThreeWayMatchDbContext> _options;

    public PipelineWorld(bool received = true)
    {
        _options = new DbContextOptionsBuilder<ThreeWayMatchDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var db = NewContext();
        SeedErp(db, received);
    }

    /// <summary>Fills an empty database with the supplier, the order and (optionally) its full goods receipt.</summary>
    public static void SeedErp(ThreeWayMatchDbContext db, bool received = true)
    {
        var supplier = new Supplier
        {
            Name = "Baltic Board Supply OÜ", Category = "Panels", VatNumber = SupplierVat, RegistryCode = "10776889",
            Iban = "EE780400150311134947", DocumentCulture = "en-GB",
        };
        var order = new PurchaseOrder { Number = "PO-2026-0003", Supplier = supplier, OrderDate = new DateOnly(2026, 4, 1), Currency = "EUR" };
        var receipt = new GoodsReceipt { Number = "GR-1", PurchaseOrder = order, ReceivedDate = new DateOnly(2026, 4, 10) };

        foreach (var (code, name, unit, quantity, price, number) in new[]
        {
            ("BBS00157", "Spruce plywood 15 mm, 1220x2440", "pcs", 283m, 36m, 1),
            ("BBS00192", "MDF 16 mm raw, 2800x2070", "pcs", 185m, 34m, 2),
            ("BBS00332", "Delivery charge", "trip", 1m, 85m, 3),
        })
        {
            var supplierItem = new SupplierItem
            {
                Supplier = supplier, Item = new Item { Code = $"I-{code}", Name = name, BaseUnit = unit },
                SupplierItemCode = code, SupplierDescription = name, SupplierUnit = unit, ConversionFactorToBase = 1m,
            };
            var line = new PurchaseOrderLine
            {
                PurchaseOrder = order, SupplierItem = supplierItem, LineNumber = number, Description = name,
                Quantity = quantity, Unit = unit, UnitPrice = price, VatRate = 0.24m,
                Status = received ? PurchaseOrderLineStatus.Received : PurchaseOrderLineStatus.Open,
            };
            order.Lines.Add(line);
            if (received)
            {
                receipt.Lines.Add(new GoodsReceiptLine { PurchaseOrderLine = line, Quantity = quantity });
            }
        }

        db.Add(order);
        if (received)
        {
            db.Add(receipt);
        }

        db.SaveChanges();
    }

    public ThreeWayMatchDbContext NewContext() => new(_options);

    public static DocumentMatcher NewMatcher(ThreeWayMatchDbContext db) =>
        new(db, DocumentAssessor.CreateDefault(), new SnapshotLoader(db), new DiscrepancyOptions(),
            TimeProvider.System, NullLogger<DocumentMatcher>.Instance);

    public static DocumentProcessor NewProcessor(ThreeWayMatchDbContext db, IDocumentExtractor extractor) =>
        new(db, extractor, NewMatcher(db), TimeProvider.System, NullLogger<DocumentProcessor>.Instance);

    /// <summary>The goods arrive late: books the full receipt for the order.</summary>
    public void BookFullReceipt()
    {
        using var db = NewContext();
        var order = db.PurchaseOrders.Include(o => o.Lines).Single();
        var receipt = new GoodsReceipt { PurchaseOrderId = order.Id, Number = "GR-LATE", ReceivedDate = new DateOnly(2026, 4, 14) };
        foreach (var line in order.Lines)
        {
            receipt.Lines.Add(new GoodsReceiptLine { PurchaseOrderLineId = line.Id, Quantity = line.Quantity });
        }

        db.Add(receipt);
        db.SaveChanges();
    }

    /// <summary>The fixture as the service returned it: one smudged group in the IBAN, so the extraction is not sure.</summary>
    public static string SmudgedResult() => File.ReadAllText(Path.Combine("Fixtures", "pipeline-invoice.json"));

    /// <summary>The same invoice read perfectly: every word at 0.95 or better.</summary>
    public static string CleanResult()
    {
        var json = JsonNode.Parse(SmudgedResult())!;
        foreach (var word in json["pages"]!.AsArray().SelectMany(p => p!["words"]!.AsArray()))
        {
            word!["confidence"] = 0.95;
        }

        return json.ToJsonString();
    }

    public static string WithInvoiceNumber(string json, string number) => json.Replace("BBS/2026/0354", number, StringComparison.Ordinal);

    public void Dispose()
    {
    }
}

/// <summary>Stands in for Document Intelligence: returns a prepared raw result, or throws, and counts the calls.</summary>
internal sealed class FakeExtractor(string rawJson, Exception? failure = null, string? deliveryNoteJson = null) : IDocumentExtractor
{
    public int Calls { get; private set; }

    public Task<ExtractedInvoice> ExtractInvoiceAsync(Stream document, CancellationToken cancellationToken = default)
    {
        Calls++;
        return failure is null
            ? Task.FromResult(InvoiceMapper.Map(rawJson, "prebuilt-invoice"))
            : Task.FromException<ExtractedInvoice>(failure);
    }

    public Task<ExtractedDeliveryNote> ExtractDeliveryNoteAsync(Stream document, CancellationToken cancellationToken = default)
    {
        Calls++;
        return failure is not null
            ? Task.FromException<ExtractedDeliveryNote>(failure)
            : Task.FromResult(DeliveryNoteMapper.Map(deliveryNoteJson ?? throw new InvalidOperationException("No delivery note result prepared."), "prebuilt-layout"));
    }
}
