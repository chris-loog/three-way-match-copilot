namespace DocGen.Seeding;

/// <summary>One fictional supplier. <see cref="CodeFormat"/> shapes the supplier's own item codes.</summary>
internal sealed record SupplierSpec(
    string Key,
    string Name,
    string Category,
    string DocumentCulture,
    string CodeFormat,
    string ItemCodePrefix,
    int PoWeight,
    bool SellsFreight);

/// <summary>
/// One item as one supplier sells it. Quantities and prices are in the supplier's unit.
/// <see cref="Factor"/> converts the supplier's unit to the item's base unit.
/// </summary>
internal sealed record ItemSpec(
    string SupplierKey,
    string Name,
    string SupplierDescription,
    string BaseUnit,
    string SupplierUnit,
    decimal Factor,
    decimal Price,
    decimal MinQty,
    decimal MaxQty,
    int QtyDecimals,
    bool IsCharge = false);

/// <summary>
/// The fixed part of the seed data: who sells what. Everything here is invented.
/// Bogus supplies the random parts (identifiers, order dates, quantities, which POs get which delivery pattern).
/// </summary>
internal static class SeedCatalogue
{
    public const string TimberKey = "timber";
    public const string PanelsKey = "panels";
    public const string GlueKey = "glue";
    public const string HardwareKey = "hardware";
    public const string PackagingKey = "packaging";
    public const string TransportKey = "transport";

    public static IReadOnlyList<SupplierSpec> Suppliers { get; } =
    [
        new(TimberKey, "Sillaotsa Saeveski OÜ", "Sawn timber", "et-EE", "KS-{0:D4}", "TMB", 30, true),
        new(PanelsKey, "Baltic Board Supply OÜ", "Panels", "en-GB", "BBS{0:D5}", "PNL", 22, true),
        new(GlueKey, "Liimimaailm OÜ", "Glue and finishes", "et-EE", "LM-{0:D3}-A", "GLU", 10, true),
        new(HardwareKey, "Naelakuningas OÜ", "Hardware", "et-EE", "NK{0:D6}", "HRD", 18, true),
        new(PackagingKey, "Nordic Pack Solutions OÜ", "Packaging", "en-GB", "NPS-{0:D3}", "PKG", 10, true),
        new(TransportKey, "Veokas Logistika OÜ", "Transport", "et-EE", "VL{0:D3}", "TRN", 10, false),
    ];

    public static IReadOnlyList<ItemSpec> Items { get; } = BuildItems();

