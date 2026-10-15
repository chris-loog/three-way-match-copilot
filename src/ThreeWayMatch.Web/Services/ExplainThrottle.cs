using System.Threading.RateLimiting;

namespace ThreeWayMatch.Web.Services;

/// <summary>
/// Limits how often one address can ask for an explanation, because each one is a paid model call and the demo site is public.
/// A page handler cannot carry a rate-limit attribute of its own, so the handler asks this instead.
/// </summary>
public sealed class ExplainThrottle : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(
        address => RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

    public bool TryAcquire(string address)
    {
        using var lease = _limiter.AttemptAcquire(address);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
