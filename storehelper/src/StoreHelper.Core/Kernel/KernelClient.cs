using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StoreHelper.Core.Kernel;

public class KernelException : Exception
{
    public int StatusCode { get; }
    public KernelException(string message, int statusCode = 0, Exception inner = null) : base(message, inner) => StatusCode = statusCode;
}

/// <summary>Oturum yok/süresi dolmuş: kullanıcıdan yeniden giriş istenmeli.</summary>
public sealed class KernelAuthException : KernelException
{
    public KernelAuthException(string message) : base(message, 401) { }
}

public sealed class KernelSession
{
    public string Token { get; set; }
    public string Username { get; set; }
    public string FullName { get; set; }
    public string Role { get; set; }
    public string Company { get; set; }
}

/// <summary>Kernel'in /api/helper/* uçlarını çağırır (kullanıcı adı/şifre → token; ürün listesi).</summary>
public sealed class KernelClient : IDisposable
{
    readonly HttpClient _http;
    public string BaseUrl { get; }
    public string Token { get; set; }

    /// <summary>NemraPOS Kernel'in varsayılan portu (kernel/.env PORT). Adreste port yazılmamışsa bu kullanılır.</summary>
    public const int DefaultPort = 4750;
    /// <summary>Kayıtlı adres yokken giriş penceresinde önerilen adres (kernel aynı bilgisayardaysa).</summary>
    public const string DefaultUrl = "http://127.0.0.1:4750";

    static KernelClient()
    {
        // .NET Framework 4.8 varsayılanı eski TLS'i de açık bırakır; TLS 1.2/1.3'ü açıkça ekleriz (pos.nemra.net gibi HTTPS ters vekiller için).
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
    }

    public KernelClient(string baseUrl, string token = null, HttpMessageHandler handler = null)
    {
        BaseUrl = NormalizeUrl(baseUrl);
        Token = token;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NemraPOS-StoreHelper/0.1");
    }

    public void Dispose() => _http.Dispose();

    /// <summary>"192.168.1.10:4750" → "http://192.168.1.10:4750"; sondaki "/" ve "/admin" atılır.
    /// http için port yazılmamışsa varsayılan kernel portu (4750) eklenir: "192.168.1.10" → "http://192.168.1.10:4750".
    /// https (ters vekil) için port yazılmadıysa 443 kalır.</summary>
    public static string NormalizeUrl(string url)
    {
        var text = (url ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException("Kernel adresi boş olamaz.");
        if (!text.Contains("://")) text = "http://" + text;
        var uri = new Uri(text);
        var explicitPort = System.Text.RegularExpressions.Regex.IsMatch(text, @"^[A-Za-z][A-Za-z0-9+.-]*://[^/?#]*:\d+(?:[/?#]|$)");
        if (uri.Scheme == Uri.UriSchemeHttp && !explicitPort) return $"{uri.Scheme}://{uri.Host}:{DefaultPort}";
        // Authority varsayılan portu (80/443) atar; kullanıcı açıkça yazdıysa korunur.
        return explicitPort ? $"{uri.Scheme}://{uri.Host}:{uri.Port}" : $"{uri.Scheme}://{uri.Authority}";
    }

    async Task<JObject> SendAsync(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        if (Token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (body != null) request.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new KernelException($"Kernel'e zamanında ulaşılamadı ({BaseUrl}).", 0, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new KernelException($"Kernel'e bağlanılamadı ({BaseUrl}): {ex.InnerException?.Message ?? ex.Message}", 0, ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            JObject json = null;
            try { json = JObject.Parse(text); } catch { /* JSON değil */ }
            if (response.IsSuccessStatusCode) return json ?? new JObject();
            var message = (string)json?["message"] ?? $"Kernel hata döndü (HTTP {(int)response.StatusCode}).";
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new KernelAuthException(message);
            throw new KernelException(message, (int)response.StatusCode);
        }
    }

    public async Task<KernelSession> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Post, "/api/helper/login", new { username, password }, ct).ConfigureAwait(false);
        Token = (string)json["token"];
        return new KernelSession
        {
            Token = Token, Username = (string)json["user"]?["username"], FullName = (string)json["user"]?["fullName"],
            Role = (string)json["user"]?["role"], Company = (string)json["company"]
        };
    }

    /// <summary>Oturumu doğrular (token geçerli değilse KernelAuthException).</summary>
    public async Task<KernelSession> MeAsync(CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, "/api/helper/me", null, ct).ConfigureAwait(false);
        return new KernelSession { Token = Token, Username = (string)json["username"], FullName = (string)json["fullName"], Role = (string)json["role"], Company = (string)json["company"] };
    }

    /// <summary>Son GetAllProductsAsync çağrısının başındaki kernel saati (yerel saate çevrilmiş); eski kernel'de null.</summary>
    public DateTime? ServerTime { get; private set; }

    /// <summary>Tüm aktif ürünleri sayfa sayfa çeker. progress: (alınan, toplam).</summary>
    public async Task<List<Product>> GetAllProductsAsync(IProgress<(int done, int total)> progress = null, CancellationToken ct = default)
    {
        const int pageSize = 1000;
        var all = new List<Product>();
        ServerTime = null;
        for (var page = 0; ; page++)
        {
            var json = await SendAsync(HttpMethod.Get, $"/api/helper/items?page={page}&pageSize={pageSize}", null, ct).ConfigureAwait(false);
            var items = (JArray)json["items"];
            if (page == 0) ServerTime = ParseTime(json["serverTime"]);
            var total = (int)json["total"];
            foreach (var i in items) all.Add(ParseProduct(i));
            progress?.Report((all.Count, total));
            if (items.Count == 0 || all.Count >= total) break;
        }
        return all;
    }

    public static Product ParseProduct(JToken i) => new Product
    {
        Code = (string)i["code"] ?? "",
        Name = (string)i["name"] ?? "",
        UnitCode = (string)i["unitCode"] ?? "NIU",
        UnitName = (string)i["unitName"] ?? "",
        Weighed = (bool?)i["weighed"] ?? false,
        VatRate = (decimal?)i["vatRate"] ?? 0,
        NetPrice = (decimal?)i["netPrice"] ?? 0,
        GrossPrice = (decimal?)i["grossPrice"] ?? 0,
        CategoryCode = (string)i["categoryCode"] ?? "",
        CategoryName = (string)i["categoryName"] ?? "",
        Barcodes = ((JArray)i["barcodes"])?.ToObject<List<string>>() ?? new List<string>(),
        WeightCodes = ((JArray)i["weightCodes"])?.ToObject<List<string>>() ?? new List<string>(),
        Active = (bool?)i["active"] ?? true,
        CreatedAt = ParseTime(i["createdAt"]),
        PriceChangedAt = ParseTime(i["priceChangedAt"])
    };

    /// <summary>Kernel'in ISO UTC zamanı → yerel saat (Json.NET tarihi kendisi çözmüş de olabilir).</summary>
    static DateTime? ParseTime(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        if (token.Type == JTokenType.Date) return ((DateTime)token).ToLocalTime();
        return DateTime.TryParse((string)token, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime() : (DateTime?)null;
    }
}
