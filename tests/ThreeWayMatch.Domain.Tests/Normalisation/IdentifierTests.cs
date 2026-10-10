using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Tests.Normalisation;

public class IdentifierTests
{
    [Theory]
    [InlineData("EE109532815", "EE109532815")]
    [InlineData("ee109532815", "EE109532815")]
    [InlineData("EE 109 532 815", "EE109532815")]
    [InlineData("EE-109532815", "EE109532815")]
    [InlineData("109532815", "EE109532815")]
    [InlineData("KMKR EE109532815", "EE109532815")]
    [InlineData("KMKR nr EE109532815", "EE109532815")]
    [InlineData("VAT no. EE109532815", "EE109532815")]
    [InlineData("  EE109532815. ", "EE109532815")]
    public void Normalises_estonian_vat_numbers(string text, string expected)
    {
        Assert.True(Normaliser.TryNormaliseVatNumber(text, out var vat));
        Assert.Equal(expected, vat);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EE10953281")]
    [InlineData("EE1095328150")]
    [InlineData("LV109532815")]
    [InlineData("EE10953281X")]
    [InlineData("KMKR")]
    public void Refuses_vat_numbers_that_are_not_estonian_or_not_complete(string? text)
    {
        Assert.False(Normaliser.TryNormaliseVatNumber(text, out _));
    }

    [Theory]
    [InlineData("11812518", "11812518")]
    [InlineData("1181 2518", "11812518")]
    [InlineData("Reg. 11812518", "11812518")]
    [InlineData("Registrikood 11812518", "11812518")]
    [InlineData("Reg. code: 11812518", "11812518")]
    public void Normalises_registry_codes(string text, string expected)
    {
        Assert.True(Normaliser.TryNormaliseRegistryCode(text, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1181251")]
    [InlineData("118125180")]
    [InlineData("ABC12345")]
    public void Refuses_registry_codes_that_are_not_eight_digits(string? text)
    {
        Assert.False(Normaliser.TryNormaliseRegistryCode(text, out _));
    }

    [Theory]
    [InlineData("EE78 0400 1503 1113 4947", "EE780400150311134947")]
    [InlineData("  ee78 0400 1503 1113 4947 ", "EE780400150311134947")]
    [InlineData("EE780400150311134947", "EE780400150311134947")]
    [InlineData("IBAN: EE78 0400 1503 1113 4947", "EE780400150311134947")]
    [InlineData("Bank account (IBAN): EE78 0400 1503 1113 4947", "EE780400150311134947")]
    [InlineData("GB82 WEST 1234 5698 7654 32", "GB82WEST12345698765432")]
    public void A_correct_iban_is_valid_and_comes_back_without_spaces(string text, string expected)
    {
        var check = Normaliser.CheckIban(text);

        Assert.Equal(IbanStatus.Valid, check.Status);
        Assert.Equal(expected, check.Normalised);
    }

    [Theory]
    [InlineData("EE78 0400 1503 1113 4948")] // last digit changed: likely a misread
    [InlineData("EE79 0400 1503 1113 4947")] // check digits changed
    [InlineData("GB82 WEST 1234 5698 7654 33")]
    public void An_iban_with_a_wrong_checksum_is_flagged_as_a_probable_misread_not_as_a_changed_account(string text)
    {
        var check = Normaliser.CheckIban(text);

        Assert.Equal(IbanStatus.InvalidChecksum, check.Status);
        Assert.NotNull(check.Normalised);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EE78")]
    [InlineData("XX")]
    [InlineData("EE78 0400 1503 1113 494")] // an Estonian IBAN has exactly 20 characters
    [InlineData("EE78 0400 1503 1113 494O")] // letter O where a digit belongs
    [InlineData("1234 5678")]
    public void Text_that_is_not_shaped_like_an_iban_is_malformed(string? text)
    {
        Assert.Equal(IbanStatus.Malformed, Normaliser.CheckIban(text).Status);
    }
}
