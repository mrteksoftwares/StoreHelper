using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StoreHelper.Core.Scales;

/// <summary>Terazi dosyasında kullanılabilecek sütunlar. Anahtarlar kayıtlı terazi tanımlarında saklanır; değiştirmeyin.</summary>
public static class ScaleColumns
{
    public static readonly (string Key, string Title, string Description)[] All =
    {
        ("plu", "PLU", "Terazi PLU numarası (tartı kısa kodu, baştaki sıfırlar atılmış)"),
        ("plu5", "PLU (5 hane)", "PLU, baştaki sıfırlarla 5 hane (00123)"),
        ("code", "Ürün kodu", "Mikro stok kodu"),
        ("name", "Ürün adı", "Terazi ekranına sığacak şekilde kısaltılır"),
        ("price", "Fiyat", "KDV dahil birim fiyat (fiyat biçimine göre)"),
        ("vat", "KDV %", "KDV oranı"),
        ("unit", "Birim", "KG veya AD"),
        ("unitno", "Birim no", "0 = kg, 1 = adet"),
        ("barcode", "Barkod", "Ürünün normal barkodu (varsa)"),
        ("barcodekind", "Barkod türü", "0 = ağırlıklı, 1 = tutarlı"),
        ("category", "Kategori", "Kategori adı")
    };

    public static string Title(string key) => All.FirstOrDefault(c => c.Key == key).Title ?? key;
}

