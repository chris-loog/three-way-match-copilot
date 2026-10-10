using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;
using ThreeWayMatch.Domain.Matching;

namespace ThreeWayMatch.Domain.Tests.Matching;

/// <summary>
/// A small ERP built by hand for the matching tests: two suppliers and a few orders, each shaped for one case.
/// Entities are linked by reference and id, the way the database loader will link them.
/// </summary>
internal sealed class MatchingWorld
{
    public const string TimberVat = "EE102412824";
    public const string PanelVat = "EE109532815";

    public Supplier Timber { get; }
    public Supplier Panels { get; }

    /// <summary>Spruce board, sold and counted in m³.</summary>
    public SupplierItem Board { get; }

    /// <summary>Spruce lath, sold in packs of 0.25 m³ (base unit m³).</summary>
    public SupplierItem Lath { get; }

    /// <summary>Pine board: on exactly one order, so a missing PO reference can be resolved.</summary>
    public SupplierItem Pine { get; }

    /// <summary>The delivery charge, sold per trip.</summary>
    public SupplierItem Freight { get; }

    public SupplierItem Plywood { get; }

    /// <summary>Board 10 m³, lath 20 packs, freight 1 trip. Board fully received, lath 8 of 20 packs, freight received.</summary>
    public PurchaseOrder Po1 { get; }

    /// <summary>Board 5 m³ only: no charge line, no lath.</summary>
    public PurchaseOrder Po2 { get; }

    /// <summary>Pine 12 m³ only.</summary>
    public PurchaseOrder Po3 { get; }

    /// <summary>Board on two lines, 10 m³ each: a tie.</summary>
    public PurchaseOrder Po4 { get; }

    /// <summary>Board on two lines, 6 and 10 m³: the best fit decides.</summary>
    public PurchaseOrder Po5 { get; }

    /// <summary>The other supplier's order.</summary>
    public PurchaseOrder Po6 { get; }

    public MatchingWorld()
    {
        Timber = new Supplier { Id = 1, Name = "Sillaotsa Saeveski OÜ", Category = "Sawn timber", VatNumber = TimberVat, RegistryCode = "11812518", Iban = "EE780400150311134947", DocumentCulture = "et-EE" };
        Panels = new Supplier { Id = 2, Name = "Baltic Board Supply OÜ", Category = "Panels", VatNumber = PanelVat, RegistryCode = "10776889", Iban = "EE330400150311134111", DocumentCulture = "en-GB" };

        Board = Sell(1, Timber, new Item { Id = 1, Code = "TMB-0001", Name = "Spruce board 25x150 mm, dried, 4.8 m", BaseUnit = "m³" }, "KS-0101", "Kuuselaud 25x150 mm, kuivatatud, 4,8 m", "m³", 1m);
        Lath = Sell(2, Timber, new Item { Id = 2, Code = "TMB-0002", Name = "Spruce lath 22x45 mm", BaseUnit = "m³" }, "KS-0102", "Kuuseliist 22x45 mm", "pakk", 0.25m);
        Pine = Sell(3, Timber, new Item { Id = 3, Code = "TMB-0003", Name = "Pine board 25x125 mm, planed", BaseUnit = "m³" }, "KS-0103", "Männilaud 25x125 mm, hööveldatud", "m³", 1m);
        Freight = Sell(4, Timber, new Item { Id = 4, Code = "FRT-0001", Name = "Delivery charge (timber)", BaseUnit = "trip" }, "KS-0199", "Transport", "reis", 1m);
        Plywood = Sell(5, Panels, new Item { Id = 5, Code = "PNL-0001", Name = "Birch plywood 18 mm", BaseUnit = "pcs" }, "BBS00157", "Birch plywood 18 mm, 1220x2440", "pcs", 1m);

        Po1 = Order(1, "PO-2026-0001", Timber);
        var boardLine = Add(Po1, 11, 1, Board, 10m);
        var lathLine = Add(Po1, 12, 2, Lath, 20m);
        var freightLine = Add(Po1, 13, 3, Freight, 1m);
        Receive(boardLine, 10m);
        Receive(lathLine, 8m);
        Receive(freightLine, 1m);

        Po2 = Order(2, "PO-2026-0002", Timber);
        Add(Po2, 21, 1, Board, 5m);

        Po3 = Order(3, "PO-2026-0003", Timber);
        Add(Po3, 31, 1, Pine, 12m);

        Po4 = Order(4, "PO-2026-0004", Timber);
        Add(Po4, 41, 1, Board, 10m);
        Add(Po4, 42, 2, Board, 10m);

        Po5 = Order(5, "PO-2026-0005", Timber);
        Add(Po5, 51, 1, Board, 6m);
        Add(Po5, 52, 2, Board, 10m);

        Po6 = Order(6, "PO-2026-0006", Panels);
        Add(Po6, 61, 1, Plywood, 100m);
    }

    public MatchingSnapshot Snapshot(IReadOnlyDictionary<int, decimal>? invoiced = null, IReadOnlyList<ItemAlias>? aliases = null, IReadOnlyList<PurchaseOrder>? orders = null) => new()
    {
        Suppliers = [Timber, Panels],
        SupplierItems = [Board, Lath, Pine, Freight, Plywood],
        Aliases = aliases ?? [],
        PurchaseOrders = orders ?? [Po1, Po2, Po3, Po4, Po5, Po6],
        InvoicedQuantityByPurchaseOrderLine = invoiced ?? new Dictionary<int, decimal>(),
    };

    public static MatchInputLine Line(int number, string? code, string? description, decimal? quantity, string? unit, decimal? price = null) =>
        new(number, code, description, quantity, unit, price);

    public static MatchInput Invoice(string? vat, string? poReference, params MatchInputLine[] lines) =>
        new(vat, null, poReference, lines);

    private static SupplierItem Sell(int id, Supplier supplier, Item item, string code, string description, string unit, decimal factor) => new()
    {
        Id = id, Supplier = supplier, SupplierId = supplier.Id, Item = item, ItemId = item.Id,
        SupplierItemCode = code, SupplierDescription = description, SupplierUnit = unit, ConversionFactorToBase = factor,
    };

    private static PurchaseOrder Order(int id, string number, Supplier supplier) =>
        new() { Id = id, Number = number, Supplier = supplier, SupplierId = supplier.Id, Currency = "EUR" };

    private static PurchaseOrderLine Add(PurchaseOrder order, int id, int number, SupplierItem item, decimal quantity)
    {
        var line = new PurchaseOrderLine
        {
            Id = id, PurchaseOrder = order, PurchaseOrderId = order.Id, LineNumber = number,
            SupplierItem = item, SupplierItemId = item.Id, Description = item.SupplierDescription,
            Quantity = quantity, Unit = item.SupplierUnit, UnitPrice = 100m, VatRate = 0.24m,
        };
        order.Lines.Add(line);
        return line;
    }

    private static void Receive(PurchaseOrderLine line, decimal quantity) =>
        line.ReceiptLines.Add(new GoodsReceiptLine { PurchaseOrderLine = line, PurchaseOrderLineId = line.Id, Quantity = quantity });
}
