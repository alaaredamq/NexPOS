using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Coduo.Tools;

/// <summary>Where the desktop tools send what they sign: the cloud address and an admin app token (never the password).</summary>
public sealed record CloudLink(string Url, string Token, string Username);

/// <summary>
/// Lets LicenseIssuer and the Keygen app record every key they sign in the cloud (POST /api/admin/v1/licenses/record), so the portal
/// knows every licence without double entry. The link is kept in ~/.nexpos-license/cloud.json, next to the private key: it holds an
/// admin token, so it is as private as that folder. Recording is best effort: a key is never held back because the cloud is down.
/// </summary>
public static class CloudLicenseLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexpos-license", "cloud.json");

    public static CloudLink? Load()
    {
        try
        {
            return File.Exists(ConfigPath) ? JsonSerializer.Deserialize<CloudLink>(File.ReadAllText(ConfigPath), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(CloudLink link)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(link, Json));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void Forget()
    {
        if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
    }

    private static HttpClient Client(string url, string? token = null)
    {
        var http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(20) };
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>"https://coduos.com" from what was typed; null when it is not an http(s) address.</summary>
    public static string? NormalizeUrl(string? text)
    {
        var t = (text ?? "").Trim().TrimEnd('/');
        if (t.Length == 0) return null;
        if (!t.Contains("://", StringComparison.Ordinal)) t = "https://" + t;
        return Uri.TryCreate(t, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" ? u.GetLeftPart(UriPartial.Authority) : null;
    }

    /// <summary>Signs in as the admin and keeps a token for this PC. Returns the link, or why it failed.</summary>
    public static async Task<(CloudLink? Link, string Message)> SignInAsync(string url, string username, string password)
    {
        var address = NormalizeUrl(url);
        if (address is null) return (null, "Enter the cloud address, like https://coduos.com");
        try
        {
            using var http = Client(address);
            var response = await http.PostAsJsonAsync("api/admin/v1/auth/login",
                new { username, password, deviceName = $"Keygen on {Environment.MachineName}" });
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return (null, ErrorText(body, response));
            var token = JsonDocument.Parse(body).RootElement.GetProperty("token").GetString()!;
            var link = new CloudLink(address, token, username);
            Save(link);
            return (link, $"Connected to {address} as {username}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return (null, $"Could not reach {address}: {ex.Message}");
        }
    }

    /// <summary>Records a key just signed. Never throws: the message says what happened, for the screen.</summary>
    public static async Task<(bool Ok, string Message)> RecordAsync(CloudLink link, string keyText)
    {
        try
        {
            using var http = Client(link.Url, link.Token);
            var response = await http.PostAsJsonAsync("api/admin/v1/licenses/record", new { key = keyText, source = "Keygen" });
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return (false, "Not recorded in the cloud: " + ErrorText(body, response));
            var root = JsonDocument.Parse(body).RootElement;
            var client = root.TryGetProperty("clientName", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            return (true, client is null
                ? "Recorded in the cloud (not linked to a client yet: link it on the Licences page)."
                : $"Recorded in the cloud for {client}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (false, $"Not recorded in the cloud ({ex.Message}). The key is fine; paste it on the Licences page later.");
        }
    }

    /// <summary>Sends a whole issued.csv (keys signed before the link existed). Skips what the cloud already has.</summary>
    public static async Task<(bool Ok, string Message)> ImportAsync(CloudLink link, string csv)
    {
        try
        {
            using var http = Client(link.Url, link.Token);
            var response = await http.PostAsync("api/admin/v1/licenses/import", new StringContent(csv, Encoding.UTF8, "text/csv"));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return (false, ErrorText(body, response));
            var r = JsonDocument.Parse(body).RootElement;
            return (true, $"{r.GetProperty("added").GetInt32()} added ({r.GetProperty("linked").GetInt32()} linked to a client), "
                          + $"{r.GetProperty("alreadyKnown").GetInt32()} already there, {r.GetProperty("problems").GetArrayLength()} unreadable.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return (false, ex.Message);
        }
    }

    private static string ErrorText(string body, HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && body.Contains("unauthorized", StringComparison.Ordinal))
            return "the cloud sign-in has expired. Connect again.";
        try
        {
            var root = JsonDocument.Parse(body).RootElement;
            if (root.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } text) return text;
        }
        catch (JsonException) { }
        return $"{(int)response.StatusCode} {response.ReasonPhrase}";
    }
}
