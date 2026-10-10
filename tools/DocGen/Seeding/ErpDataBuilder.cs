using Bogus;
using ThreeWayMatch.Domain.Erp;

namespace DocGen.Seeding;

/// <summary>
/// Builds the mock ERP data. Pure: the same seed always gives the same data, and nothing here touches a database.
/// Every random decision comes from one seeded Bogus <see cref="Randomizer"/>, drawn in a fixed order.
/// </summary>
internal sealed class ErpDataBuilder(int seed)
{
    public const int DefaultSeed = 20261010;

    // "Today" in the story. Fixed so the data does not depend on when the seeder runs.
    private static readonly DateOnly AsOf = new(2026, 10, 9);
    private static readonly DateOnly FirstOrderDate = new(2026, 4, 1);
    private static readonly DateOnly LastOrderDate = new(2026, 9, 25);

    private const int PurchaseOrderCount = 120;
    private const decimal VatRate = 0.24m;

    private readonly Randomizer _random = new(seed);

    public ErpData Build()
    {
        var suppliers = BuildSuppliers();
        var (items, supplierItems, specBySupplierItem) = BuildItems(suppliers);
        var priceChanges = PickPriceChanges(supplierItems, specBySupplierItem);
        var purchaseOrders = BuildPurchaseOrders(suppliers, supplierItems, specBySupplierItem, priceChanges);
        var receipts = BuildGoodsReceipts(purchaseOrders, specBySupplierItem);

        return new ErpData(suppliers.Values.ToList(), items, supplierItems, purchaseOrders, receipts);
    }

