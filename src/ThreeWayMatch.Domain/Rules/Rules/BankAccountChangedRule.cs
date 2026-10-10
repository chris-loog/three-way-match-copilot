using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>
/// BANK_ACCOUNT_CHANGED: a valid IBAN that differs from the supplier's master data. Classic invoice fraud, so a Block:
/// payment is held until the new account is verified. An IBAN with a wrong checksum is not reported here; it is most likely
/// a misread and is handled as LOW_CONFIDENCE.
/// </summary>
public sealed class BankAccountChangedRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.Invoice || context.Invoice is not { } invoice || context.Supplier is not { } supplier)
        {
            yield break;
        }

        var printed = Normaliser.CheckIban(invoice.Iban.RawText ?? invoice.Iban.Value);
        var master = Normaliser.CheckIban(supplier.Iban);
        if (printed.Status == IbanStatus.Valid && printed.Normalised != master.Normalised)
        {
            yield return new Finding(DiscrepancyCode.BankAccountChanged, Severity.Block, null,
                master.Normalised, printed.Normalised, invoice.GrossTotal.Value ?? 0m);
        }
    }
}
