using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ThreeWayMatch.Web.Auth;

/// <summary>
/// Demo mode: everyone is the same reviewer, so a recruiter can open the public site and try it without an account.
/// Switched on by <c>Demo:Enabled</c>; with it off, the app signs in with Entra ID instead.
/// </summary>
public sealed class DemoAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Demo";
    public const string ReviewerRole = "Reviewer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "Demo reviewer"), new Claim(ClaimTypes.Role, ReviewerRole)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
