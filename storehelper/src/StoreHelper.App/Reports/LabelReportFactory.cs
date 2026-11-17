using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using DevExpress.Drawing.Printing;
using DevExpress.XtraPrinting;
using DevExpress.XtraPrinting.BarCode;
using DevExpress.XtraReports.UI;
using StoreHelper.Core;
using StoreHelper.Core.Labels;

namespace StoreHelper.App.Reports;

/// <summary>
/// Etiket raporlarını üretir. Bir "etiket tasarımı" tek bir etiketi (Detail bandı) anlatan DevExpress raporudur; birimi 0,1 mm'dir.
///  • Tek etiket modu: sayfa = etiket boyutu (etiket yazıcısı/rulo).
///  • Tabaka modu: aynı Detail bandı çok sütunlu yerleşimle A4 (veya tabaka) üzerine dizilir.
/// </summary>
public static class LabelReportFactory
{
    /// <summary>Yeni tasarımların birimi. DevExpress çok sütunlu bantlarda 0,1 mm biriminde satır adımını ~%10 büyük hesapladığı için (denemeyle görüldü) inç yüzlüğü kullanılır.</summary>
    public const ReportUnit DesignUnit = ReportUnit.HundredthsOfAnInch;

    static decimal PerMm(ReportUnit unit)
    {
        switch (unit)
        {
            case ReportUnit.TenthsOfAMillimeter: return 10m;
            case ReportUnit.HundredthsOfAnInch: return 100m / 25.4m;
            default: return 96m / 25.4m;
        }
    }

    /// <summary>mm → verilen rapor birimi</summary>
    static int U(ReportUnit unit, decimal mm) => (int)Math.Round(mm * PerMm(unit));

    public static XtraReport CreateDefault(LabelTemplate t)
    {
        var report = new XtraReport { ReportUnit = DesignUnit, PaperKind = DXPaperKind.Custom };
        var unit = report.ReportUnit;
        report.PageWidth = U(unit, t.LabelWidthMm);
        report.PageHeight = U(unit, t.LabelHeightMm);
        report.Margins = new Margins(0, 0, 0, 0);

        var w = U(unit, t.LabelWidthMm);
        var h = U(unit, t.LabelHeightMm);
        var pad = Math.Max(2, w / 30);

        // ÖNEMLİ: kontroller önce rapora/banda EKLENİR, sonra boyutlandırılır; aksi halde boyutlar varsayılan birimle (1/100 inç) yorumlanır.
        var detail = new DetailBand { Name = "Detail" };
        report.Bands.Add(detail);
        detail.HeightF = h;
        detail.Padding = new PaddingInfo(0, 0, 0, 0);

        // Ürün adı (üst, 2 satıra kadar)
        var name = NewLabel(detail, "lblName", pad, (int)(h * 0.05), w - 2 * pad, (int)(h * 0.30), "[Name]", 11f, FontStyle.Bold);
        name.TextAlignment = TextAlignment.TopLeft;
        name.WordWrap = true;
        name.CanGrow = false;

        // Fiyat (orta, büyük)
        var price = NewLabel(detail, "lblPrice", pad, (int)(h * 0.36), (int)(w * 0.68), (int)(h * 0.32), "Iif([IsBlank], '', '₺' + [PriceText])", Math.Min(40f, Math.Max(12f, (float)t.LabelHeightMm * 0.42f)), FontStyle.Bold);
        price.TextAlignment = TextAlignment.MiddleLeft;

        // Birim (fiyatın sağında, küçük)
        var unitLabel = NewLabel(detail, "lblUnit", pad + (int)(w * 0.68), (int)(h * 0.36), w - (int)(w * 0.68) - 2 * pad, (int)(h * 0.32), "Iif([IsBlank], '', '/ ' + [Unit])", 10f, FontStyle.Regular);
        unitLabel.TextAlignment = TextAlignment.MiddleLeft;

        // Barkod (alt)
        var barcode = new XRBarCode { Name = "barcode" };
        detail.Controls.Add(barcode);
        barcode.LocationF = new PointF(pad, (int)(h * 0.70));
        barcode.SizeF = new SizeF(w - 2 * pad, (int)(h * 0.27));
        barcode.Symbology = new Code128Generator();
        barcode.AutoModule = true;
        barcode.ShowText = true;
        barcode.Font = new Font("Segoe UI", 8f);
        barcode.Padding = new PaddingInfo(0, 0, 0, 0);
        barcode.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Text", "[Barcode]"));
        return report;
    }

    static XRLabel NewLabel(DetailBand band, string name, int x, int y, int w, int h, string expression, float size, FontStyle style)
    {
        var label = new XRLabel { Name = name };
        band.Controls.Add(label);
        label.LocationF = new PointF(x, y);
        label.SizeF = new SizeF(w, h);
        label.Font = new Font("Segoe UI", size, style);
        label.Padding = new PaddingInfo(0, 0, 0, 0);
        label.CanGrow = false; // etiket yüksekliği sabit: metin büyüyüp ızgarayı kaydırmasın
        label.CanShrink = false;
        label.BorderWidth = 0;
        label.Borders = BorderSide.None;
        label.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Text", expression));
        return label;
    }

