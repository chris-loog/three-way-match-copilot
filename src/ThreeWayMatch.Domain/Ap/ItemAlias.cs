using ThreeWayMatch.Domain.Erp;

namespace ThreeWayMatch.Domain.Ap;

/// <summary>
/// A learned mapping from a supplier's free-text line description to one of their items.
/// Saved when a reviewer confirms a fuzzy match, so the same line matches by alias next time.
/// </summary>
public class ItemAlias
{
    public int Id { get; set; }
    public int SupplierItemId { get; set; }

    /// <summary>The description in normalised form (trimmed, lower case, single spaces).</summary>
    public required string NormalisedDescription { get; set; }

    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public SupplierItem? SupplierItem { get; set; }
}
