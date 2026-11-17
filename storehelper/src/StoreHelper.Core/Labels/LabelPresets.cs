using System;
using System.Collections.Generic;

namespace StoreHelper.Core.Labels;

/// <summary>Sık kullanılan etiket/tabaka ölçüleri (milimetre). Tabaka ön ayarları A4 (210 × 297) içindir.</summary>
public sealed class LabelPreset
{
    public string Name { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    /// <summary>0 = tek etiket (rulo/etiket yazıcısı); &gt;0 = A4 tabakada sütun sayısı.</summary>
    public int Columns { get; set; }
    public decimal GapX { get; set; }
    public decimal GapY { get; set; }
    public override string ToString() => Name;

    public static readonly IReadOnlyList<LabelPreset> All = new List<LabelPreset>
    {
        new LabelPreset { Name = "Rulo 60 × 40 mm", Width = 60, Height = 40, Columns = 0 },
        new LabelPreset { Name = "Rulo 50 × 30 mm", Width = 50, Height = 30, Columns = 0 },
        new LabelPreset { Name = "Rulo 40 × 25 mm", Width = 40, Height = 25, Columns = 0 },
        new LabelPreset { Name = "Rulo 100 × 50 mm", Width = 100, Height = 50, Columns = 0 },
        new LabelPreset { Name = "A4 · 3 × 7 = 21 etiket (63,5 × 38,1)", Width = 63.5m, Height = 38.1m, Columns = 3, GapX = 2.5m },
        new LabelPreset { Name = "A4 · 3 × 8 = 24 etiket (70 × 37)", Width = 70, Height = 37, Columns = 3 },
        new LabelPreset { Name = "A4 · 2 × 7 = 14 etiket (99,1 × 38,1)", Width = 99.1m, Height = 38.1m, Columns = 2, GapX = 2.5m },
        new LabelPreset { Name = "A4 · 2 × 5 = 10 etiket (99,1 × 57)", Width = 99.1m, Height = 57, Columns = 2, GapX = 2.5m },
        new LabelPreset { Name = "A4 · 4 × 11 = 44 etiket (48,5 × 25,4)", Width = 48.5m, Height = 25.4m, Columns = 4 },
        new LabelPreset { Name = "A4 · 2 × 4 = 8 etiket (105 × 74)", Width = 105, Height = 74, Columns = 2 }
    };

    /// <summary>Ön ayarı şablona uygular; tabaka ön ayarında kenar boşluklarını ızgarayı tabakada ortalayacak şekilde hesaplar.</summary>
    public void ApplyTo(LabelTemplate t)
    {
        t.LabelWidthMm = Width;
        t.LabelHeightMm = Height;
        t.GapXMm = GapX;
        t.GapYMm = GapY;
        if (Columns > 0)
        {
            t.SheetWidthMm = 210;
            t.SheetHeightMm = 297;
            t.Columns = Columns;
            CenterOnSheet(t);
        }
        else
        {
            t.Columns = 1;
        }
    }

    public static void CenterOnSheet(LabelTemplate t)
    {
        t.MarginLeftMm = Math.Max(0, Math.Round((t.SheetWidthMm - (t.Columns * t.LabelWidthMm + (t.Columns - 1) * t.GapXMm)) / 2m, 1));
        var rows = (int)Math.Floor((double)((t.SheetHeightMm + t.GapYMm) / (t.LabelHeightMm + t.GapYMm)));
        t.MarginTopMm = Math.Max(0, Math.Round((t.SheetHeightMm - (rows * t.LabelHeightMm + (rows - 1) * t.GapYMm)) / 2m, 1));
    }
}
