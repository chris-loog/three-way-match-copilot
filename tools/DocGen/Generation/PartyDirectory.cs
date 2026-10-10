using DocGen.Seeding;
using ThreeWayMatch.Domain.Erp;

namespace DocGen.Generation;

/// <summary>Addresses and numbering styles that only matter on the printed page. All invented.</summary>
internal static class PartyDirectory
{
    public static string[] BuyerLines { get; } = ["Näidis Puit OÜ", "Tehase 12, 11415 Tallinn", "KMKR EE100000001 · Reg. 10000001"];

    public static string[] AddressOf(Supplier supplier) => KeyOf(supplier) switch
    {
        SeedCatalogue.TimberKey => ["Saeveski tee 4", "65501 Võru"],
        SeedCatalogue.PanelsKey => ["Sadama 25", "10111 Tallinn"],
        SeedCatalogue.GlueKey => ["Tööstuse 8", "50409 Tartu"],
        SeedCatalogue.HardwareKey => ["Mäe 17", "80042 Pärnu"],
        SeedCatalogue.PackagingKey => ["Kopli 90", "10412 Tallinn"],
        SeedCatalogue.TransportKey => ["Veokite tee 3", "74001 Maardu"],
        _ => ["", ""],
    };

    /// <summary>Each supplier numbers its invoices in its own style.</summary>
    public static string InvoiceNumber(Supplier supplier, int sequence) => KeyOf(supplier) switch
    {
        SeedCatalogue.TimberKey => $"A-{sequence}",
        SeedCatalogue.PanelsKey => $"BBS/2026/{sequence:D4}",
        SeedCatalogue.GlueKey => $"LM{sequence:D5}",
        SeedCatalogue.HardwareKey => $"NK-26-{sequence:D4}",
        SeedCatalogue.PackagingKey => $"NPS{sequence:D6}",
        SeedCatalogue.TransportKey => $"VL-{sequence:D3}",
        _ => sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public static string KeyOf(Supplier supplier) => SeedCatalogue.Suppliers.Single(s => s.Name == supplier.Name).Key;
}
