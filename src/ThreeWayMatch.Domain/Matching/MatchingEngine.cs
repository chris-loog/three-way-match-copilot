using FuzzySharp;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Matching;

/// <summary>
/// Matches one document against the purchase orders and goods receipts in a snapshot: which supplier, which order,
/// and for every line which order line it belongs to. Pure: no database, no Azure, no clock.
/// It only matches. Whether the match is acceptable (prices, quantities, duplicates) is decided by the rules in step 5.2.
/// </summary>
public static class MatchingEngine
{
    private sealed record ResolvedLine(MatchInputLine Line, ItemResolution Resolution, bool IsUncodedCharge);

    public static MatchOutcome Match(MatchInput input, MatchingSnapshot snapshot)
    {
        var concerns = new List<MatchConcern>();

        var supplierMatch = SupplierMatcher.Match(input, snapshot, concerns);
        var supplier = supplierMatch.Supplier;

        var items = supplier is null ? [] : snapshot.SupplierItems.Where(i => Ownership.Owns(supplier, i)).ToList();
        var resolver = new ItemResolver(items, snapshot.Aliases);
        var resolved = input.Lines.Select(line => Resolve(line, resolver)).ToList();

        var purchaseOrder = MatchPurchaseOrder(input, supplier, snapshot, resolved, concerns);
        var lines = AllocateLines(resolved, purchaseOrder.PurchaseOrder, snapshot, concerns);

        return new MatchOutcome(supplierMatch, purchaseOrder, lines, concerns);
    }

    /// <summary>
    /// The cascade for one line. Exact levels first. A line with no code that reads like a charge ("Fuel surcharge") is
    /// not guessed at: it is matched to the order's charge line later, or reported as not on the order. Only the rest is fuzzy.
    /// </summary>
    private static ResolvedLine Resolve(MatchInputLine line, ItemResolver resolver)
    {
        var exact = resolver.ResolveExact(line);
        if (exact.Item is not null)
        {
            return new ResolvedLine(line, exact, false);
        }

        return ChargeLines.LooksLikeCharge(line.Description)
            ? new ResolvedLine(line, ItemResolution.None, true)
            : new ResolvedLine(line, resolver.ResolveFuzzy(line), false);
    }

    // ---------- purchase order ----------

    private static PurchaseOrderMatch MatchPurchaseOrder(
        MatchInput input, Supplier? supplier, MatchingSnapshot snapshot, List<ResolvedLine> resolved, List<MatchConcern> concerns)
    {
        var printed = input.PurchaseOrderReference;
        var key = Normaliser.ReferenceKey(printed);

        PurchaseOrderReferenceStatus status;
        if (key.Length == 0)
        {
            status = PurchaseOrderReferenceStatus.Missing;
        }
        else if (supplier is null)
        {
            status = PurchaseOrderReferenceStatus.SupplierUnknown;
        }
        else
        {
            var found = snapshot.PurchaseOrders.FirstOrDefault(p => Normaliser.ReferenceKey(p.Number) == key);
            if (found is null)
            {
                status = PurchaseOrderReferenceStatus.Unknown;
            }
            else if (!Ownership.Owns(supplier, found))
            {
                status = PurchaseOrderReferenceStatus.BelongsToOtherSupplier;
            }
            else
            {
                return new PurchaseOrderMatch(found, PurchaseOrderMatchMethod.Reference, PurchaseOrderReferenceStatus.Matched, printed, []);
            }
        }

        if (supplier is null)
        {
            return new PurchaseOrderMatch(null, PurchaseOrderMatchMethod.None, status, printed, []);
        }

        // No usable reference. Look for an open order of this supplier that the lines fit, like an accountant would.
        var candidates = FindCandidates(supplier, snapshot, resolved);
        if (candidates.Count == 1)
        {
            concerns.Add(new MatchConcern(ConcernKind.PurchaseOrderSuggested, null,
                $"No usable PO reference; {candidates[0].Number} is the only open order of the supplier that fits the lines."));
            return new PurchaseOrderMatch(candidates[0], PurchaseOrderMatchMethod.SuggestedFromLines, status, printed, candidates);
        }

        return new PurchaseOrderMatch(null, PurchaseOrderMatchMethod.None, status, printed, candidates);
    }

