using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Tests.Normalisation;

public class UnitTests
{
    [Theory]
    [InlineData("m3", UnitCode.CubicMetre)]
    [InlineData("m³", UnitCode.CubicMetre)]
    [InlineData("M3", UnitCode.CubicMetre)]
    [InlineData("tm", UnitCode.CubicMetre)]
    [InlineData("tihumeeter", UnitCode.CubicMetre)]
    [InlineData("rm", UnitCode.StackedCubicMetre)]
    [InlineData("ruumimeeter", UnitCode.StackedCubicMetre)]
    [InlineData("jm", UnitCode.RunningMetre)]
    [InlineData("jooksev meeter", UnitCode.RunningMetre)]
    [InlineData("tk", UnitCode.Piece)]
    [InlineData("tk.", UnitCode.Piece)]
    [InlineData("pcs", UnitCode.Piece)]
    [InlineData("PCS.", UnitCode.Piece)]
    [InlineData("pc", UnitCode.Piece)]
    [InlineData("l", UnitCode.Litre)]
    [InlineData("L", UnitCode.Litre)]
    [InlineData("ltr", UnitCode.Litre)]
    [InlineData("kg", UnitCode.Kilogram)]
    [InlineData("m2", UnitCode.SquareMetre)]
    [InlineData("m²", UnitCode.SquareMetre)]
    [InlineData("km", UnitCode.Kilometre)]
    [InlineData("reis", UnitCode.Trip)]
    [InlineData("trip", UnitCode.Trip)]
    [InlineData("karp", UnitCode.Box)]
    [InlineData("box", UnitCode.Box)]
    [InlineData("pakk", UnitCode.Pack)]
    [InlineData("pack", UnitCode.Pack)]
    [InlineData("pall", UnitCode.Pallet)]
    [InlineData("pallet", UnitCode.Pallet)]
    [InlineData("purk", UnitCode.Can)]
    [InlineData("can", UnitCode.Can)]
    [InlineData("rull", UnitCode.Roll)]
    [InlineData("roll", UnitCode.Roll)]
    [InlineData("  m³  ", UnitCode.CubicMetre)]
    public void Maps_the_spellings_suppliers_use_to_one_code(string text, UnitCode expected)
    {
        Assert.True(Normaliser.TryNormaliseUnit(text, out var unit));
        Assert.Equal(expected, unit);
    }

    [Fact]
    public void Solid_and_stacked_cubic_metres_are_different_units_and_never_the_same_code()
    {
        Normaliser.TryNormaliseUnit("tm", out var solid);
        Normaliser.TryNormaliseUnit("rm", out var stacked);

        Assert.NotEqual(solid, stacked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xyz")]
    [InlineData("m4")]
    public void Refuses_units_it_does_not_know_instead_of_guessing(string? text)
    {
        Assert.False(Normaliser.TryNormaliseUnit(text, out var unit));
        Assert.Equal(UnitCode.Unknown, unit);
    }
}
