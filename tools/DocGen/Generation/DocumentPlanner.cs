using System.Globalization;
using Bogus;
using DocGen.Seeding;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;

namespace DocGen.Generation;

/// <summary>
/// Decides which documents exist and what is wrong with them. Pure and seeded: no files, no database.
/// Order of work: clean documents, then injected errors (spread evenly over suppliers), then resends, then ids and split.
/// </summary>
internal sealed class DocumentPlanner(ErpData erp, int seed)
{
    public const decimal OverpaymentBlockThreshold = 500m;
    private static readonly DateOnly AsOf = new(2026, 10, 9);
    private const int PartialDeliveryOnPartlyReceivedOrders = 4;
    private const int PartialDeliveryOnUnreceivedOrders = 6;
    private const int CasesPerInvoiceCode = 4;
    private const int PriceBelowCases = 3;
    private const int ExactDuplicateCases = 4;
    private const int PossibleDuplicateCases = 4;
    private const int DoubleDiscrepancyCases = 5;
    private const double DeliveryNoteMismatchRate = 0.12;
    private const double HeldOutTestShare = 1.0;
    private const double TargetTestShare = 0.30;

    private readonly Randomizer _random = new(seed);
    private readonly Dictionary<Supplier, int> _injectedPerSupplier = [];

    private sealed class InvoicePlan
    {
        public required PlannedDocument Doc { get; init; }
        public required PurchaseOrder Po { get; init; }
        public GoodsReceipt? Receipt { get; init; }
        public bool Injected { get; set; }
        public InvoiceModel Invoice => Doc.Invoice!;
    }

    public IReadOnlyList<PlannedDocument> Plan()
    {
        var plans = BuildCleanInvoices();
        plans.AddRange(BuildInvoicesOnUnreceivedOrders());
        InjectInvoiceDiscrepancies(plans);

        var documents = plans.Select(p => p.Doc).ToList();
        documents.AddRange(BuildResends(plans));
        var notes = BuildDeliveryNotes();

        AssignIds(documents, notes);
        foreach (var document in documents.Concat(notes))
        {
            document.ExpectedDecision = ExpectedDecisionFor(document.Injected);
        }

        var all = documents.Concat(notes).ToList();
        AssignSplit(all);
        return all;
    }

    // ---------- clean invoices ----------

    private List<InvoicePlan> BuildCleanInvoices()
    {
        var plans = new List<InvoicePlan>();
        var partlyReceived = erp.PurchaseOrders
            .Where(po => po.Lines.Any(l => l.Status != PurchaseOrderLineStatus.Received) && erp.GoodsReceipts.Count(r => r.PurchaseOrder == po) == 1)
            .Where(po => po.Lines.Any(l => l.Status != PurchaseOrderLineStatus.Open))
            .ToList();
        var billedInFull = _random.Shuffle(partlyReceived).Take(PartialDeliveryOnPartlyReceivedOrders).ToHashSet();

        foreach (var receipt in erp.GoodsReceipts)
        {
            var po = receipt.PurchaseOrder!;
            var invoiceDate = Clamp(receipt.ReceivedDate.AddDays(_random.Int(0, 5)));

            if (billedInFull.Contains(po))
            {
                // The supplier bills the whole order after the first partial delivery: goods still expected, so on hold.
                var invoice = NewInvoice(po, invoiceDate, po.Lines.Select(l => (l, l.Quantity)).ToList());
                var doc = NewInvoiceDocument(invoice, po, receipt.Number);
                var unreceived = po.Lines.Sum(l => (l.Quantity - ReceivedQuantity(l)) * l.UnitPrice);
                doc.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.PartialDelivery, Severity.Warning, null,
                    "goods received", "invoiced in full", Math.Round(unreceived, 2)));
                plans.Add(new InvoicePlan { Doc = doc, Po = po, Receipt = receipt, Injected = true });
                continue;
            }