    private static List<ItemSpec> BuildItems()
    {
        var items = new List<ItemSpec>();

        // Sawn timber: base unit m³. Three items are sold in packs, e.g. 1 pack = 0.25 m³.
        items.AddRange(
        [
            Timber("Spruce board 25x150 mm, dried, 4.8 m", "Kuuselaud 25x150 mm, kuivatatud, 4,8 m", "m³", 1m, 410m),
            Timber("Spruce board 50x150 mm, dried, 4.8 m", "Kuuselaud 50x150 mm, kuivatatud, 4,8 m", "m³", 1m, 395m),
            Timber("Spruce beam 100x100 mm, 4.8 m", "Kuusepalk 100x100 mm, 4,8 m", "m³", 1m, 380m),
            Timber("Pine board 25x125 mm, planed", "Männilaud 25x125 mm, hööveldatud", "m³", 1m, 520m),
            Timber("Pine board 38x150 mm, planed", "Männilaud 38x150 mm, hööveldatud", "m³", 1m, 540m),
            Timber("Pine beam 75x150 mm", "Männipalk 75x150 mm", "m³", 1m, 510m),
            Timber("Birch board 25x100 mm, kiln-dried", "Kaselaud 25x100 mm, kamberkuivatatud", "m³", 1m, 690m),
            Timber("Birch board 40x120 mm, kiln-dried", "Kaselaud 40x120 mm, kamberkuivatatud", "m³", 1m, 720m),
            Timber("Oak board 27 mm, dried", "Tammelaud 27 mm, kuivatatud", "m³", 1m, 1250m),
            Timber("Ash board 32 mm, dried", "Saarelaud 32 mm, kuivatatud", "m³", 1m, 980m),
            Timber("Spruce lath 22x45 mm", "Kuuseliist 22x45 mm", "pakk", 0.25m, 98m, 8, 60, 0),
            Timber("Pine lath 28x70 mm", "Männiliist 28x70 mm", "pakk", 0.30m, 152m, 6, 40, 0),
            Timber("Spruce board 25x100 mm, packed", "Kuuselaud 25x100 mm, pakitud", "pakk", 0.50m, 196m, 4, 30, 0),
        ]);

        // Panels: base unit pcs (sheets). Two items are sold per pallet.
        items.AddRange(
        [
            Panel("Birch plywood 18 mm, 1220x2440", "Birch plywood 18 mm, 1220x2440", 1m, 58m, "pcs"),
            Panel("Birch plywood 12 mm, 1220x2440", "Birch plywood 12 mm, 1220x2440", 1m, 41m, "pcs"),
            Panel("Birch plywood 9 mm, 1220x2440", "Birch plywood 9 mm, 1220x2440", 1m, 33m, "pcs"),
            Panel("Spruce plywood 15 mm, 1220x2440", "Spruce plywood 15 mm, 1220x2440", 1m, 36m, "pcs"),
            Panel("MDF 16 mm raw, 2800x2070", "MDF 16 mm raw, 2800x2070", 1m, 34m, "pcs"),
            Panel("MDF 19 mm raw, 2800x2070", "MDF 19 mm raw, 2800x2070", 1m, 39m, "pcs"),
            Panel("MDF 22 mm raw, 2800x2070", "MDF 22 mm raw, 2800x2070", 1m, 44m, "pcs"),
            Panel("Chipboard 18 mm white melamine, 2800x2070", "Chipboard 18 mm white melamine, 2800x2070", 50m, 1250m, "pallet", 1, 6),
            Panel("Chipboard 18 mm oak melamine, 2800x2070", "Chipboard 18 mm oak melamine, 2800x2070", 1m, 31m, "pcs"),
            Panel("Oak veneered MDF 19 mm, 2440x1220", "Oak veneered MDF 19 mm, 2440x1220", 1m, 88m, "pcs"),
            Panel("Edge-glued pine panel 18 mm, 2000x600", "Edge-glued pine panel 18 mm, 2000x600", 40m, 2150m, "pallet", 1, 4),
        ]);

        // Glue and finishes: base unit litre. Mostly sold in cans of 5, 10 or 20 litres.
        items.AddRange(
        [
            Glue("PVAC wood glue D3", "PVAC puiduliim D3", "purk", 10m, 42m),
            Glue("PVAC wood glue D4", "PVAC puiduliim D4", "purk", 10m, 51m),
            Glue("PU construction glue", "PU ehitusliim", "purk", 5m, 58m),
            Glue("Water-based lacquer, matt", "Veepõhine lakk, matt", "purk", 10m, 98m),
            Glue("Water-based lacquer, gloss", "Veepõhine lakk, läige", "purk", 10m, 104m),
            Glue("Wood oil, natural", "Puiduõli, naturaalne", "purk", 5m, 73m),
            Glue("Hard wax oil", "Kõvavahaõli", "purk", 5m, 95m),
            Glue("Wood stain, walnut", "Puidupeits, pähkel", "l", 1m, 7.5m),
            Glue("Primer, water-based", "Krunt, veepõhine", "purk", 20m, 124m),
        ]);

        // Hardware: base unit pcs. Small parts come in boxes.
        items.AddRange(
        [
            Hardware("Confirmat screw 7x50", "Konfirmaatkruvi 7x50", "karp", 200m, 8.4m),
            Hardware("Chipboard screw 4x40", "Spoonplaadikruvi 4x40", "karp", 500m, 11.5m),
            Hardware("Soft-close hinge 35 mm", "Vaikse sulgemisega hing 35 mm", "tk", 1m, 1.9m, 200, 2000),
            Hardware("Drawer slide 450 mm", "Sahtlisiin 450 mm", "tk", 1m, 6.4m, 50, 600),
            Hardware("Handle bar 128 mm", "Käepide 128 mm", "tk", 1m, 2.2m, 100, 1500),
            Hardware("Cam lock 15 mm", "Ekstsentrikuhk 15 mm", "karp", 100m, 12m),
            Hardware("Wooden dowel 8x35", "Puitdüübel 8x35", "karp", 500m, 9.6m),
            Hardware("Shelf support pin 5 mm", "Riiuli tugitihvt 5 mm", "karp", 200m, 5.2m),
            Hardware("Corner bracket 40x40", "Nurgakinnitus 40x40", "tk", 1m, 0.35m, 200, 3000),
            Hardware("Adjustable leg 100 mm", "Reguleeritav jalg 100 mm", "tk", 1m, 0.9m, 200, 2500),
            Hardware("Hanging rail 2 m", "Riputusliist 2 m", "tk", 1m, 3.8m, 50, 500),
        ]);

        // Packaging: base unit pcs.
        items.AddRange(
        [
            Packaging("EUR pallet 1200x800", "EUR pallet 1200x800", "pcs", 1m, 11m, 20, 200),
            Packaging("Cardboard box 600x400x300", "Cardboard box 600x400x300", "pack", 25m, 22.5m, 2, 30),
            Packaging("Corner protector 40 mm", "Corner protector 40 mm", "pack", 100m, 14m, 2, 20),
            Packaging("Stretch film 500 mm", "Stretch film 500 mm", "pcs", 1m, 6.5m, 20, 200),
            Packaging("Strapping band 12 mm", "Strapping band 12 mm", "pcs", 1m, 18m, 5, 60),
            Packaging("Foam sheet 5 mm", "Foam sheet 5 mm", "pack", 10m, 31m, 2, 25),
            Packaging("Bubble wrap roll 1 m", "Bubble wrap roll 1 m", "pcs", 1m, 24m, 5, 40),
        ]);

        // Transport services: counted in trips.
        items.AddRange(
        [
            Transport("Transport Tallinn - Tartu", "Transport Tallinn - Tartu", 180m),
            Transport("Transport Tallinn - Pärnu", "Transport Tallinn - Pärnu", 160m),
            Transport("Transport Tartu - Narva", "Transport Tartu - Narva", 210m),
            Transport("Transport Tallinn - Kuressaare", "Transport Tallinn - Kuressaare", 290m),
            Transport("Express delivery within Harju county", "Kiirkohaletoimetamine Harjumaal", 95m),
        ]);

        // Delivery charge, one per material supplier. Sold as a PO line like anything else.
        items.AddRange(
        [
            Freight(TimberKey, "Transport", 120m),
            Freight(PanelsKey, "Delivery charge", 85m),
            Freight(GlueKey, "Transport", 45m),
            Freight(HardwareKey, "Transport", 38m),
            Freight(PackagingKey, "Delivery charge", 60m),
        ]);

        return items;
    }

