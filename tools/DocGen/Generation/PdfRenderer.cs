using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DocGen.Generation;

/// <summary>Draws invoices and delivery notes as PDFs. Layout differences come from <see cref="LayoutDefinition"/>.</summary>
internal enum Align
{
    Left,
    Right,
    Center,
}

internal static class TextAlignExtensions
{
    public static void Aligned(this TextDescriptor text, Align align)
    {
        switch (align)
        {
            case Align.Right: text.AlignRight(); break;
            case Align.Center: text.AlignCenter(); break;
            default: text.AlignLeft(); break;
        }
    }

    public static TextBlockDescriptor Aligned(this TextBlockDescriptor text, Align align) => align switch
    {
        Align.Right => text.AlignRight(),
        Align.Center => text.AlignCenter(),
        _ => text.AlignLeft(),
    };
}

internal static class PdfRenderer
{
    private const string Grey = "#666666";
    private const string LightGrey = "#D8D8D8";

    public static byte[] RenderInvoice(PlannedDocument doc)
    {
        var invoice = doc.Invoice!;
        var layout = invoice.Layout;
        var labels = layout.Labels;

        var meta = new List<(string Label, string Value)>
        {
            (labels.InvoiceNumber, invoice.InvoiceNumber),
            (labels.InvoiceDate, layout.FormatDate(invoice.InvoiceDate)),
            (labels.DueDate, layout.FormatDate(invoice.DueDate)),
        };
        if (!string.IsNullOrEmpty(invoice.PoReference))
        {
            meta.Add((labels.PoReference, invoice.PoReference));
        }

        return Build(doc, labels.InvoiceTitle, meta, invoice.Supplier, invoice.InvoiceDate, content =>
        {
            content.PaddingTop(14).Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(c => BuyerBlock(c, layout, labels.BillTo));
                column.Item().Element(c => InvoiceTable(c, invoice));
                column.Item().AlignRight().Element(c => Totals(c, invoice));
                column.Item().PaddingTop(10).Column(footer =>
                {
                    footer.Item().Text($"{labels.Iban}: {LayoutDefinition.FormatIban(invoice.Iban)}").SemiBold();
                    footer.Item().Text(labels.PaymentNote).FontColor(Grey).FontSize(8);
                });
            });
        });
    }

    public static byte[] RenderDeliveryNote(PlannedDocument doc)
    {
        var note = doc.DeliveryNote!;
        var layout = note.Layout;
        var labels = layout.Labels;

        var meta = new List<(string Label, string Value)>
        {
            (labels.DeliveryNoteNumber, note.DeliveryNoteNumber),
            (labels.DeliveryDate, layout.FormatDate(note.DeliveryDate)),
            (labels.PoReference, note.PoReference),
        };

        return Build(doc, labels.DeliveryNoteTitle, meta, note.Supplier, note.DeliveryDate, content =>
        {
            content.PaddingTop(14).Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(c => BuyerBlock(c, layout, labels.DeliveredTo));
                column.Item().Element(c => DeliveryNoteTable(c, note));
                column.Item().PaddingTop(40).Row(row =>
                {
                    row.RelativeItem().Column(sign =>
                    {
                        sign.Item().Text($"{labels.Received}:").FontColor(Grey);
                        sign.Item().PaddingTop(24).LineHorizontal(0.6f).LineColor(Grey);
                    });
                    row.ConstantItem(60);
                    row.RelativeItem().Column(sign =>
                    {
                        sign.Item().Text($"{labels.DeliveryDate}:").FontColor(Grey);
                        sign.Item().PaddingTop(24).LineHorizontal(0.6f).LineColor(Grey);
                    });
                });
            });
        });
    }

    private static byte[] Build(
        PlannedDocument doc, string title, List<(string Label, string Value)> meta,
        ThreeWayMatch.Domain.Erp.Supplier supplier, DateOnly date, Action<IContainer> content)
    {
        var layout = doc.Layout;
        var created = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(34);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor("#222222"));
                page.Header().Element(header => Header(header, layout, title, meta, supplier));
                page.Content().Element(content);
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span($"{supplier.Name} · {layout.Labels.VatNumber} {supplier.VatNumber} · ").FontSize(7.5f).FontColor(Grey);
                    t.Span("1 / ").FontSize(7.5f).FontColor(Grey);
                    t.TotalPages().FontSize(7.5f).FontColor(Grey);
                });
            });
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = $"{title} {doc.Id}",
            Author = supplier.Name,
            Creator = supplier.Name,
            Producer = supplier.Name,
            CreationDate = created,
            ModifiedDate = created,
        })
        .GeneratePdf();
    }

    private static void Header(
        IContainer container, LayoutDefinition layout, string title, List<(string Label, string Value)> meta,
        ThreeWayMatch.Domain.Erp.Supplier supplier)
    {
        void SupplierBlock(IContainer c, Align align) => c.Column(col =>
        {
            col.Item().Text(supplier.Name).FontSize(15).Bold().FontColor(layout.AccentColor).Aligned(align);
            foreach (var line in PartyDirectory.AddressOf(supplier))
            {
                col.Item().Text(line).Aligned(align);
            }

            col.Item().Text($"{layout.Labels.VatNumber} {supplier.VatNumber}").FontColor(Grey).Aligned(align);
            col.Item().Text($"{layout.Labels.RegistryCode} {supplier.RegistryCode}").FontColor(Grey).Aligned(align);
        });

        void MetaBlock(IContainer c, Align align) => c.Column(col =>
        {
            col.Item().Text(title).FontSize(20).Bold().FontColor(layout.AccentColor).Aligned(align);
            foreach (var (label, value) in meta)
            {
                col.Item().Text(t =>
                {
                    t.Aligned(align);
                    t.Span($"{label}: ").FontColor(Grey);
                    t.Span(value).SemiBold();
                });
            }
        });

        switch (layout.Header)
        {
            case HeaderStyle.SupplierLeftTitleRight:
                container.BorderBottom(1.5f).BorderColor(layout.AccentColor).PaddingBottom(8).Row(row =>
                {
                    row.RelativeItem().Element(c => SupplierBlock(c, Align.Left));
                    row.RelativeItem().Element(c => MetaBlock(c, Align.Right));
                });
                break;

            case HeaderStyle.TitleLeftSupplierRight:
                container.PaddingBottom(8).Row(row =>
                {
                    row.RelativeItem().Element(c => MetaBlock(c, Align.Left));
                    row.RelativeItem().Element(c => SupplierBlock(c, Align.Right));
                });
                break;

            default:
                container.BorderBottom(0.8f).BorderColor(layout.AccentColor).PaddingBottom(8).Column(col =>
                {
                    col.Item().Element(c => SupplierBlock(c, Align.Center));
                    col.Item().PaddingTop(8).Element(c => MetaBlock(c, Align.Center));
                });
                break;
        }
    }

    private static void BuyerBlock(IContainer container, LayoutDefinition layout, string label)
    {
        container.Column(col =>
        {
            col.Item().Text(label).FontColor(Grey).FontSize(8);
            foreach (var line in PartyDirectory.BuyerLines)
            {
                col.Item().Text(line);
            }
        });
    }

    private static void InvoiceTable(IContainer container, InvoiceModel invoice)
    {
        var layout = invoice.Layout;
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var column in layout.InvoiceColumns)
                {
                    DefineColumn(columns, column);
                }
            });

            table.Header(header =>
            {
                foreach (var column in layout.InvoiceColumns)
                {
                    header.Cell().Element(c => HeaderCell(c, layout, column)).Text(HeaderText(layout.Labels, column)).Bold().FontColor(Colors.White).Aligned(AlignmentOf(column));
                }
            });

            foreach (var line in invoice.Lines)
            {
                foreach (var column in layout.InvoiceColumns)
                {
                    table.Cell().Element(c => BodyCell(c, layout)).Text(InvoiceCellText(layout, line, column)).Aligned(AlignmentOf(column));
                }
            }
        });
    }

    private static void DeliveryNoteTable(IContainer container, DeliveryNoteModel note)
    {
        var layout = note.Layout;
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var column in layout.DeliveryNoteColumns)
                {
                    DefineColumn(columns, column);
                }
            });

            table.Header(header =>
            {
                foreach (var column in layout.DeliveryNoteColumns)
                {
                    header.Cell().Element(c => HeaderCell(c, layout, column)).Text(HeaderText(layout.Labels, column)).Bold().FontColor(Colors.White).Aligned(AlignmentOf(column));
                }
            });

            foreach (var line in note.Lines)
            {
                foreach (var column in layout.DeliveryNoteColumns)
                {
                    table.Cell().Element(c => BodyCell(c, layout)).Text(NoteCellText(layout, line, column)).Aligned(AlignmentOf(column));
                }
            }
        });
    }

    private static void DefineColumn(TableColumnsDefinitionDescriptor columns, Column column)
    {
        switch (column)
        {
            case Column.Position: columns.ConstantColumn(26); break;
            case Column.Code: columns.ConstantColumn(78); break;
            case Column.Description: columns.RelativeColumn(3); break;
            case Column.Quantity: columns.ConstantColumn(54); break;
            case Column.Unit: columns.ConstantColumn(42); break;
            case Column.UnitPrice: columns.ConstantColumn(60); break;
            case Column.VatRate: columns.ConstantColumn(42); break;
            default: columns.ConstantColumn(66); break;
        }
    }

    private static IContainer HeaderCell(IContainer container, LayoutDefinition layout, Column column) =>
        container.Background(layout.AccentColor).Padding(4);

    private static IContainer BodyCell(IContainer container, LayoutDefinition layout) =>
        (layout.GridLines
            ? container.Border(0.5f).BorderColor(LightGrey)
            : container.BorderBottom(0.5f).BorderColor(LightGrey))
        .Padding(4);

    private static Align AlignmentOf(Column column) => column is Column.Quantity or Column.UnitPrice or Column.VatRate or Column.Amount
        ? Align.Right
        : Align.Left;

    private static string HeaderText(Labels labels, Column column) => column switch
    {
        Column.Position => labels.Position,
        Column.Code => labels.Code,
        Column.Description => labels.Description,
        Column.Quantity => labels.Quantity,
        Column.Unit => labels.Unit,
        Column.UnitPrice => labels.UnitPrice,
        Column.VatRate => labels.VatRate,
        _ => labels.Amount,
    };

    private static string InvoiceCellText(LayoutDefinition layout, InvoiceLineModel line, Column column) => column switch
    {
        Column.Position => line.LineNumber.ToString(layout.CultureInfo),
        Column.Code => line.Code,
        Column.Description => line.Description,
        Column.Quantity => layout.FormatQuantity(line.Quantity),
        Column.Unit => line.Unit,
        Column.UnitPrice => layout.FormatMoney(line.UnitPrice, allowFourDecimals: true),
        Column.VatRate => layout.FormatVatRate(line.VatRate),
        _ => layout.FormatMoney(line.LineTotal),
    };

    private static string NoteCellText(LayoutDefinition layout, DeliveryNoteLineModel line, Column column) => column switch
    {
        Column.Position => line.LineNumber.ToString(layout.CultureInfo),
        Column.Code => line.Code,
        Column.Description => line.Description,
        Column.Quantity => layout.FormatQuantity(line.Quantity),
        _ => line.Unit,
    };

    private static void Totals(IContainer container, InvoiceModel invoice)
    {
        var layout = invoice.Layout;
        var labels = layout.Labels;
        container.Width(240).Column(col =>
        {
            void Row(string label, string value, bool bold)
            {
                col.Item().Row(row =>
                {
                    var l = row.RelativeItem().Text(label);
                    var v = row.ConstantItem(80).AlignRight().Text($"{value} {invoice.Currency}");
                    if (bold)
                    {
                        l.Bold();
                        v.Bold();
                    }
                });
            }

            Row(labels.Net, layout.FormatMoney(invoice.NetTotal), false);
            Row(labels.Vat, layout.FormatMoney(invoice.VatTotal), false);
            col.Item().PaddingVertical(2).LineHorizontal(0.8f).LineColor(layout.AccentColor);
            Row(labels.Gross, layout.FormatMoney(invoice.GrossTotal), true);
        });
    }
}
