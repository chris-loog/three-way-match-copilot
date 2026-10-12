using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Web.Services;

public enum InboxFilter
{
    NeedsReview,
    OnHold,
    AutoApproved,
    Rejected,
    Reviewed,
}

public sealed record InboxCounts(int NeedsReview, int OnHold, int AutoApproved, int Rejected, int Reviewed, int Failed);

public sealed record FindingView(DiscrepancyCode Code, Severity Severity, int? LineNumber, string? Expected, string? Actual, decimal AmountAtStake);

public sealed record InboxRow(
    int DocumentId, string Supplier, string? Number, decimal? Gross, string? Currency, decimal AmountAtStake,
    IReadOnlyList<FindingView> Findings, DateTimeOffset ReceivedAt, DocumentStatus Status, bool Reviewed, string? PurchaseOrder);

/// <summary>The three quantities of a three-way match for one line, all in the item's base unit.</summary>
public sealed record Tally(decimal Ordered, decimal Received, decimal InvoicedBefore, decimal ThisInvoice, string Unit)
{
    /// <summary>The scale all three bars share, so their lengths compare.</summary>
    public decimal Scale => new[] { Ordered, Received, InvoicedBefore + ThisInvoice, 0.0001m }.Max();

    /// <summary>How much this invoice bills beyond what has arrived and was not yet billed.</summary>
    public decimal Overshoot => Math.Max(0, InvoicedBefore + ThisInvoice - Received);
}

public sealed record LineView(
    int LineNumber, string? ItemCode, string? Description, decimal? Quantity, string? Unit, decimal? UnitPrice, decimal? LineTotal, double? Confidence,
    int? LineMatchId, MatchMethod? Method, double? MatchConfidence, int? PoLineNumber, string? PoDescription, string? PoUnit, decimal? PoUnitPrice,
    Tally? Tally, IReadOnlyList<FindingView> Findings);

public sealed record FieldView(string Name, string Label, string? Display, string? EditValue, double? Confidence, bool Corrected, bool Editable);

public sealed record DocumentDetail(
    int Id, string BlobPath, DocumentType Type, DocumentStatus Status, string ModelId, DateTimeOffset ReceivedAt,
    Decision? Decision, decimal AmountAtStake, string? Supplier, string? PurchaseOrder, string? Currency,
    bool Reviewed, string? ReviewedBy, DateTimeOffset? ReviewedAt, string? OverrideReason, bool BankChangeVerified, string? ErrorMessage,
    IReadOnlyList<FieldView> Header, IReadOnlyList<LineView> Lines, IReadOnlyList<FindingView> Findings)
{
    public bool CanAct => !Reviewed && Decision is Domain.Ap.Decision.NeedsReview or Domain.Ap.Decision.OnHold;
    public bool NeedsBankVerification => Findings.Any(f => f.Code == DiscrepancyCode.BankAccountChanged);
}

public sealed record DashboardStats(
    int Processed, int AutoApproved, int OpenExceptions, decimal OpenAmount,
    IReadOnlyList<(DiscrepancyCode Code, int Count)> TopFindings,
    IReadOnlyList<(string Field, int Corrections, int Total)> FieldAccuracy,
    IReadOnlyList<(string Supplier, int Corrections, int Documents)> SupplierCorrections,
    IReadOnlyList<(string Decision, int Count)> Decisions)
{
    public double StraightThroughRate => Processed == 0 ? 0 : (double)AutoApproved / Processed;
}

public sealed record FailedRow(int DocumentId, string BlobPath, DocumentType Type, string? Error, DateTimeOffset ReceivedAt);
