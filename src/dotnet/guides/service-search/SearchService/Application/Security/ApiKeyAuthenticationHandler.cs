using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SearchService.Application.Options;

namespace SearchService.Application.Security;

/// <summary>
/// Authenticates the X-Api-Key header against the configured keys. Keys are compared as SHA-256
/// hashes in constant time, so neither the comparison time nor the stored form leaks the key.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly IOptionsMonitor<AuthOptions> auth;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<AuthOptions> auth
    )
        : base(options, logger, encoder)
    {
        this.auth = auth;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || string.IsNullOrEmpty(values.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(values.ToString()));
        foreach (var entry in auth.CurrentValue.ApiKeys)
        {
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(entry.Key));
            if (CryptographicOperations.FixedTimeEquals(presented, expected))
            {
                var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, entry.Name)], SchemeName);
                return Task.FromResult(
                    AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName))
                );
            }
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        WriteProblemAsync(
            StatusCodes.Status401Unauthorized,
            "An API key is required.",
            $"Send a valid key in the {HeaderName} header."
        );

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteProblemAsync(StatusCodes.Status403Forbidden, "This API key may not use this endpoint.", null);

    private async Task WriteProblemAsync(int status, string title, string? detail)
    {
        Response.StatusCode = status;
        var problems = Context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = Context,
                ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = status,
                    Title = title,
                    Detail = detail,
                },
            }
        );
    }
}
