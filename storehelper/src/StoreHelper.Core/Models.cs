using System;
using System.Collections.Generic;
using System.Linq;

namespace StoreHelper.Core;

/// <summary>Kernel'den gelen ürün (KDV dahil fiyatla). Yerel SQLite'ta önbelleğe alınır.</summary>
public sealed class Product
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string UnitCode { get; set; } = "NIU";
    public string UnitName { get; set; } = "";
    public bool Weighed { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetPrice { get; set; }
    public decimal GrossPrice { get; set; }
    public string CategoryCode { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public List<string> Barcodes { get; set; } = new List<string>();
    /// <summary>Terazi PLU / tartı kısa kodları (5 hane, ör. 00123).</summary>
    public List<string> WeightCodes { get; set; } = new List<string>();
    public bool Active { get; set; } = true;
    /// <summary>Kernel'in ürünü ilk gördüğü an (yerel saat); kernel yükseltmesinden önceki ürünlerde bilinmez (null).</summary>
    public DateTime? CreatedAt { get; set; }
    /// <summary>KDV dahil fiyatın son değiştiği an (yerel saat); bilinmiyorsa null.</summary>
    public DateTime? PriceChangedAt { get; set; }

    /// <summary>Verilen andan SONRA eklenmiş ya da fiyatı değişmiş mi (raf etiketi yeniden basılmalı).</summary>
    public bool ChangedSince(DateTime since) => CreatedAt > since || PriceChangedAt > since;

    /// <summary>Etikette basılacak barkod: ilk barkod, yoksa ürün kodu.</summary>
    public string PrimaryBarcode => Barcodes.FirstOrDefault() ?? Code;

    /// <summary>Teraziye gidecek PLU numarası (baştaki sıfırlar atılmış); kodu yoksa null.</summary>
    public int? Plu
    {
        get
        {
            var raw = WeightCodes.FirstOrDefault();
            return raw != null && int.TryParse(raw, out var n) && n > 0 ? n : (int?)null;
        }
    }

    public string BarcodesText => string.Join(", ", Barcodes);
}

/// <summary>Etiket rapor şablonunun veri kaynağı: tasarımcıda alan listesi olarak görünür; alan adları KARARLIDIR (kayıtlı tasarımlar buna bağlıdır).</summary>
public sealed class LabelItem
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Barcode { get; set; } = "";
    public decimal Price { get; set; }
    /// <summary>"1.234,50"</summary>
    public string PriceText { get; set; } = "";
    /// <summary>Fiyatın tam kısmı: "1.234"</summary>
    public string PriceInteger { get; set; } = "";
    /// <summary>Kuruş kısmı: "50"</summary>
    public string PriceDecimal { get; set; } = "";
    /// <summary>"Kg" / "Adet"</summary>
    public string Unit { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public decimal VatRate { get; set; }
    /// <summary>Basım tarihi "13.10.2026"</summary>
    public string Date { get; set; } = "";
    public bool IsBlank { get; set; }

    public static LabelItem From(Product p, DateTime now)
    {
        var tr = new System.Globalization.CultureInfo("tr-TR");
        var price = Math.Round(p.GrossPrice, 2);
        var whole = Math.Truncate(price);
        var cents = (int)Math.Round((price - whole) * 100);
        return new LabelItem
        {
            Code = p.Code,
            Name = p.Name,
            Barcode = p.PrimaryBarcode,
            Price = price,
            PriceText = price.ToString("N2", tr),
            PriceInteger = whole.ToString("N0", tr),
            PriceDecimal = cents.ToString("00"),
            Unit = string.IsNullOrWhiteSpace(p.UnitName) ? (p.Weighed ? "Kg" : "Adet") : p.UnitName,
            CategoryName = p.CategoryName,
            VatRate = p.VatRate,
            Date = now.ToString("dd.MM.yyyy", tr)
        };
    }

    /// <summary>Başlangıç hücresini atlamak için kullanılan boş etiket.</summary>
    public static LabelItem Blank() => new LabelItem { IsBlank = true };

    public static LabelItem Sample() => new LabelItem
    {
        Code = "DOM001", Name = "Domates (Salkım)", Barcode = "8690000000011", Price = 49.9m, PriceText = "49,90",
        PriceInteger = "49", PriceDecimal = "90", Unit = "Kg", CategoryName = "Sebze", VatRate = 1, Date = DateTime.Now.ToString("dd.MM.yyyy")
    };
}

/// <summary>Kaydedilmiş etiket tasarımı: DevExpress rapor şablonu (REPX) + kâğıt/yerleşim ayarları. Milimetre cinsindendir.</summary>
public sealed class LabelTemplate
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>DevExpress REPX (XML). Boşsa varsayılan tasarım üretilir.</summary>
    public string Repx { get; set; } = "";
    public decimal LabelWidthMm { get; set; } = 60;
    public decimal LabelHeightMm { get; set; } = 40;
    /// <summary>A4 / sayfa yazıcısı için tabaka ölçüleri.</summary>
    public decimal SheetWidthMm { get; set; } = 210;
    public decimal SheetHeightMm { get; set; } = 297;
    public int Columns { get; set; } = 3;
    public decimal MarginLeftMm { get; set; } = 7;
    public decimal MarginTopMm { get; set; } = 10;
    public decimal GapXMm { get; set; } = 2;
    public decimal GapYMm { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public LabelTemplate Clone() => (LabelTemplate)MemberwiseClone();
}

/// <summary>Terazi tanımı: nereye, hangi biçimde veri gideceği.</summary>
public sealed class ScaleDevice
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 0;
    /// <summary>csv-file | tcp-text</summary>
    public string Output { get; set; } = ScaleOutput.CsvFile;
    /// <summary>csv-file: klasör (veya .csv dosya yolu). Ağ yolu (\\sunucu\paylaşım) olabilir.</summary>
    public string OutputPath { get; set; } = "";
    public string Separator { get; set; } = ";";
    /// <summary>utf-8 | windows-1254 | ascii</summary>
    public string Encoding { get; set; } = "windows-1254";
    public bool FoldTurkish { get; set; }
    public bool IncludeHeader { get; set; } = true;
    /// <summary>Virgülle ayrılmış sütun anahtarları (bkz. ScaleColumns).</summary>
    public string Columns { get; set; } = "plu,name,price,unitno";
    /// <summary>decimal-comma | decimal-dot | kurus</summary>
    public string PriceFormat { get; set; } = "decimal-comma";
    public int NameMaxLength { get; set; } = 24;
    /// <summary>weight (ağırlıklı barkod) | price (tutarlı barkod)</summary>
    public string BarcodeKind { get; set; } = "weight";
    public bool Active { get; set; } = true;
    public DateTime? LastExportAt { get; set; }
    public string LastExportResult { get; set; } = "";

    public IReadOnlyList<string> ColumnKeys => Columns.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim().ToLowerInvariant()).ToList();
}

public static class ScaleOutput
{
    public const string CsvFile = "csv-file";
    public const string TcpText = "tcp-text";
}
