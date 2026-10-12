using Microsoft.Azure.Functions.Worker;
using ThreeWayMatch.Infrastructure.Pipeline;

namespace ThreeWayMatch.Functions;

/// <summary>
/// Every day at 06:00 UTC, matches the invoices on hold again. If the goods have been booked since, they move on.
/// The review UI will be able to trigger the same re-check for one document.
/// </summary>
public sealed class RecheckOnHoldFunction(DocumentMatcher matcher)
{
    [Function("RecheckOnHold")]
    public Task Run([TimerTrigger("0 0 6 * * *")] TimerInfo timer, CancellationToken cancellationToken) =>
        matcher.RematchOnHoldAsync(cancellationToken);
}
