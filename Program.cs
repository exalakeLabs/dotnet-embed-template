using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

DotEnv.Load(Path.Combine(AppContext.BaseDirectory, ".env"));
DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5000");

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/sigma/embed-url", (HttpRequest request) =>
{
    var settings = SigmaSettings.FromEnvironment();
    var embedUser = EmbedUser.FromRequestOrDefaults(request);

    var now = DateTimeOffset.UtcNow;
    var payload = BuildSigmaPayload(settings, embedUser, now);
    var jwt = JwtSigner.Sign(payload, settings.EmbedSecret);
    var embedUrl = BuildEmbedUrl(settings, jwt);

    return Results.Ok(new
    {
        embedUrl,
        expiresAt = now.AddSeconds(settings.JwtTtlSeconds)
    });
});

app.MapGet("/api/sigma/jwt-preview", (HttpRequest request) =>
{
    var settings = SigmaSettings.FromEnvironment();
    var embedUser = EmbedUser.FromRequestOrDefaults(request);
    var payload = BuildSigmaPayload(settings, embedUser, DateTimeOffset.UtcNow);

    return Results.Ok(payload);
});

app.Run();

static Dictionary<string, object> BuildSigmaPayload(
    SigmaSettings settings,
    EmbedUser embedUser,
    DateTimeOffset now)
{
    var issuedAt = now.ToUnixTimeSeconds();
    var expiresAt = now.AddSeconds(settings.JwtTtlSeconds).ToUnixTimeSeconds();
    string ClaimValue(string key, string fallback) =>
        embedUser.UserAttributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    var payload = new Dictionary<string, object>
    {
        // Standard JWT claims.
        ["iss"] = settings.ClientId,
        ["sub"] = embedUser.ExternalUserId,
        ["jti"] = Guid.NewGuid().ToString("N"),
        ["iat"] = issuedAt,
        ["exp"] = expiresAt,

        // Sigma embed user context. Adjust these names if your Sigma embed
        // configuration expects a different tenant-specific payload shape.
        ["email"] = embedUser.Email,
        ["external_user_id"] = embedUser.ExternalUserId,
        ["first_name"] = embedUser.FirstName,
        ["last_name"] = embedUser.LastName,
        ["teams"] = embedUser.Teams,
        ["account_type"] = settings.AccountType,
        ["mode"] = settings.Mode,
        ["session_length"] = settings.SessionLengthSeconds,

        // Example user attributes for row-level security or personalization.
        ["user_attributes"] = new Dictionary<string, object>
        {
            ["department"] = ClaimValue("department", "Sales"),
            ["region"] = ClaimValue("region", "West")
        }
    };

    return payload;
}

static string BuildEmbedUrl(SigmaSettings settings, string jwt)
{
    var baseUrl = settings.BaseUrl.TrimEnd('/');
    var embedPath = settings.EmbedPath.TrimStart('/');
    var separator = embedPath.Contains('?') ? "&" : "?";

    return $"{baseUrl}/embed/{embedPath}{separator}{settings.JwtQueryParameter}={Uri.EscapeDataString(jwt)}";
}

sealed record SigmaSettings(
    string BaseUrl,
    string EmbedPath,
    string JwtQueryParameter,
    string ClientId,
    string EmbedSecret,
    string AccountType,
    string Mode,
    int SessionLengthSeconds,
    int JwtTtlSeconds)
{
    public static SigmaSettings FromEnvironment()
    {
        return new SigmaSettings(
            Required("SIGMA_BASE_URL"),
            Required("SIGMA_EMBED_PATH"),
            Environment.GetEnvironmentVariable("SIGMA_JWT_QUERY_PARAMETER") ?? ":jwt",
            Required("SIGMA_CLIENT_ID"),
            Required("SIGMA_EMBED_SECRET"),
            Environment.GetEnvironmentVariable("SIGMA_DEFAULT_ACCOUNT_TYPE") ?? "Viewer",
            Environment.GetEnvironmentVariable("SIGMA_DEFAULT_MODE") ?? "userbacked",
            IntValue("SIGMA_SESSION_LENGTH_SECONDS", 3600),
            IntValue("SIGMA_JWT_TTL_SECONDS", 300));
    }

    static string Required(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} must be set in .env or environment variables.");
        }

        return value;
    }

    static int IntValue(string key, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(key), out var value) ? value : fallback;
}

sealed record EmbedUser(
    string Email,
    string ExternalUserId,
    string FirstName,
    string LastName,
    string[] Teams,
    IReadOnlyDictionary<string, string> UserAttributes)
{
    public static EmbedUser FromRequestOrDefaults(HttpRequest request)
    {
        var query = request.Query;
        var teams = Value("teams", "SIGMA_DEFAULT_TEAMS", "Customers")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var userAttributes = query
            .Where(item => item.Key.StartsWith("ua_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                item => item.Key[3..],
                item => item.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

        return new EmbedUser(
            Value("email", "SIGMA_DEFAULT_EMAIL", "viewer@example.com"),
            Value("externalUserId", "SIGMA_DEFAULT_EXTERNAL_USER_ID", "viewer-123"),
            Value("firstName", "SIGMA_DEFAULT_FIRST_NAME", "Embed"),
            Value("lastName", "SIGMA_DEFAULT_LAST_NAME", "Viewer"),
            teams,
            userAttributes);

        string Value(string queryKey, string envKey, string fallback)
        {
            var queryValue = query[queryKey].ToString();
            if (!string.IsNullOrWhiteSpace(queryValue))
            {
                return queryValue;
            }

            return Environment.GetEnvironmentVariable(envKey) ?? fallback;
        }
    }
}

static class JwtSigner
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Sign(Dictionary<string, object> payload, string secret)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };

        var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions));
        var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var unsignedToken = $"{encodedHeader}.{encodedPayload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsignedToken)));

        return $"{unsignedToken}.{signature}";
    }

    static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

static class DotEnv
{
    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = line[..equalsIndex].Trim();
            var value = line[(equalsIndex + 1)..].Trim().Trim('"');
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
