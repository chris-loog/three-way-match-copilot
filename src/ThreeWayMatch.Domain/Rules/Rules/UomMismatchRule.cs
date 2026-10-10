using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Matching;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>
/// UOM_MISMATCH: the unit on the document differs from the supplier's and cannot be converted, or is not a unit we know.
/// The classic case is stacked against solid cubic metres, which are never converted automatically.
/// </summary>
public sealed class UomMismatchRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        foreach (var line in context.Match.Lines.Where(l => l.SupplierItem is not null && l.UnitStatus is UnitStatus.Mismatch or UnitStatus.Unknown))
        {
            var amount = Math.Round((line.Line.Quantity ?? 0) * (line.Line.UnitPrice ?? 0), 2);
            yield return new Finding(DiscrepancyCode.UomMismatch, Severity.Warning,
                line.Line.LineNumber, line.SupplierItem!.SupplierUnit, string.IsNullOrWhiteSpace(line.Line.Unit) ? "(missing)" : line.Line.Unit, amount);
        }
    }
}