    /// <summary>
    /// Orders of the supplier that can take every goods line of the document: each item is on the order with enough
    /// quantity still open. A document with a line that matches no item has no candidates, which is the safe answer.
    /// </summary>
    private static List<PurchaseOrder> FindCandidates(Supplier supplier, MatchingSnapshot snapshot, List<ResolvedLine> resolved)
    {
        var unmatchedGoods = resolved.Any(r => r.Resolution.Item is null && !r.IsUncodedCharge);
        var goods = resolved.Where(r => r.Resolution.Item is not null).GroupBy(r => r.Resolution.Item!).ToList();
        if (unmatchedGoods || goods.Count == 0)
        {
            return [];
        }

        var needed = goods
            .Select(g => (Item: g.Key, Total: g.Sum(r => UnitConversion.ToBase(r.Line.Unit, r.Line.Quantity, g.Key).BaseQuantity ?? 0)))
            .ToList();

        return snapshot.PurchaseOrders
            .Where(order => Ownership.Owns(supplier, order))
            .Where(order => needed.All(n => order.Lines.Any(l =>
                Ownership.IsLineOf(l, n.Item) && OpenBase(snapshot, l, n.Item) > 0 && OpenBase(snapshot, l, n.Item) >= n.Total)))
            .ToList();
    }

    private static decimal OpenBase(MatchingSnapshot snapshot, PurchaseOrderLine line, SupplierItem item) =>
        snapshot.OpenQuantity(line) * item.ConversionFactorToBase;

    // ---------- lines ----------

    private static List<LineMatchOutcome> AllocateLines(
        List<ResolvedLine> resolved, PurchaseOrder? order, MatchingSnapshot snapshot, List<MatchConcern> concerns)
    {
        // Convert every goods line to the base unit first, so lines of the same item can be summed.
        var converted = resolved.ToDictionary(
            r => r,
            r => r.Resolution.Item is { } item ? UnitConversion.ToBase(r.Line.Unit, r.Line.Quantity, item) : (Status: UnitStatus.Same, BaseQuantity: (decimal?)null));

        // For each item on the document: which order line takes it, and how much of it the document asks for in total.
        var allocation = new Dictionary<SupplierItem, (PurchaseOrderLine? Line, decimal Total)>();
        foreach (var group in resolved.Where(r => r.Resolution.Item is not null).GroupBy(r => r.Resolution.Item!))
        {
            var total = group.Sum(r => converted[r].BaseQuantity ?? 0);
            var chosen = order is null ? null : ChoosePurchaseOrderLine(order, group.Key, total, snapshot, group.First().Line.LineNumber, concerns);
            allocation[group.Key] = (chosen, total);
        }

        var outcomes = new List<LineMatchOutcome>();
        foreach (var r in resolved)
        {
            outcomes.Add(r.Resolution.Item is { } item
                ? ItemOutcome(r, item, converted[r], allocation[item], snapshot, concerns)
                : UnmatchedOrChargeOutcome(r, order, snapshot, concerns));
        }

        return outcomes;
    }

