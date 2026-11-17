using System;
using System.Collections.Generic;

namespace StoreHelper.Core.Labels;

public enum PrintMode
{
    /// <summary>Etiket yazıcısı / rulo: her etiket ayrı bir sayfa (sayfa = etiket boyutu).</summary>
    Single,
    /// <summary>A4 (veya tabaka) yazıcısı: etiketler satır/sütun ızgarasında.</summary>
    Sheet
}

/// <summary>Tabaka yerleşimi hesapları (milimetre). Arayüzden bağımsız, birim testlidir.</summary>
public static class SheetLayout
{
    /// <summary>Yerleşim geçerliyse null, değilse kullanıcıya gösterilecek Türkçe hata.</summary>
    public static string Validate(LabelTemplate t, PrintMode mode)
    {
        if (t.LabelWidthMm < 10 || t.LabelHeightMm < 5) return "Etiket ölçüsü çok küçük (en az 10 × 5 mm).";
        if (mode == PrintMode.Single) return null;
        if (t.Columns < 1) return "Sütun sayısı en az 1 olmalı.";
        if (t.SheetWidthMm < t.LabelWidthMm || t.SheetHeightMm < t.LabelHeightMm) return "Tabaka etiketten küçük olamaz.";
        if (RequiredWidth(t) > t.SheetWidthMm + 0.01m)
            return $"Sütunlar tabakaya sığmıyor: {t.Columns} × {t.LabelWidthMm} mm + boşluklar + sol kenar = {RequiredWidth(t):0.#} mm, tabaka {t.SheetWidthMm:0.#} mm.";
        if (Rows(t) < 1) return "Etiket tabakaya dikey olarak sığmıyor.";
        return null;
    }

    public static decimal RequiredWidth(LabelTemplate t) => t.MarginLeftMm + t.Columns * t.LabelWidthMm + (t.Columns - 1) * t.GapXMm;

    /// <summary>Tabaka üzerinde aşağı doğru kaç satır etiket sığar.</summary>
    public static int Rows(LabelTemplate t)
    {
        var usable = t.SheetHeightMm - t.MarginTopMm;
        if (usable < t.LabelHeightMm) return 0;
        return (int)Math.Floor((double)((usable + t.GapYMm) / (t.LabelHeightMm + t.GapYMm)));
    }

    public static int LabelsPerSheet(LabelTemplate t) => Rows(t) * Math.Max(1, t.Columns);

    /// <summary>Sağ kenar boşluğu: sütun genişliğinin tam etiket genişliği olması için (DevExpress sütun genişliğini sayfadan hesaplar).</summary>
    public static decimal RightMargin(LabelTemplate t) => Math.Max(0, t.SheetWidthMm - RequiredWidth(t));

    /// <summary>Sayfa sayısı (önizleme bilgisi).</summary>
    public static int Sheets(LabelTemplate t, int labelCount)
    {
        var per = LabelsPerSheet(t);
        return per <= 0 ? 0 : (labelCount + per - 1) / per;
    }
}

/// <summary>Basım listesi: ürün + adet; ilk N hücreyi boş bırakma (yarım kullanılmış tabaka).</summary>
public sealed class PrintLine
{
    public Product Product { get; set; }
    public int Copies { get; set; } = 1;
}

public static class LabelPlan
{
    public static List<LabelItem> Expand(IEnumerable<PrintLine> lines, int skipCells, DateTime now)
    {
        var list = new List<LabelItem>();
        for (var i = 0; i < Math.Max(0, skipCells); i++) list.Add(LabelItem.Blank());
        foreach (var line in lines)
        {
            var item = LabelItem.From(line.Product, now);
            for (var i = 0; i < Math.Max(1, line.Copies); i++) list.Add(item);
        }
        return list;
    }
}