    private static ItemSpec Timber(string name, string description, string unit, decimal factor, decimal price,
        decimal minQty = 4, decimal maxQty = 30, int qtyDecimals = 2) =>
        new(TimberKey, name, description, "m³", unit, factor, price, minQty, maxQty, qtyDecimals);

    private static ItemSpec Panel(string name, string description, decimal factor, decimal price, string unit,
        decimal minQty = 20, decimal maxQty = 300) =>
        new(PanelsKey, name, description, "pcs", unit, factor, price, minQty, maxQty, 0);

    private static ItemSpec Glue(string name, string description, string unit, decimal litres, decimal price) =>
        new(GlueKey, name, description, "l", unit, litres, price, unit == "l" ? 20 : 2, unit == "l" ? 120 : 24, 0);

    private static ItemSpec Hardware(string name, string description, string unit, decimal perUnit, decimal price,
        decimal minQty = 4, decimal maxQty = 40) =>
        new(HardwareKey, name, description, "pcs", unit, perUnit, price, minQty, maxQty, 0);

    private static ItemSpec Packaging(string name, string description, string unit, decimal perUnit, decimal price,
        decimal minQty, decimal maxQty) =>
        new(PackagingKey, name, description, "pcs", unit, perUnit, price, minQty, maxQty, 0);

    private static ItemSpec Transport(string name, string description, decimal price) =>
        new(TransportKey, name, description, "trip", "reis", 1m, price, 1, 3, 0);

    private static ItemSpec Freight(string supplierKey, string description, decimal price) =>
        new(supplierKey, $"Delivery charge ({supplierKey})", description, "trip", supplierKey is GlueKey or HardwareKey or TimberKey ? "reis" : "trip",
            1m, price, 1, 1, 0, IsCharge: true);
}