    /// <summary>Kayıtlı REPX'ten (yoksa varsayılan tasarımdan) raporu üretir ve tasarımcı için örnek veri kaynağı bağlar.</summary>
    public static XtraReport LoadOrCreate(LabelTemplate t)
    {
        XtraReport report;
        if (string.IsNullOrWhiteSpace(t.Repx)) report = CreateDefault(t);
        else
        {
            report = new XtraReport();
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(t.Repx))) report.LoadLayoutFromXml(ms);
        }
        report.DataSource = new List<LabelItem> { LabelItem.Sample() };
        return report;
    }

    public static string ToRepx(XtraReport report)
    {
        using var ms = new MemoryStream();
        report.SaveLayoutToXml(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Raporun sayfa boyutunu (etiket boyutu) milimetre olarak verir.</summary>
    public static (decimal width, decimal height) LabelSizeMm(XtraReport report)
    {
        decimal toMm(float v)
        {
            switch (report.ReportUnit)
            {
                case ReportUnit.TenthsOfAMillimeter: return (decimal)v / 10m;
                case ReportUnit.HundredthsOfAnInch: return (decimal)v * 0.254m;
                default: return (decimal)v * 25.4m / 96m;
            }
        }
        return (Math.Round(toMm(report.PageWidth), 1), Math.Round(toMm(report.PageHeight), 1));
    }

    /// <summary>Yazdırmaya hazır rapor: veri bağlı, sayfa/sütun yerleşimi moda göre ayarlı. Doğrulama çağıran tarafta (SheetLayout.Validate) yapılır.</summary>
    public static XtraReport Build(LabelTemplate t, PrintMode mode, IList<LabelItem> items)
    {
        var report = LoadOrCreate(t);
        report.DataSource = items;
        report.DataMember = null;

        var detail = report.Bands.OfType<DetailBand>().FirstOrDefault() ?? throw new InvalidOperationException("Tasarımda Detail bandı yok.");

        var unit = report.ReportUnit;
        var labelH = U(unit, t.LabelHeightMm);
        report.PaperKind = DXPaperKind.Custom;
        if (mode == PrintMode.Single)
        {
            report.PageWidth = U(unit, t.LabelWidthMm);
            report.PageHeight = labelH;
            report.Margins = new Margins(0, 0, 0, 0);
            detail.HeightF = labelH;
            detail.MultiColumn.Mode = MultiColumnMode.None;
        }
        else
        {
            report.PageWidth = U(unit, t.SheetWidthMm);
            report.PageHeight = U(unit, t.SheetHeightMm);
            report.Margins = new Margins(U(unit, t.MarginLeftMm), U(unit, SheetLayout.RightMargin(t)), U(unit, t.MarginTopMm), 0);
            detail.HeightF = labelH + U(unit, t.GapYMm);
            detail.MultiColumn.Mode = MultiColumnMode.UseColumnCount;
            detail.MultiColumn.ColumnCount = t.Columns;
            detail.MultiColumn.ColumnSpacing = U(unit, t.GapXMm);
            detail.MultiColumn.Layout = ColumnLayout.AcrossThenDown;
        }

        HideControlsOfBlankItems(detail);
        return report;
    }

    /// <summary>Yarım kullanılmış tabakada atlanan hücreler (IsBlank) boş kalsın: tasarımdaki tüm denetimler o hücrede gizlenir; bant yerinde kalır.</summary>
    static void HideControlsOfBlankItems(DetailBand detail)
    {
        // Tüm denetimler gizli olan bant DevExpress'te yok sayılır ve ızgara kayar: atlanan hücre yerinde kalsın diye boş, büyümeyen bir tutucu eklenir.
        var keeper = new XRLabel { Name = "__keeper" };
        detail.Controls.Add(keeper);
        keeper.LocationF = new PointF(0, 0);
        keeper.SizeF = new SizeF(1, 1);
        keeper.Font = new Font("Segoe UI", 1f);
        keeper.CanGrow = false;
        keeper.CanShrink = false;
        keeper.Padding = new PaddingInfo(0, 0, 0, 0);
        keeper.Borders = BorderSide.None;

        // Denetimlerin Visible'ı bir olay (BeforePrint) ile yazılırsa DevExpress satır adımını ~%10 büyütüyor; ifade bağlaması bunu yapmıyor.
        foreach (var c in Flatten(detail.Controls).Where(c => c != keeper && c.Visible && !c.ExpressionBindings.Cast<ExpressionBinding>().Any(x => x.PropertyName == "Visible")))
            c.ExpressionBindings.Add(new ExpressionBinding("BeforePrint", "Visible", "Not [IsBlank]"));
    }

    static IEnumerable<XRControl> Flatten(XRControlCollection collection)
    {
        foreach (XRControl c in collection)
        {
            yield return c;
            foreach (var child in Flatten(c.Controls)) yield return child;
        }
    }
}
