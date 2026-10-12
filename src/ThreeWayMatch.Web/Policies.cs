namespace ThreeWayMatch.Web;

public static class Policies
{
    public const string Reviewer = "Reviewer";
    public const string Upload = "upload";
}

/// <summary>Whether this is the public demo (fixed reviewer, reset button, sample-data notice).</summary>
public sealed record DemoMode(bool Enabled);
