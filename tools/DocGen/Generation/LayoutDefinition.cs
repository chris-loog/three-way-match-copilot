using DocGen.Seeding;

namespace DocGen.Generation;

internal enum Column
{
    Position,
    Code,
    Description,
    Quantity,
    Unit,
    UnitPrice,
    VatRate,
    Amount,
}

internal enum HeaderStyle
{
    SupplierLeftTitleRight,
    TitleLeftSupplierRight,
    Centered,
}

/// <summary>The words a layout prints. One set per language, with per-layout overrides.</summary>
internal sealed record Labels(
    string InvoiceTitle,
    string InvoiceNumber,
    string InvoiceDate,
    string DueDate,
    string PoReference,
    string VatNumber,
    string RegistryCode,
    string Iban,
    string BillTo,
    string Position,
    string Code,
    string Description,
    string Quantity,
    string Unit,
    string UnitPrice,
    string VatRate,
    string Amount,
    string Net,
    string Vat,
    string Gross,
    string DeliveryNoteTitle,
    string DeliveryNoteNumber,
    string DeliveryDate,
    string DeliveredTo,
    string Received,
    string PaymentNote);

/// <summary>How one supplier's documents look: language, number and date formats, column order, colours.</summary>
internal sealed record LayoutDefinition(
    string Id,
    string Culture,
    string DateFormat,
    string AccentColor,
    HeaderStyle Header,
    IReadOnlyList<Column> InvoiceColumns,
    IReadOnlyList<Column> DeliveryNoteColumns,
    bool GridLines,
    Labels Labels,
    bool HeldOut = false)
{
    private static readonly Labels Estonian = new(
        InvoiceTitle: "ARVE", InvoiceNumber: "Arve nr", InvoiceDate: "Arve kuupäev", DueDate: "Maksetähtaeg",
        PoReference: "Tellimus nr", VatNumber: "KMKR nr", RegistryCode: "Registrikood", Iban: "Arveldusarve (IBAN)",
        BillTo: "Ostja", Position: "Nr", Code: "Kood", Description: "Nimetus", Quantity: "Kogus", Unit: "Ühik",
        UnitPrice: "Hind", VatRate: "KM %", Amount: "Summa", Net: "Summa käibemaksuta", Vat: "Käibemaks 24%",
        Gross: "Arve summa kokku", DeliveryNoteTitle: "SAATELEHT", DeliveryNoteNumber: "Saatelehe nr",
        DeliveryDate: "Tarnekuupäev", DeliveredTo: "Saaja", Received: "Kauba vastu võttis",
        PaymentNote: "Palume arve tasuda tähtajaks. Viivis 0,2% päevas.");

    private static readonly Labels English = new(
        InvoiceTitle: "INVOICE", InvoiceNumber: "Invoice no.", InvoiceDate: "Invoice date", DueDate: "Due date",
        PoReference: "Your order", VatNumber: "VAT no.", RegistryCode: "Reg. code", Iban: "Bank account (IBAN)",
        BillTo: "Bill to", Position: "Pos", Code: "Item code", Description: "Description", Quantity: "Qty", Unit: "Unit",
        UnitPrice: "Unit price", VatRate: "VAT %", Amount: "Amount", Net: "Total excl. VAT", Vat: "VAT 24%",
        Gross: "Total due", DeliveryNoteTitle: "DELIVERY NOTE", DeliveryNoteNumber: "Delivery note no.",
        DeliveryDate: "Delivery date", DeliveredTo: "Deliver to", Received: "Goods received by",
        PaymentNote: "Payment within the due date, please.");

    /// <summary>The five layouts. Hardware and transport share one, as if both use the same invoicing software.</summary>
    public static IReadOnlyList<LayoutDefinition> All { get; } =
    [
        // L1: Estonian timber mill. Supplier block left, title right, classic grid.
        new("L1", "et-EE", "dd.MM.yyyy", "#2F5D3A", HeaderStyle.SupplierLeftTitleRight,
            [Column.Position, Column.Code, Column.Description, Column.Quantity, Column.Unit, Column.UnitPrice, Column.Amount],
            [Column.Position, Column.Code, Column.Description, Column.Quantity, Column.Unit],
            GridLines: true, Estonian),

        // L2: English panel supplier. Title left, VAT column, light lines.
        new("L2", "en-GB", "d MMM yyyy", "#1F3A68", HeaderStyle.TitleLeftSupplierRight,
            [Column.Position, Column.Code, Column.Description, Column.Quantity, Column.Unit, Column.UnitPrice, Column.VatRate, Column.Amount],
            [Column.Position, Column.Code, Column.Description, Column.Unit, Column.Quantity],
            GridLines: false, English with { PoReference = "Your order", Gross = "Total payable" }),

        // L3: Estonian glue and finishes. Held out of all training. Centred header, description first, ISO dates.
        new("L3", "et-EE", "yyyy-MM-dd", "#C2571A", HeaderStyle.Centered,
            [Column.Description, Column.Code, Column.Unit, Column.Quantity, Column.UnitPrice, Column.VatRate, Column.Amount],
            [Column.Description, Column.Code, Column.Unit, Column.Quantity],
            GridLines: false, Estonian with { InvoiceTitle = "MÜÜGIARVE", PoReference = "Teie tellimus", Code = "Art. nr", Amount = "Kokku", DeliveryNoteTitle = "TARNEDOKUMENT" },
            HeldOut: true),

        // L4: Estonian hardware and transport. Short d.M.yyyy dates, article before description.
        new("L4", "et-EE", "d.M.yyyy", "#3B3B3B", HeaderStyle.SupplierLeftTitleRight,
            [Column.Position, Column.Code, Column.Description, Column.Quantity, Column.Unit, Column.UnitPrice, Column.Amount],
            [Column.Position, Column.Code, Column.Description, Column.Quantity, Column.Unit],
            GridLines: true, Estonian with { PoReference = "Tellimuse number", Code = "Artikkel", Description = "Kirjeldus", Amount = "Kokku" }),

        // L5: English packaging supplier. dd/MM/yyyy dates (ambiguous on purpose), no position column.
        new("L5", "en-GB", "dd/MM/yyyy", "#1B7F79", HeaderStyle.TitleLeftSupplierRight,
            [Column.Code, Column.Description, Column.Unit, Column.Quantity, Column.UnitPrice, Column.Amount],
            [Column.Code, Column.Description, Column.Unit, Column.Quantity],
            GridLines: false, English with { PoReference = "PO ref", Code = "Item", Amount = "Total" }),
    ];

    public static LayoutDefinition For(string supplierKey) => All.Single(l => l.Id == supplierKey switch
    {
        SeedCatalogue.TimberKey => "L1",
        SeedCatalogue.PanelsKey => "L2",
        SeedCatalogue.GlueKey => "L3",
        SeedCatalogue.HardwareKey or SeedCatalogue.TransportKey => "L4",
        SeedCatalogue.PackagingKey => "L5",
        _ => throw new ArgumentOutOfRangeException(nameof(supplierKey)),
    });

    public System.Globalization.CultureInfo CultureInfo => System.Globalization.CultureInfo.GetCultureInfo(Culture);

    public string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo);

    /// <summary>Money and prices: two decimals, or four when the price has sub-cent precision.</summary>
    public string FormatMoney(decimal value, bool allowFourDecimals = false)
    {
        var decimals = allowFourDecimals && Math.Round(value, 2) != value ? 4 : 2;
        return value.ToString("N" + decimals, CultureInfo);
    }

    public string FormatQuantity(decimal value) =>
        value == Math.Truncate(value) ? value.ToString("N0", CultureInfo) : value.ToString("N2", CultureInfo);

    public string FormatVatRate(decimal rate) => (rate * 100).ToString("0", CultureInfo);

    public static string FormatIban(string iban) =>
        string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - i * 4))));
}
