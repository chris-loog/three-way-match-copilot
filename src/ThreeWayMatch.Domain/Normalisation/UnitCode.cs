namespace ThreeWayMatch.Domain.Normalisation;

/// <summary>
/// The units that appear on timber, panel, glue, hardware, packaging and transport paperwork, one code per meaning.
/// Solid and stacked cubic metres are different codes on purpose and are never converted into each other.
/// </summary>
public enum UnitCode
{
    Unknown = 0,

    /// <summary>Solid cubic metre (tihumeeter, tm): the wood itself, without the air between boards.</summary>
    CubicMetre,

    /// <summary>Stacked cubic metre (ruumimeeter, rm): a stack including the gaps. Roughly 0.6 solid, but that depends on the wood.</summary>
    StackedCubicMetre,

    RunningMetre,
    SquareMetre,
    Piece,
    Litre,
    Kilogram,
    Kilometre,
    Trip,
    Box,
    Pack,
    Pallet,
    Can,
    Roll,
}