    private Dictionary<string, Supplier> BuildSuppliers()
    {
        var suppliers = new Dictionary<string, Supplier>();
        foreach (var spec in SeedCatalogue.Suppliers)
        {
            suppliers[spec.Key] = new Supplier
            {
                Name = spec.Name,
                Category = spec.Category,
                // Estonian VAT numbers are EE + 9 digits; registry codes are 8 digits.
                VatNumber = "EE10" + _random.Int(0, 9_999_999).ToString("D7", System.Globalization.CultureInfo.InvariantCulture),
                RegistryCode = _random.Int(10_000_000, 14_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Iban = IbanGenerator.Create(_random),
                DocumentCulture = spec.DocumentCulture,
                // Timber volumes vary more than the other categories, so the quantity tolerance is wider there.
                QuantityTolerancePercent = spec.Key == SeedCatalogue.TimberKey ? 3m : null,
            };
        }

        return suppliers;
    }

    private (List<Item> Items, List<SupplierItem> SupplierItems, Dictionary<SupplierItem, ItemSpec> Specs) BuildItems(
        Dictionary<string, Supplier> suppliers)
    {
        var items = new List<Item>();
        var supplierItems = new List<SupplierItem>();
        var specs = new Dictionary<SupplierItem, ItemSpec>();
        var itemNumbers = new Dictionary<string, int>();
        var supplierNumbers = new Dictionary<string, int>();

        foreach (var spec in SeedCatalogue.Items)
        {
            var supplierSpec = SeedCatalogue.Suppliers.Single(s => s.Key == spec.SupplierKey);
            var prefix = spec.IsCharge ? "FRT" : supplierSpec.ItemCodePrefix;

            itemNumbers[prefix] = itemNumbers.GetValueOrDefault(prefix) + 1;
            var item = new Item
            {
                Code = $"{prefix}-{itemNumbers[prefix]:D4}",
                Name = spec.Name,
                BaseUnit = spec.BaseUnit,
            };

            // Supplier codes are not consecutive, like real catalogues.
            supplierNumbers[spec.SupplierKey] = supplierNumbers.GetValueOrDefault(spec.SupplierKey, 100 * _random.Int(1, 9)) + _random.Int(1, 40);
            var supplierItem = new SupplierItem
            {
                Supplier = suppliers[spec.SupplierKey],
                Item = item,
                SupplierItemCode = string.Format(System.Globalization.CultureInfo.InvariantCulture, supplierSpec.CodeFormat, supplierNumbers[spec.SupplierKey]),
                SupplierDescription = spec.SupplierDescription,
                SupplierUnit = spec.SupplierUnit,
                ConversionFactorToBase = spec.Factor,
            };

            items.Add(item);
            supplierItems.Add(supplierItem);
            specs[supplierItem] = spec;
        }

        return (items, supplierItems, specs);
    }

    /// <summary>A few price changes during the period: six increases from 1 July, two decreases from 15 August.</summary>
    private Dictionary<SupplierItem, (DateOnly From, decimal Factor)> PickPriceChanges(
        List<SupplierItem> supplierItems, Dictionary<SupplierItem, ItemSpec> specs)
    {
        var candidates = _random.Shuffle(supplierItems.Where(si => !specs[si].IsCharge && specs[si].SupplierKey != SeedCatalogue.TransportKey)).ToList();
        var changes = new Dictionary<SupplierItem, (DateOnly, decimal)>();

        foreach (var supplierItem in candidates.Take(6))
        {
            changes[supplierItem] = (new DateOnly(2026, 7, 1), 1.06m);
        }

        foreach (var supplierItem in candidates.Skip(6).Take(2))
        {
            changes[supplierItem] = (new DateOnly(2026, 8, 15), 0.97m);
        }

        return changes;
    }

    private List<PurchaseOrder> BuildPurchaseOrders(
        Dictionary<string, Supplier> suppliers,
        List<SupplierItem> supplierItems,
        Dictionary<SupplierItem, ItemSpec> specs,
        Dictionary<SupplierItem, (DateOnly From, decimal Factor)> priceChanges)
    {
        var span = LastOrderDate.DayNumber - FirstOrderDate.DayNumber;
        var orderDates = Enumerable.Range(0, PurchaseOrderCount)
            .Select(_ => FirstOrderDate.AddDays(_random.Int(0, span)))
            .Order()
            .ToList();

        var supplierSpecs = SeedCatalogue.Suppliers.ToArray();
        var weights = supplierSpecs.Select(s => (float)s.PoWeight).ToArray();

        var purchaseOrders = new List<PurchaseOrder>();
        for (var i = 0; i < PurchaseOrderCount; i++)
        {
            var supplierSpec = _random.WeightedRandom(supplierSpecs, weights);
            var supplier = suppliers[supplierSpec.Key];

            var po = new PurchaseOrder
            {
                Number = $"PO-2026-{i + 1:D4}",
                Supplier = supplier,
                OrderDate = orderDates[i],
                Currency = "EUR",
            };

            var goods = supplierItems.Where(si => si.Supplier == supplier && !specs[si].IsCharge).ToList();
            var lineCount = supplierSpec.Key == SeedCatalogue.TransportKey ? _random.Int(1, 2) : _random.Int(1, 4);
            var chosen = _random.Shuffle(goods).Take(lineCount).ToList();

            // About a third of the orders from material suppliers include a delivery charge line.
            if (supplierSpec.SellsFreight && _random.Double() < 0.3)
            {
                chosen.Add(supplierItems.Single(si => si.Supplier == supplier && specs[si].IsCharge));
            }

            var lineNumber = 0;
            foreach (var supplierItem in chosen)
            {
                var spec = specs[supplierItem];
                var quantity = Math.Round(_random.Decimal(spec.MinQty, spec.MaxQty), spec.QtyDecimals);
                quantity = Math.Max(quantity, spec.QtyDecimals == 0 ? 1m : 0.5m);

                po.Lines.Add(new PurchaseOrderLine
                {
                    LineNumber = ++lineNumber,
                    SupplierItem = supplierItem,
                    Description = supplierItem.SupplierDescription,
                    Quantity = quantity,
                    Unit = supplierItem.SupplierUnit,
                    UnitPrice = PriceOn(spec, supplierItem, po.OrderDate, priceChanges),
                    VatRate = VatRate,
                    Status = PurchaseOrderLineStatus.Open,
                });
            }

            purchaseOrders.Add(po);
        }

        return purchaseOrders;
    }

    private static decimal PriceOn(
        ItemSpec spec, SupplierItem supplierItem, DateOnly date,
        Dictionary<SupplierItem, (DateOnly From, decimal Factor)> priceChanges)
    {
        var price = spec.Price;
        if (priceChanges.TryGetValue(supplierItem, out var change) && date >= change.From)
        {
            price *= change.Factor;
        }

        return Math.Round(price, price < 1m ? 4 : 2);
    }

    /// <summary>
    /// Delivery pattern: the newest 10 % of orders have nothing received, about 20 % are partly delivered
    /// (one delivery, short on some lines), the rest are fully delivered, a fifth of those in two deliveries.
    /// </summary>
    private List<GoodsReceipt> BuildGoodsReceipts(List<PurchaseOrder> purchaseOrders, Dictionary<SupplierItem, ItemSpec> specs)
    {
        var notReceivedFrom = (int)(purchaseOrders.Count * 0.9);
        var partialIndexes = _random.Shuffle(Enumerable.Range(purchaseOrders.Count / 4, notReceivedFrom - purchaseOrders.Count / 4))
            .Take((int)(purchaseOrders.Count * 0.2))
            .ToHashSet();

        var receipts = new List<GoodsReceipt>();
        for (var i = 0; i < notReceivedFrom; i++)
        {
            var po = purchaseOrders[i];
            var firstDelivery = Earlier(po.OrderDate.AddDays(_random.Int(2, 14)));

            if (partialIndexes.Contains(i))
            {
                var partial = Receipt(po, firstDelivery, po.Lines.Where(l => !specs[l.SupplierItem!].IsCharge), share: () => _random.Decimal(0.4m, 0.8m), specs);
                if (partial.Lines.Count == 0)
                {
                    // Every line rounded to nothing (tiny quantities): deliver the first line in full instead.
                    partial.Lines.Add(new GoodsReceiptLine { PurchaseOrderLine = po.Lines[0], Quantity = po.Lines[0].Quantity });
                }

                receipts.Add(partial);
            }
            else if (_random.Double() < 0.2)
            {
                var share = _random.Decimal(0.4m, 0.7m);
                var first = Receipt(po, firstDelivery, po.Lines, () => share, specs);
                var second = Remainder(po, first, Earlier(firstDelivery.AddDays(_random.Int(3, 10))));
                receipts.Add(first);
                if (second.Lines.Count > 0)
                {
                    receipts.Add(second);
                }
            }
            else
            {
                receipts.Add(Receipt(po, firstDelivery, po.Lines, share: null, specs));
            }
        }

        // Number receipts in date order, like a warehouse system would, then set each line's status.
        var ordered = receipts.OrderBy(r => r.ReceivedDate).ThenBy(r => r.PurchaseOrder!.Number).ToList();
        var deliveryNoteCounters = new Dictionary<Supplier, int>();
        for (var n = 0; n < ordered.Count; n++)
        {
            var receipt = ordered[n];
            receipt.Number = $"GR-2026-{n + 1:D5}";

            var supplier = receipt.PurchaseOrder!.Supplier!;
            if (!deliveryNoteCounters.TryGetValue(supplier, out var counter))
            {
                counter = _random.Int(1000, 4000);
            }

            deliveryNoteCounters[supplier] = ++counter;
            receipt.DeliveryNoteNumber = $"{counter}";
        }

        foreach (var po in purchaseOrders)
        {
            foreach (var line in po.Lines)
            {
                var received = ordered.SelectMany(r => r.Lines).Where(l => l.PurchaseOrderLine == line).Sum(l => l.Quantity);
                line.Status = received == 0 ? PurchaseOrderLineStatus.Open
                    : received < line.Quantity ? PurchaseOrderLineStatus.PartiallyReceived
                    : PurchaseOrderLineStatus.Received;
            }
        }

        return ordered;
    }

    private static DateOnly Earlier(DateOnly date) => date > AsOf ? AsOf : date;

    /// <summary>One delivery. With a share function, each line gets that share of its quantity; without, the full quantity.</summary>
    private static GoodsReceipt Receipt(
        PurchaseOrder po, DateOnly date, IEnumerable<PurchaseOrderLine> lines, Func<decimal>? share,
        Dictionary<SupplierItem, ItemSpec> specs)
    {
        var receipt = new GoodsReceipt { Number = string.Empty, PurchaseOrder = po, ReceivedDate = date };
        foreach (var line in lines)
        {
            var spec = specs[line.SupplierItem!];
            var quantity = share is null ? line.Quantity : Math.Round(line.Quantity * share(), spec.QtyDecimals);

            // A share that rounds to nothing, or to everything, is not a partial delivery of this line.
            if (share is not null && (quantity <= 0 || quantity >= line.Quantity))
            {
                quantity = quantity <= 0 ? 0 : line.Quantity;
            }

            if (quantity > 0)
            {
                receipt.Lines.Add(new GoodsReceiptLine { PurchaseOrderLine = line, Quantity = quantity });
            }
        }

        return receipt;
    }

    /// <summary>The second delivery of a split order: whatever the first delivery did not bring.</summary>
    private static GoodsReceipt Remainder(PurchaseOrder po, GoodsReceipt first, DateOnly date)
    {
        var receipt = new GoodsReceipt { Number = string.Empty, PurchaseOrder = po, ReceivedDate = date };
        foreach (var line in po.Lines)
        {
            var delivered = first.Lines.Where(l => l.PurchaseOrderLine == line).Sum(l => l.Quantity);
            if (line.Quantity - delivered > 0)
            {
                receipt.Lines.Add(new GoodsReceiptLine { PurchaseOrderLine = line, Quantity = line.Quantity - delivered });
            }
        }

        return receipt;
    }
}