    /// <summary>
    /// The same item can be on several order lines (different delivery dates, say). Take the line whose open quantity
    /// just fits the document; if two lines fit equally well, that is a tie and a human decides.
    /// </summary>
    private static PurchaseOrderLine? ChoosePurchaseOrderLine(
        PurchaseOrder order, SupplierItem item, decimal total, MatchingSnapshot snapshot, int lineNumber, List<MatchConcern> concerns)
    {
        var onOrder = order.Lines.Where(l => Ownership.IsLineOf(l, item)).ToList();
        var open = onOrder.Where(l => l.Status != PurchaseOrderLineStatus.Closed).ToList();
        var candidates = (open.Count > 0 ? open : onOrder).Select(l => (Line: l, Open: OpenBase(snapshot, l, item))).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var fitting = candidates.Where(c => c.Open >= total).OrderBy(c => c.Open).ToList();
        var best = fitting.Count > 0 ? fitting[0] : candidates.OrderByDescending(c => c.Open).First();
        var tied = (fitting.Count > 0 ? fitting : candidates).Count(c => c.Open == best.Open) > 1;

        if (tied)
        {
            concerns.Add(new MatchConcern(ConcernKind.AmbiguousPurchaseOrderLine, lineNumber,
                $"'{item.SupplierItemCode}' is on several lines of {order.Number} with the same open quantity."));
        }

        return best.Line;
    }

    private static LineMatchOutcome ItemOutcome(
        ResolvedLine r, SupplierItem item, (UnitStatus Status, decimal? BaseQuantity) unit,
        (PurchaseOrderLine? Line, decimal Total) allocation, MatchingSnapshot snapshot, List<MatchConcern> concerns)
    {
        if (r.Resolution.Method == MatchMethod.FuzzyDescription)
        {
            concerns.Add(new MatchConcern(ConcernKind.FuzzyLineMatch, r.Line.LineNumber,
                $"Line {r.Line.LineNumber} matched '{item.SupplierItemCode}' by description only (score {r.Resolution.Confidence:0.00})."));
        }

        var isCharge = ChargeLines.LooksLikeCharge(r.Line.Description) || ChargeLines.LooksLikeCharge(item.SupplierDescription);
        return new LineMatchOutcome(
            r.Line, item, r.Resolution.Method, r.Resolution.Confidence, isCharge,
            allocation.Line, allocation.Line?.ReceiptLines ?? [],
            unit.Status, unit.BaseQuantity, allocation.Total,
            allocation.Line is null ? null : QuantitiesOf(allocation.Line, item, snapshot));
    }

    private static LineMatchOutcome UnmatchedOrChargeOutcome(ResolvedLine r, PurchaseOrder? order, MatchingSnapshot snapshot, List<MatchConcern> concerns)
    {
        var none = new LineMatchOutcome(r.Line, null, MatchMethod.None, 0, r.IsUncodedCharge, null, [], UnitStatus.Same, null, null, null);
        if (!r.IsUncodedCharge || order is null)
        {
            return none;
        }

        var chargeLines = order.Lines.Where(l => ChargeLines.LooksLikeCharge(l.Description)).ToList();
        if (chargeLines.Count == 0)
        {
            return none; // a charge nobody ordered
        }

        var description = Normaliser.NormaliseDescription(r.Line.Description);
        var scored = chargeLines
            .Select(l => (Line: l, Score: Fuzz.WeightedRatio(description, Normaliser.NormaliseDescription(l.Description))))
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored.Count > 1 && scored[0].Score - scored[1].Score < 3)
        {
            concerns.Add(new MatchConcern(ConcernKind.AmbiguousChargeLine, r.Line.LineNumber,
                $"Charge '{r.Line.Description}' fits several charge lines of {order.Number}."));
        }

        var chosen = scored[0].Line;
        var item = chosen.SupplierItem;
        return new LineMatchOutcome(
            r.Line, item, MatchMethod.ChargeKeyword, 0.8, true,
            chosen, chosen.ReceiptLines, UnitStatus.Same, r.Line.Quantity, r.Line.Quantity,
            item is null ? null : QuantitiesOf(chosen, item, snapshot));
    }

    private static PurchaseOrderLineQuantities QuantitiesOf(PurchaseOrderLine line, SupplierItem item, MatchingSnapshot snapshot)
    {
        var factor = item.ConversionFactorToBase;
        return new PurchaseOrderLineQuantities(
            Ordered: line.Quantity * factor,
            Received: line.ReceiptLines.Sum(l => l.Quantity) * factor,
            AlreadyInvoiced: snapshot.InvoicedQuantity(line) * factor);
    }
}