public sealed class ScaleExportResult
{
    public int Count { get; set; }
    public int SkippedNoPlu { get; set; }
    public string Destination { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>Terazi için ürün listesini üretir (CSV metni) ve gönderir (dosya / TCP).</summary>
public static class ScaleExport
{
    public static Encoding GetEncoding(string name)
    {
        switch ((name ?? "").ToLowerInvariant())
        {
            case "utf-8": return new UTF8Encoding(false);
            case "ascii": return Encoding.ASCII;
            default:
                try { return Encoding.GetEncoding(1254); }
                catch (ArgumentException) { return new UTF8Encoding(false); } // 1254 kod sayfası yoksa güvenli varsayılan
        }
    }

    /// <summary>Terazide yer alacak ürünler: tartılan (KGM/GRM) VEYA tartı kodu (PLU) olanlar; aktif olanlar. PLU'su olmayanlar ayrıca sayılır.</summary>
    public static (List<Product> Included, int SkippedNoPlu) Select(IEnumerable<Product> products)
    {
        var included = new List<Product>();
        var skipped = 0;
        foreach (var p in products.Where(p => p.Active && (p.Weighed || p.WeightCodes.Count > 0)))
        {
            if (p.Plu == null) { skipped++; continue; }
            included.Add(p);
        }
        // PLU benzersizdir: aynı PLU iki üründe varsa ilki kalır.
        var seen = new HashSet<int>();
        included = included.OrderBy(p => p.Plu).Where(p => seen.Add(p.Plu.Value)).ToList();
        return (included, skipped);
    }

    public static string FormatPrice(decimal price, string format)
    {
        switch (format)
        {
            case "kurus": return ((long)Math.Round(price * 100m, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            case "decimal-dot": return price.ToString("0.00", CultureInfo.InvariantCulture);
            default: return price.ToString("0.00", new CultureInfo("tr-TR"));
        }
    }

    static string Clean(string text, ScaleDevice scale)
    {
        var t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Replace(scale.Separator, " ").Replace("\"", "'");
        if (scale.FoldTurkish || scale.Encoding == "ascii") t = TextTools.FoldToAscii(t);
        t = t.Trim();
        return scale.NameMaxLength > 0 && t.Length > scale.NameMaxLength ? t.Substring(0, scale.NameMaxLength).TrimEnd() : t;
    }

    static string Cell(string key, Product p, ScaleDevice scale)
    {
        switch (key)
        {
            case "plu": return p.Plu?.ToString(CultureInfo.InvariantCulture) ?? "";
            case "plu5": return (p.Plu ?? 0).ToString("00000", CultureInfo.InvariantCulture);
            case "code": return p.Code;
            case "name": return Clean(p.Name, scale);
            case "price": return FormatPrice(p.GrossPrice, scale.PriceFormat);
            case "vat": return p.VatRate.ToString("0.##", CultureInfo.InvariantCulture);
            case "unit": return p.UnitCode == "GRM" || p.UnitCode == "KGM" || p.Weighed ? "KG" : "AD";
            case "unitno": return p.Weighed ? "0" : "1";
            case "barcode": return p.Barcodes.FirstOrDefault() ?? "";
            case "barcodekind": return scale.BarcodeKind == "price" ? "1" : "0";
            case "category": return Clean(p.CategoryName, scale);
            default: return "";
        }
    }

    /// <summary>Terazi tanımına göre CSV metni (satır sonu CRLF).</summary>
    public static string BuildCsv(ScaleDevice scale, IEnumerable<Product> products)
    {
        var keys = scale.ColumnKeys;
        if (keys.Count == 0) throw new InvalidOperationException("Terazi için en az bir sütun seçin.");
        var sep = scale.Separator == "\\t" ? "\t" : scale.Separator;
        var sb = new StringBuilder();
        if (scale.IncludeHeader)
        {
            var titles = keys.Select(ScaleColumns.Title).Select(t => scale.FoldTurkish || scale.Encoding == "ascii" ? TextTools.FoldToAscii(t) : t);
            sb.Append(string.Join(sep, titles)).Append("\r\n");
        }
        foreach (var p in products) sb.Append(string.Join(sep, keys.Select(k => Cell(k, p, scale)))).Append("\r\n");
        return sb.ToString();
    }

    static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((name ?? "terazi").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>Çıktı dosyasının tam yolu: klasör verilmişse "&lt;terazi adı&gt;.csv" eklenir.</summary>
    public static string ResolveFilePath(ScaleDevice scale)
    {
        var path = (scale.OutputPath ?? "").Trim();
        if (path.Length == 0) throw new InvalidOperationException("CSV için bir klasör veya dosya yolu girin.");
        return Path.HasExtension(path) ? path : Path.Combine(path, SafeFileName(scale.Name) + ".csv");
    }

    public static async Task<ScaleExportResult> ExportAsync(ScaleDevice scale, IEnumerable<Product> allProducts, CancellationToken ct = default)
    {
        var (included, skipped) = Select(allProducts);
        var csv = BuildCsv(scale, included);
        var bytes = GetEncoding(scale.Encoding).GetBytes(csv);
        var result = new ScaleExportResult { Count = included.Count, SkippedNoPlu = skipped };

        if (scale.Output == ScaleOutput.TcpText)
        {
            if (string.IsNullOrWhiteSpace(scale.Ip) || scale.Port <= 0) throw new InvalidOperationException("TCP gönderimi için IP adresi ve port girin.");
            using (var client = new TcpClient())
            {
                var connect = client.ConnectAsync(scale.Ip.Trim(), scale.Port);
                if (await Task.WhenAny(connect, Task.Delay(5000, ct)).ConfigureAwait(false) != connect) throw new IOException($"{scale.Ip}:{scale.Port} adresine 5 sn içinde bağlanılamadı.");
                await connect.ConfigureAwait(false); // bağlantı hatasını fırlat
                var stream = client.GetStream();
                await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            result.Destination = $"{scale.Ip}:{scale.Port}";
        }
        else
        {
            var file = ResolveFilePath(scale);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            // Yarım dosya görülmesin: önce geçici dosyaya yaz, sonra yerine taşı.
            var temp = file + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(file)) File.Delete(file);
            File.Move(temp, file);
            result.Destination = file;
        }

        result.Message = $"{result.Count} ürün gönderildi → {result.Destination}" + (skipped > 0 ? $" ({skipped} ürün PLU kodu olmadığı için atlandı)" : "");
        return result;
    }
}
