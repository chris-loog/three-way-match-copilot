using System.Text.RegularExpressions;
using FuzzySharp;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Matching;

/// <summary>Finds the supplier: by VAT number first, then by a close name. A name match is a guess and says so.</summary>
internal static partial class SupplierMatcher
{
    private const int MinimumNameScore = 90;
    private const int MinimumLeadOverRunnerUp = 5;

    public static SupplierMatch Match(MatchInput input, MatchingSnapshot snapshot, List<MatchConcern> concerns)
    {
        if (Normaliser.TryNormaliseVatNumber(input.SupplierVatNumber, out var vat))
        {
            var byVat = snapshot.Suppliers.FirstOrDefault(s => Normaliser.TryNormaliseVatNumber(s.VatNumber, out var own) && own == vat);
            if (byVat is not null)
            {
                return new SupplierMatch(byVat, SupplierMatchMethod.VatNumber, 1.0);
            }
        }

        var printed = NameKey(input.SupplierName);
        if (printed.Length == 0)
        {
            return new SupplierMatch(null, SupplierMatchMethod.None, 0);
        }

        var scored = snapshot.Suppliers
            .Select(s => (Supplier: s, Score: Fuzz.Ratio(printed, NameKey(s.Name))))
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scored.FirstOrDefault();
        var runnerUp = scored.Count > 1 ? scored[1].Score : 0;
        if (best.Supplier is null || best.Score < MinimumNameScore || best.Score - runnerUp < MinimumLeadOverRunnerUp)
        {
            return new SupplierMatch(null, SupplierMatchMethod.None, 0);
        }

        concerns.Add(new MatchConcern(ConcernKind.FuzzySupplier, null,
            $"Supplier found by name only ('{input.SupplierName}' ~ '{best.Supplier.Name}', score {best.Score})."));
        return new SupplierMatch(best.Supplier, SupplierMatchMethod.FuzzyName, best.Score / 100.0);
    }

    /// <summary>Lower case, punctuation and legal forms (OÜ, AS, Ltd...) removed, so "Baltic Board Supply OÜ" ~ "BALTIC BOARD SUPPLY".</summary>
    private static string NameKey(string? name)
    {
        var text = Punctuation().Replace((name ?? string.Empty).ToLowerInvariant(), " ");
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !LegalForms.Contains(word)));
    }

    private static readonly HashSet<string> LegalForms = ["oü", "ou", "as", "ltd", "oy", "ab", "gmbh", "sia", "uab", "llc", "inc"];

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Punctuation();
}
