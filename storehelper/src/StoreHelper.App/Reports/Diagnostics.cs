using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using DevExpress.XtraPrinting;
using StoreHelper.Core;
using StoreHelper.Core.Labels;
using StoreHelper.Core.Scales;

namespace StoreHelper.App.Reports;

/// <summary>"StoreHelper.exe --render-sample [klasör]": varsayılan tasarımı tek etiket ve A4 modunda PNG'ye çizer (DevExpress lisansı/yazıcı sorunlarını sahada denemek için).</summary>
public static class Diagnostics
{
    public static string RenderSamples(string directory)
    {
        Directory.CreateDirectory(directory);
        var products = new List<Product>();
        for (var i = 1; i <= 8; i++)
            products.Add(new Product
            {
                Code = $"URUN{i:000}", Name = i % 3 == 0 ? "Çok uzun bir ürün adı örneği şeker pancarı 1 kg" : $"Örnek ürün {i}", GrossPrice = 9.5m * i + 0.9m,
                UnitName = i % 2 == 0 ? "Kg" : "Adet", Barcodes = { $"86900000000{i:00}" }
            });
        var lines = new List<PrintLine>();
        foreach (var p in products) lines.Add(new PrintLine { Product = p, Copies = p.Code.EndsWith("3") ? 4 : 2 });
        var items = LabelPlan.Expand(lines, 2, DateTime.Now); // ilk 2 hücre boş

        var rulo = new LabelTemplate { Name = "Rulo" };
        LabelPreset.All[0].ApplyTo(rulo);
        var single = LabelReportFactory.Build(rulo, PrintMode.Single, items.GetRange(2, 3));
        single.CreateDocument();
        var singlePages = single.Pages.Count;
        single.ExportToImage(Path.Combine(directory, "single.png"), new ImageExportOptions { Format = ImageFormat.Png, ExportMode = ImageExportMode.DifferentFiles, PageRange = "1", Resolution = 150 });

        var a4 = new LabelTemplate { Name = "A4" };
        LabelPreset.All[4].ApplyTo(a4); // 3 x 7
        var sheet = LabelReportFactory.Build(a4, PrintMode.Sheet, items);
        sheet.CreateDocument();
        var sheetPages = sheet.Pages.Count;
        sheet.ExportToImage(Path.Combine(directory, "sheet.png"), new ImageExportOptions { Format = ImageFormat.Png, ExportMode = ImageExportMode.DifferentFiles, PageRange = "1", Resolution = 100 });

        var result = $"tek etiket: {singlePages} sayfa (beklenen 3); A4: {sheetPages} sayfa (beklenen 1); {items.Count} etiket → {directory}";
        File.WriteAllText(Path.Combine(directory, "result.txt"), result);
        return result;
    }
}

/// <summary>"StoreHelper.exe --ui-snapshot &lt;klasör&gt; &lt;kernel-url&gt; &lt;kullanıcı&gt; &lt;şifre&gt;": pencereleri ekran dışında açıp DrawToBitmap ile PNG'ye alır (masaüstüne/girdiye dokunmaz).</summary>
public static class UiSnapshot
{
    public static string Run(string directory, string url, string user, string password)
    {
        Directory.CreateDirectory(directory);
        using var store = new StoreHelper.Core.Data.Store(Path.Combine(directory, "snapshot.db"));
        using var session = new AppSession(store);

        Capture(new LoginForm(session), Path.Combine(directory, "ui-login.png"));

        session.LoginAsync(url, user, password).GetAwaiter().GetResult();
        session.SyncProductsAsync(null).GetAwaiter().GetResult();

        var template = new LabelTemplate { Name = "Raf etiketi 60x40" };
        LabelPreset.All[0].ApplyTo(template);
        template.Repx = LabelReportFactory.ToRepx(LabelReportFactory.CreateDefault(template));
        store.SaveTemplate(template);
        var a4 = new LabelTemplate { Name = "A4 3x7 etiket" };
        LabelPreset.All[4].ApplyTo(a4);
        a4.Repx = LabelReportFactory.ToRepx(LabelReportFactory.CreateDefault(a4));
        store.SaveTemplate(a4);
        store.SaveScale(new ScaleDevice { Name = "Manav terazisi", Output = ScaleOutput.CsvFile, OutputPath = Path.Combine(directory, "terazi"), Columns = "plu,name,price,unitno" });
        store.SaveScale(new ScaleDevice { Name = "Kasap terazisi", Output = ScaleOutput.TcpText, Ip = "192.168.1.51", Port = 5001, Columns = "plu,name,price" });

        var main = new MainForm(session);
        main.FillSampleList(5);
        var names = new[] { "labels", "templates", "scales", "settings" };
        Capture(main, null, () =>
        {
            for (var i = 0; i < main.TabCount; i++)
            {
                main.SelectTab(i);
                System.Windows.Forms.Application.DoEvents();
                SavePng(main, Path.Combine(directory, $"ui-{names[i]}.png"));
            }
        });
        return $"ekran görüntüleri: {directory}";
    }

    static void Capture(System.Windows.Forms.Form form, string path, Action then = null)
    {
        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        form.Location = new System.Drawing.Point(-20000, -20000);
        form.ShowInTaskbar = false;
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        System.Threading.Thread.Sleep(300);
        System.Windows.Forms.Application.DoEvents();
        if (path != null) SavePng(form, path);
        then?.Invoke();
        form.Close();
    }

    static void SavePng(System.Windows.Forms.Form form, string path)
    {
        using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        bmp.Save(path, ImageFormat.Png);
    }
}
