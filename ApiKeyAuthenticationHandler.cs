using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace WordToPdfService.Auth;

public sealed class ApiKeyOptions
{
    /// <summary>
    /// Whitespace- or comma-separated list of allowed API keys.
    /// Configure via Auth:ApiKey:Keys (e.g. environment variable AUTH__APIKEY__KEYS).
    /// </summary>
    public string Keys { get; set; } = string.Empty;
}

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";

    private readonly HashSet<string> _validKeys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ApiKeyOptions> apiKeyOptions)
        : base(options, logger, encoder)
    {
        _validKeys = (apiKeyOptions.Value.Keys ?? string.Empty)
            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var providedKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        var key = providedKey.ToString();
        if (string.IsNullOrWhiteSpace(key) || !_validKeys.Contains(key))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "creatio-client"),
            new Claim("auth_method", "api_key")
        }, SchemeName);

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties(),
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
