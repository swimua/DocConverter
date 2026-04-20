using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using WordToPdfService.Auth;
using WordToPdfService.Endpoints;
using WordToPdfService.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ---------- Options ----------
builder.Services.Configure<ConverterOptions>(
    builder.Configuration.GetSection("Converter"));
builder.Services.Configure<ApiKeyOptions>(
    builder.Configuration.GetSection("Auth:ApiKey"));

// ---------- Services ----------
builder.Services.AddSingleton<IDocumentConverter, LibreOfficeConverter>();

// ---------- Auth ----------
// Supports BOTH "X-API-Key" header and JWT Bearer.
// A request succeeds if EITHER scheme authenticates it.
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "ApiKeyOrJwt";
        options.DefaultChallengeScheme = "ApiKeyOrJwt";
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var jwt = builder.Configuration.GetSection("Auth:Jwt");
        var key = jwt["SigningKey"];
        if (!string.IsNullOrWhiteSpace(key))
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrWhiteSpace(jwt["Issuer"]),
                ValidIssuer = jwt["Issuer"],
                ValidateAudience = !string.IsNullOrWhiteSpace(jwt["Audience"]),
                ValidAudience = jwt["Audience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            };
        }
    })
    .AddPolicyScheme("ApiKeyOrJwt", "ApiKeyOrJwt", options =>
    {
        options.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                ? ApiKeyAuthenticationHandler.SchemeName
                : JwtBearerDefaults.AuthenticationScheme;
    });

builder.Services.AddAuthorization();

// ---------- Request size limits ----------
// .docx can occasionally be large (embedded media). Default to 50 MB.
var maxMb = builder.Configuration.GetValue<int?>("Converter:MaxUploadMb") ?? 50;
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = (long)maxMb * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = (long)maxMb * 1024 * 1024;
});

// ---------- Misc ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment() ||
    builder.Configuration.GetValue<bool>("EnableSwagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapConversionEndpoints();

app.Run();