            var lines = receipt.Lines.Select(l => (l.PurchaseOrderLine!, l.Quantity)).ToList();
            var clean = NewInvoice(po, invoiceDate, lines);
            plans.Add(new InvoicePlan { Doc = NewInvoiceDocument(clean, po, receipt.Number), Po = po, Receipt = receipt });
        }

        return plans;
    }

    private List<InvoicePlan> BuildInvoicesOnUnreceivedOrders()
    {
        var plans = new List<InvoicePlan>();
        var unreceived = erp.PurchaseOrders.Where(po => erp.GoodsReceipts.All(r => r.PurchaseOrder != po)).OrderBy(po => po.OrderDate).ToList();

        foreach (var po in unreceived.Take(PartialDeliveryOnUnreceivedOrders))
        {
            var invoice = NewInvoice(po, Clamp(po.OrderDate.AddDays(_random.Int(7, 20))), po.Lines.Select(l => (l, l.Quantity)).ToList());
            var doc = NewInvoiceDocument(invoice, po, null);
            doc.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.PartialDelivery, Severity.Warning, null,
                "0 received", "invoiced in full", invoice.NetTotal));
            plans.Add(new InvoicePlan { Doc = doc, Po = po, Injected = true });
        }

        return plans;
    }

    private InvoiceModel NewInvoice(PurchaseOrder po, DateOnly date, List<(PurchaseOrderLine Line, decimal Quantity)> lines)
    {
        var supplier = po.Supplier!;
        var model = new InvoiceModel
        {
            Supplier = supplier,
            Layout = LayoutDefinition.For(PartyDirectory.KeyOf(supplier)),
            InvoiceNumber = string.Empty, // set once all invoices are ordered by date
            InvoiceDate = date,
            DueDate = date.AddDays(_random.Bool() ? 14 : 30),
            PoReference = po.Number,
            Iban = supplier.Iban,
            Lines = [],
            NetTotal = 0,
            VatTotal = 0,
            GrossTotal = 0,
        };

        var number = 0;
        foreach (var (line, quantity) in lines)
        {
            model.Lines.Add(new InvoiceLineModel
            {
                LineNumber = ++number,
                Code = line.SupplierItem!.SupplierItemCode,
                Description = line.Description,
                Quantity = quantity,
                Unit = line.Unit,
                UnitPrice = line.UnitPrice,
                VatRate = line.VatRate,
                LineTotal = Math.Round(quantity * line.UnitPrice, 2),
                PurchaseOrderLine = line,
            });
        }

        Recalculate(model);
        return model;
    }

    private static PlannedDocument NewInvoiceDocument(InvoiceModel invoice, PurchaseOrder po, string? receiptNumber) => new()
    {
        Id = string.Empty,
        Kind = DocumentKind.Invoice,
        Supplier = invoice.Supplier,
        Layout = invoice.Layout,
        Invoice = invoice,
        PurchaseOrderNumber = po.Number,
        GoodsReceiptNumber = receiptNumber,
        SortDate = invoice.InvoiceDate,
    };

    /// <summary>Totals the way a supplier's system would: VAT per line, rounded, then summed.</summary>
    private static void Recalculate(InvoiceModel model)
    {
        model.NetTotal = model.Lines.Sum(l => l.LineTotal);
        model.VatTotal = model.Lines.Sum(l => Math.Round(l.LineTotal * l.VatRate, 2));
        model.GrossTotal = model.NetTotal + model.VatTotal;
    }

    private decimal ReceivedQuantity(PurchaseOrderLine line) =>
        erp.GoodsReceipts.SelectMany(r => r.Lines).Where(l => l.PurchaseOrderLine == line).Sum(l => l.Quantity);

    private static DateOnly Clamp(DateOnly date) => date > AsOf ? AsOf : date;

    // ---------- injection ----------

    private void InjectInvoiceDiscrepancies(List<InvoicePlan> plans)
    {
        var singleReceiptInFull = (InvoicePlan p) => p.Receipt is not null && !p.Injected
            && p.Po.Lines.All(l => l.Status == PurchaseOrderLineStatus.Received)
            && erp.GoodsReceipts.Count(r => r.PurchaseOrder == p.Po) == 1;
        var anyClean = (InvoicePlan p) => p.Receipt is not null && !p.Injected;

        var primary = new List<(DiscrepancyCode Code, InvoicePlan Plan)>();
        void Run(DiscrepancyCode code, int count, Func<InvoicePlan, bool> eligible, Action<InvoicePlan, int> inject)
        {
            for (var i = 0; i < count; i++)
            {
                var plan = PickLeastUsedSupplier(plans.Where(p => !p.Injected && eligible(p)).ToList());
                if (plan is null)
                {
                    break;
                }

                inject(plan, i);
                plan.Injected = true;
                _injectedPerSupplier[plan.Invoice.Supplier] = _injectedPerSupplier.GetValueOrDefault(plan.Invoice.Supplier) + 1;
                primary.Add((code, plan));
            }
        }

        // Most restrictive first, so the scarce eligible invoices are not used up by the easy codes.
        Run(DiscrepancyCode.QtyOverInvoiced, CasesPerInvoiceCode, singleReceiptInFull, (p, _) => InjectQtyOver(p));
        Run(DiscrepancyCode.ItemNotOnPo, CasesPerInvoiceCode, anyClean, InjectItemNotOnPo);
        Run(DiscrepancyCode.UomMismatch, CasesPerInvoiceCode, anyClean, (p, _) => InjectUomMismatch(p));
        Run(DiscrepancyCode.PriceAbovePo, CasesPerInvoiceCode, anyClean, (p, _) => InjectPriceAbove(p));
        Run(DiscrepancyCode.NoPoReference, CasesPerInvoiceCode, anyClean, InjectNoPoReference);
        Run(DiscrepancyCode.TotalsMismatch, CasesPerInvoiceCode, anyClean, InjectTotalsMismatch);
        Run(DiscrepancyCode.BankAccountChanged, CasesPerInvoiceCode, anyClean, (p, _) => InjectBankChange(p));
        Run(DiscrepancyCode.PriceBelowPo, PriceBelowCases, anyClean, (p, _) => InjectPriceBelow(p));

        // A handful of invoices get a second, independent error on top of the first.
        var candidates = primary.Where(x => x.Code is DiscrepancyCode.QtyOverInvoiced or DiscrepancyCode.PriceAbovePo or DiscrepancyCode.UomMismatch)
            .Select(x => x.Plan).Distinct().ToList();
        var second = 0;
        foreach (var plan in _random.Shuffle(candidates).Take(DoubleDiscrepancyCases))
        {
            if (second++ % 2 == 0)
            {
                InjectTotalsMismatch(plan, 0);
            }
            else
            {
                InjectBankChange(plan);
            }
        }
    }

    private InvoicePlan? PickLeastUsedSupplier(List<InvoicePlan> eligible)
    {
        if (eligible.Count == 0)
        {
            return null;
        }

        var least = eligible.Min(p => _injectedPerSupplier.GetValueOrDefault(p.Invoice.Supplier));
        return _random.ArrayElement(eligible.Where(p => _injectedPerSupplier.GetValueOrDefault(p.Invoice.Supplier) == least).ToArray());
    }

    private static Severity OverpaymentSeverity(decimal amount) => amount > OverpaymentBlockThreshold ? Severity.Block : Severity.Warning;

    private static string Text(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private void InjectQtyOver(InvoicePlan plan)
    {
        var line = plan.Invoice.Lines
            .Where(l => l.PurchaseOrderLine is not null && l.Quantity == l.PurchaseOrderLine.Quantity)
            .MaxBy(l => l.LineTotal)!;
        var decimals = line.Unit == "m³" ? 2 : 0;
        var original = line.Quantity;
        var inflated = Math.Round(original * _random.Decimal(1.15m, 1.40m), decimals);
        line.Quantity = inflated > original ? inflated : original + (decimals == 0 ? 1 : 0.1m);
        line.LineTotal = Math.Round(line.Quantity * line.UnitPrice, 2);
        Recalculate(plan.Invoice);

        var amount = Math.Round((line.Quantity - original) * line.UnitPrice, 2);
        plan.Doc.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.QtyOverInvoiced, OverpaymentSeverity(amount),
            line.LineNumber, Text(original), Text(line.Quantity), amount));
    }

    private void InjectPriceAbove(InvoicePlan plan)
    {
        var line = _random.ArrayElement(plan.Invoice.Lines.Where(l => l.PurchaseOrderLine is not null).ToArray());
        var original = line.UnitPrice;
        line.UnitPrice = Math.Round(original * _random.Decimal(1.03m, 1.12m), original < 1m ? 4 : 2);
        line.LineTotal = Math.Round(line.Quantity * line.UnitPrice, 2);
        Recalculate(plan.Invoice);

        var amount = Math.Round((line.UnitPrice - original) * line.Quantity, 2);
        plan.Doc.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.PriceAbovePo, OverpaymentSeverity(amount),
            line.LineNumber, Text(original), Text(line.UnitPrice), amount));
    }

    private void InjectPriceBelow(InvoicePlan plan)
    {
        var line = _random.ArrayElement(plan.Invoice.Lines.Where(l => l.PurchaseOrderLine is not null).ToArray());
        var original = line.UnitPrice;
        line.UnitPrice = Math.Round(original * _random.Decimal(0.92m, 0.97m), original < 1m ? 4 : 2);
        line.LineTotal = Math.Round(line.Quantity * line.UnitPrice, 2);
        Recalculate(plan.Invoice);

        plan.Doc.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.PriceBelowPo, Severity.Info,
            line.LineNumber, Text(original), Text(line.UnitPrice), 0m));
    }

    private void InjectItemNotOnPo(InvoicePlan plan, int caseIndex)
    {
        var estonian = plan.Invoice.Layout.Culture == "et-EE";
        var poHasChargeLine = plan.Po.Lines.Any(l => l.SupplierItem!.Item!.Code.StartsWith("FRT-", StringComparison.Ordinal));

        InvoiceLineModel extra;
        var lineNumber = plan.Invoice.Lines.Count + 1;
        if (caseIndex % 2 == 0 && !poHasChargeLine)
        {
            // A surcharge nobody ordered.
            extra = new InvoiceLineModel
            {
                LineNumber = lineNumber, Code = "SURCH", Description = estonian ? "Kütuselisa" : "Fuel surcharge",
                Quantity = 1, Unit = estonian ? "tk" : "pcs", UnitPrice = Math.Round(_random.Decimal(25m, 95m), 2),
                VatRate = 0.24m, LineTotal = 0,
            };
        }
        else
        {
            // A product that is not on the order.
            extra = new InvoiceLineModel
            {
                LineNumber = lineNumber, Code = $"ZX-{_random.Int(1000, 9999)}", Description = estonian ? "Näidiskomplekt" : "Sample set",
                Quantity = _random.Int(1, 3), Unit = estonian ? "tk" : "pcs", UnitPrice = Math.Round(_random.Decimal(15m, 120m), 2),
                VatRate = 0.24m, LineTotal = 0,
            };
        }

        extra.LineTotal = Math.Round(extra.Quantity * extra.UnitPrice, 2);
        plan.Invoice.Lines.Add(extra);
        Recalculate(plan.Invoice);

        plan.Doc.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.ItemNotOnPo, Severity.Warning,
            extra.LineNumber, "not on PO", extra.Description, extra.LineTotal));
    }

    private void InjectNoPoReference(InvoicePlan plan, int caseIndex)
    {
        var real = plan.Invoice.PoReference;
        plan.Invoice.PoReference = caseIndex % 2 == 0 ? null : $"PO-2026-{_random.Int(9000, 9899)}";
        plan.Doc.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.NoPoReference, Severity.Warning,
            null, real, plan.Invoice.PoReference ?? "(blank)", plan.Invoice.NetTotal));
    }

    private void InjectTotalsMismatch(InvoicePlan plan, int caseIndex)
    {
        var delta = Math.Round(_random.Decimal(1m, 25m), 2) * (_random.Bool() ? 1 : -1);
        var expected = plan.Invoice.GrossTotal;
        if (caseIndex % 2 == 0)
        {
            // The grand total does not equal net + VAT.
            plan.Invoice.GrossTotal += delta;
        }
        else
        {
            // The VAT total is wrong, and the grand total follows it.
            plan.Invoice.VatTotal += delta;
            plan.Invoice.GrossTotal += delta;
        }

        plan.Doc.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.TotalsMismatch, Severity.Warning,
            null, Text(expected), Text(plan.Invoice.GrossTotal), Math.Abs(delta)));
    }

    private void InjectBankChange(InvoicePlan plan)
    {
        var master = plan.Invoice.Supplier.Iban;
        string changed;
        do
        {
            changed = IbanGenerator.Create(_random);
        }
        while (changed == master);

        plan.Invoice.Iban = changed;
        plan.Doc.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.BankAccountChanged, Severity.Block,
            null, master, changed, plan.Invoice.GrossTotal));
    }

    private void InjectUomMismatch(InvoicePlan plan)
    {
        var line = _random.ArrayElement(plan.Invoice.Lines.Where(l => l.PurchaseOrderLine is not null).ToArray());
        var original = line.Unit;
        // Solid cubic metres versus stacked ones is the classic timber mix-up; the other categories get a unit that cannot convert.
        line.Unit = PartyDirectory.KeyOf(plan.Invoice.Supplier) switch
        {
            SeedCatalogue.TimberKey => "rm",
            SeedCatalogue.PanelsKey => "m²",
            SeedCatalogue.TransportKey => "km",
            _ => "kg",
        };

        plan.Doc.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.UomMismatch, Severity.Warning,
            line.LineNumber, original, line.Unit, line.LineTotal));
    }

    // ---------- resends ----------

    private List<PlannedDocument> BuildResends(List<InvoicePlan> plans)
    {
        var resends = new List<PlannedDocument>();
        var cleanSources = plans
            .Where(p => !p.Injected && p.Receipt is not null && p.Invoice.InvoiceDate <= AsOf.AddDays(-12))
            .ToList();
        var picked = _random.Shuffle(cleanSources).ToList();

        foreach (var source in picked.Take(ExactDuplicateCases))
        {
            var copy = new PlannedDocument
            {
                Id = string.Empty, Kind = DocumentKind.Invoice, Supplier = source.Doc.Supplier, Layout = source.Doc.Layout,
                Invoice = source.Invoice, PurchaseOrderNumber = source.Doc.PurchaseOrderNumber,
                GoodsReceiptNumber = source.Doc.GoodsReceiptNumber, ExactCopyOf = source.Doc,
                SortDate = source.Invoice.InvoiceDate, SortOrder = 1,
            };
            copy.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.DuplicateInvoice, Severity.Block,
                null, "first copy", "same invoice again", source.Invoice.GrossTotal));
            resends.Add(copy);
        }

        foreach (var source in picked.Skip(ExactDuplicateCases).Take(PossibleDuplicateCases))
        {
            var original = source.Invoice;
            var date = Clamp(original.InvoiceDate.AddDays(_random.Int(1, 10)));
            var invoice = new InvoiceModel
            {
                Supplier = original.Supplier, Layout = original.Layout, InvoiceNumber = string.Empty,
                InvoiceDate = date, DueDate = date.AddDays(14), PoReference = original.PoReference, Iban = original.Iban,
                NetTotal = original.NetTotal, VatTotal = original.VatTotal, GrossTotal = original.GrossTotal,
                Lines = original.Lines.Select(l => new InvoiceLineModel
                {
                    LineNumber = l.LineNumber, Code = l.Code, Description = l.Description, Quantity = l.Quantity,
                    Unit = l.Unit, UnitPrice = l.UnitPrice, VatRate = l.VatRate, LineTotal = l.LineTotal,
                    PurchaseOrderLine = l.PurchaseOrderLine,
                }).ToList(),
            };

            var copy = new PlannedDocument
            {
                Id = string.Empty, Kind = DocumentKind.Invoice, Supplier = source.Doc.Supplier, Layout = source.Doc.Layout,
                Invoice = invoice, PurchaseOrderNumber = source.Doc.PurchaseOrderNumber,
                GoodsReceiptNumber = source.Doc.GoodsReceiptNumber, SortDate = date, SortOrder = 1,
            };
            copy.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.PossibleDuplicate, Severity.Warning,
                null, original.InvoiceNumber, "new number, same supplier and amount", original.GrossTotal));
            resends.Add(copy);
        }

        return resends;
    }

    // ---------- delivery notes ----------

    private List<PlannedDocument> BuildDeliveryNotes()
    {
        var notes = new List<PlannedDocument>();
        foreach (var receipt in erp.GoodsReceipts)
        {
            var po = receipt.PurchaseOrder!;
            var supplier = po.Supplier!;
            var layout = LayoutDefinition.For(PartyDirectory.KeyOf(supplier));
            var model = new DeliveryNoteModel
            {
                Supplier = supplier, Layout = layout, DeliveryNoteNumber = receipt.DeliveryNoteNumber!,
                DeliveryDate = receipt.ReceivedDate, PoReference = po.Number,
                Lines = receipt.Lines.Select((l, i) => new DeliveryNoteLineModel
                {
                    LineNumber = i + 1, Code = l.PurchaseOrderLine!.SupplierItem!.SupplierItemCode,
                    Description = l.PurchaseOrderLine.Description, Quantity = l.Quantity, Unit = l.PurchaseOrderLine.Unit,
                    GoodsReceiptLine = l,
                }).ToList(),
            };

            notes.Add(new PlannedDocument
            {
                Id = string.Empty, Kind = DocumentKind.DeliveryNote, Supplier = supplier, Layout = layout,
                DeliveryNote = model, PurchaseOrderNumber = po.Number, GoodsReceiptNumber = receipt.Number,
                SortDate = receipt.ReceivedDate,
            });
        }

        var mismatchCount = (int)Math.Round(notes.Count * DeliveryNoteMismatchRate);
        var perSupplier = new Dictionary<Supplier, int>();
        for (var i = 0; i < mismatchCount; i++)
        {
            var eligible = notes.Where(n => n.Injected.Count == 0).ToList();
            var least = eligible.Min(n => perSupplier.GetValueOrDefault(n.Supplier));
            var note = _random.ArrayElement(eligible.Where(n => perSupplier.GetValueOrDefault(n.Supplier) == least).ToArray());
            perSupplier[note.Supplier] = perSupplier.GetValueOrDefault(note.Supplier) + 1;

            var line = _random.ArrayElement(note.DeliveryNote!.Lines.ToArray());
            var booked = line.Quantity;
            var decimals = line.Unit == "m³" ? 2 : 0;
            var delta = decimals == 0 ? _random.Int(1, 3) : Math.Round(_random.Decimal(0.1m, 1m), 2);
            line.Quantity = booked > delta && _random.Bool() ? booked - delta : booked + delta;

            var price = line.GoodsReceiptLine!.PurchaseOrderLine!.UnitPrice;
            note.Injected.Add(new InjectedDiscrepancy(DiscrepancyCode.DeliveryNoteMismatch, Severity.Warning,
                line.LineNumber, Text(booked), Text(line.Quantity), Math.Round(Math.Abs(line.Quantity - booked) * price, 2)));
        }

        return notes;
    }

    // ---------- ids, decisions, split ----------

    private void AssignIds(List<PlannedDocument> invoices, List<PlannedDocument> notes)
    {
        var ordered = invoices.OrderBy(d => d.SortDate).ThenBy(d => d.SortOrder).ThenBy(d => d.Supplier.Name).ToList();
        var counters = new Dictionary<Supplier, int>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var doc = ordered[i];
            doc.Id = $"inv-{i + 1:D4}";

            // Exact resends share the original's printed invoice number; everything else gets the next number.
            if (doc.ExactCopyOf is null)
            {
                var next = counters.TryGetValue(doc.Supplier, out var current) ? current + _random.Int(1, 4) : _random.Int(100, 900);
                counters[doc.Supplier] = next;
                doc.Invoice!.InvoiceNumber = PartyDirectory.InvoiceNumber(doc.Supplier, next);
            }
        }

        var orderedNotes = notes.OrderBy(d => d.SortDate).ThenBy(d => d.Supplier.Name).ThenBy(d => d.DeliveryNote!.DeliveryNoteNumber).ToList();
        for (var i = 0; i < orderedNotes.Count; i++)
        {
            orderedNotes[i].Id = $"dn-{i + 1:D4}";
        }
    }

    private static Decision ExpectedDecisionFor(List<InjectedDiscrepancy> injected)
    {
        if (injected.Any(d => d.Code == DiscrepancyCode.DuplicateInvoice))
        {
            return Decision.Rejected;
        }

        if (injected.Any(d => d.Code == DiscrepancyCode.PartialDelivery))
        {
            return Decision.OnHold;
        }

        return injected.Any(d => d.Severity >= Severity.Warning) ? Decision.NeedsReview : Decision.AutoApproved;
    }

    /// <summary>
    /// About 30 % of documents go to test, spread evenly inside each (kind, main error) group so every error type
    /// appears on both sides. The held-out layout goes to test entirely, so the other groups give up a bit less.
    /// </summary>
    private static void AssignSplit(List<PlannedDocument> documents)
    {
        var heldOut = documents.Where(d => d.Layout.HeldOut).ToList();
        foreach (var doc in heldOut)
        {
            doc.Split = "test";
        }

        var rest = documents.Where(d => !d.Layout.HeldOut).ToList();
        var restShare = Math.Max(0, (TargetTestShare * documents.Count - heldOut.Count * HeldOutTestShare) / rest.Count);

        foreach (var group in documents.GroupBy(d => (d.Kind, Main: d.Injected.FirstOrDefault()?.Code.ToString() ?? "none")))
        {
            var members = group.Where(d => !d.Layout.HeldOut).OrderBy(d => d.Layout.Id).ThenBy(d => d.Id).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            // At least one test document per group (unless the held-out layout already supplies one), and at least one dev document.
            var heldOutInGroup = group.Count(d => d.Layout.HeldOut);
            var wanted = (int)Math.Round(members.Count * restShare);
            var testCount = Math.Max(wanted, heldOutInGroup == 0 ? 1 : 0);
            testCount = Math.Min(testCount, members.Count - 1);

            // Spread the test documents evenly across the group, which is sorted by layout.
            for (var k = 0; k < testCount; k++)
            {
                members[(int)((k + 0.5) * members.Count / testCount)].Split = "test";
            }
        }
    }
}
