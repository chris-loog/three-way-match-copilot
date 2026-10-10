using System.Globalization;
using System.Numerics;
using System.Text;
using Bogus;

namespace DocGen.Seeding;

/// <summary>
/// Makes Estonian IBANs with a valid mod-97 checksum and a bank code that is not on the regulator's list of
/// credit institution codes, so no generated account number can belong to a real account.
/// </summary>
internal static class IbanGenerator
{
    // Assigned codes seen in the Finantsinspektsioon list: 10, 16, 22, 42, 75, 77, 96 (and a few older ones).
    // 04 is not among them. The list covers the major banks only, so treat this as "very unlikely to be real", not proof.
    private const string UnassignedBankCode = "04";

    public static string Create(Randomizer random)
    {
        var accountDigits = new StringBuilder();
        for (var i = 0; i < 12; i++)
        {
            accountDigits.Append(random.Int(0, 9));
        }

        // Estonian BBAN: bank code (2) + branch (2) + account number and check digit (12) = 16 digits.
        var bban = UnassignedBankCode + "00" + accountDigits;

        // ISO 13616: move "EE00" to the end, turn letters into numbers (E = 14), check digits = 98 - (n mod 97).
        var remainder = (int)(BigInteger.Parse(bban + "141400", CultureInfo.InvariantCulture) % 97);
        var checkDigits = 98 - remainder;

        return $"EE{checkDigits:D2}{bban}";
    }

    /// <summary>Checks the mod-97 rule. Used by the seeder's self-check; the production validator is written in step 3.2.</summary>
    public static bool HasValidChecksum(string iban)
    {
        var rearranged = iban[4..] + iban[..4];
        var digits = new StringBuilder();
        foreach (var c in rearranged)
        {
            digits.Append(char.IsLetter(c) ? (c - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString());
        }

        return BigInteger.Parse(digits.ToString(), CultureInfo.InvariantCulture) % 97 == 1;
    }
}
