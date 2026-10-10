using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>
/// LOW_CONFIDENCE: a key field was read with too little confidence, or our own reading of the printed text disagreed with the
/// model's (the cross-check sets such a field to 0). A document with no lines at all is unreadable too: nothing could be
/// checked, so it must not pass.
/// </summary>
public sealed class LowConfidenceRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var fields = context.Invoice is { } invoice
            ? invoice.LowConfidenceFields(context.Options.ConfidenceThreshold).ToList()
            : context.LowConfidenceFields.ToList();

        if (context.Invoice is { Lines.Count: 0 })
        {
            fields.Add("Lines");
        }

        if (fields.Count > 0)
        {
            yield return new Finding(DiscrepancyCode.LowConfidence, Severity.Warning, null,
                $"confidence of at least {context.Options.ConfidenceThreshold:0.00}", string.Join(", ", fields),
                context.Invoice?.GrossTotal.Value ?? 0m);
        }
    }
}
