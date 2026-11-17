using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StoreHelper.Core;
using StoreHelper.Core.Data;
using StoreHelper.Core.Kernel;
using StoreHelper.Core.Labels;
using StoreHelper.Core.Scales;
using Xunit;

namespace StoreHelper.Tests;

public class CoreTests
{
    static Product Tomato() => new Product
    {
        Code = "DOM001", Name = "Domates Çeşme İnci", UnitCode = "KGM", UnitName = "Kg", Weighed = true, VatRate = 1, NetPrice = 49.4m, GrossPrice = 49.9m,
        CategoryName = "Sebze", WeightCodes = { "00123" }, Barcodes = { "8690000000011" }
    };

    static Product Water() => new Product { Code = "SU05", Name = "Su 0,5L", UnitCode = "NIU", UnitName = "Adet", VatRate = 20, NetPrice = 5, GrossPrice = 6, Barcodes = { "8690000000028" } };

    // ── Store ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Store_urunler_aranir_barkodla_bulunur_ve_degistirilir()
    {
        using var store = new Store(":memory:");
        store.ReplaceProducts(new[] { Tomato(), Water() });
        Assert.Equal(2, store.CountProducts());
        Assert.Equal("DOM001", store.FindByCode("8690000000011").Code);
        Assert.Equal("DOM001", store.FindByCode("00123").Code);
        Assert.Equal("SU05", store.FindByCode("su05").Code);
        Assert.Null(store.FindByCode("yok"));
        Assert.Null(store.FindByCode("869000000001"));

        var back = store.GetProducts().Single(p => p.Code == "DOM001");
        Assert.Equal(49.9m, back.GrossPrice);
        Assert.Equal(123, back.Plu);
        Assert.Equal(new[] { "00123" }, back.WeightCodes);

        store.ReplaceProducts(new[] { Water() });
        Assert.Equal(1, store.CountProducts());
        Assert.NotNull(store.ProductsSyncedAt());
    }

    [Fact]
    public void Store_degisim_zamanlari_ve_son_basim_saklanir_degisenler_suzulur()
    {
        using var store = new Store(":memory:");
        var since = new DateTime(2026, 10, 17, 0, 0, 0);
        var tomato = Tomato();
        tomato.CreatedAt = since.AddDays(-30);
        tomato.PriceChangedAt = since.AddHours(9);
        var water = Water();
        water.CreatedAt = since.AddMinutes(-1);
        var bread = new Product { Code = "EKM", Name = "Ekmek", CreatedAt = since.AddHours(8) };
        store.ReplaceProducts(new[] { tomato, water, bread, new Product { Code = "ESKI", Name = "Eski ürün" } });

        var back = store.GetProducts();
        Assert.Equal(tomato.PriceChangedAt, back.Single(p => p.Code == "DOM001").PriceChangedAt);
        Assert.Null(back.Single(p => p.Code == "SU05").PriceChangedAt);
        Assert.Equal(new[] { "DOM001", "EKM" }, back.Where(p => p.ChangedSince(since)).Select(p => p.Code).OrderBy(c => c));

        Assert.Null(store.LastLabelPrintAt);
        store.LastLabelPrintAt = since.AddHours(14).AddMinutes(30);
        Assert.Equal(since.AddHours(14).AddMinutes(30), store.LastLabelPrintAt);
        store.LastLabelPrintAt = null;
        Assert.Null(store.LastLabelPrintAt);
    }

    [Fact]
    public void Kernel_urun_zamanlari_yerel_saate_cevrilir()
    {
        var p = KernelClient.ParseProduct(Newtonsoft.Json.Linq.JObject.Parse(@"{""code"":""A"",""createdAt"":null,""priceChangedAt"":""2026-10-17T06:30:00.000Z""}"));
        Assert.Null(p.CreatedAt);
        Assert.Equal(new DateTime(2026, 10, 17, 6, 30, 0, DateTimeKind.Utc).ToLocalTime(), p.PriceChangedAt);
    }

    [Fact]
    public void Store_sablonlar_terazi_ve_ayarlar_kaydedilir()
    {
        using var store = new Store(":memory:");
        store.SetSetting("kernel.url", "http://x:4750");
        Assert.Equal("http://x:4750", store.GetSetting("kernel.url"));
        Assert.Equal("", store.GetSetting("yok"));

        var t = new LabelTemplate { Name = "Raf 60x40", Repx = "<xml/>", Columns = 3 };
        var id = store.SaveTemplate(t);
        t.Name = "Raf 60x40 v2";
        store.SaveTemplate(t);
        var loaded = store.GetTemplates().Single();
        Assert.Equal(id, loaded.Id);
        Assert.Equal("Raf 60x40 v2", loaded.Name);

        var json = Store.ExportTemplate(loaded);
        var imported = Store.ImportTemplate(json);
        Assert.Equal(0, imported.Id);
        Assert.Equal("<xml/>", imported.Repx);
        Assert.Equal(3, imported.Columns);
        Assert.Throws<InvalidDataException>(() => Store.ImportTemplate("{\"format\":\"baska\"}"));

        var scale = new ScaleDevice { Name = "Manav terazisi", Ip = "192.168.1.50", Port = 5001, OutputPath = @"C:\terazi" };
        store.SaveScale(scale);
        store.RecordScaleExport(scale.Id, "tamam");
        var s = store.GetScales().Single();
        Assert.Equal("Manav terazisi", s.Name);
        Assert.Equal("tamam", s.LastExportResult);
        Assert.NotNull(s.LastExportAt);

        store.LogPrint("DOM001", 40m, 2);
        store.LogPrint("DOM001", 49.9m, 1);
        Assert.Equal(49.9m, store.LastPrintedPrices()["DOM001"]);
    }

    // ── Kernel istemcisi ───────────────────────────────────────────────────────────────
    sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Requests = new List<string>();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add($"{request.Method} {request.RequestUri.PathAndQuery} auth={request.Headers.Authorization?.Parameter}");
            HttpResponseMessage Json(HttpStatusCode code, string body) => new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/helper/login")
            {
                var body = request.Content.ReadAsStringAsync().Result;
                return Task.FromResult(body.Contains("dogru")
                    ? Json(HttpStatusCode.OK, "{\"token\":\"T0K\",\"user\":{\"username\":\"ali\",\"fullName\":\"Ali\",\"role\":\"manager\"},\"company\":\"Market\"}")
                    : Json(HttpStatusCode.Unauthorized, "{\"error\":\"INVALID_CREDENTIALS\",\"message\":\"Kullanıcı adı veya şifre hatalı.\"}"));
            }
            if (path == "/api/helper/items")
            {
                var page = int.Parse(request.RequestUri.Query.Split('&')[0].Split('=')[1]);
                var items = page == 0
                    ? "{\"code\":\"A\",\"name\":\"Alfa\",\"grossPrice\":12.5,\"weighed\":true,\"weightCodes\":[\"00007\"],\"barcodes\":[]},{\"code\":\"B\",\"name\":\"Beta\",\"grossPrice\":3,\"barcodes\":[\"123\"]}"
                    : "{\"code\":\"C\",\"name\":\"Ceta\",\"grossPrice\":1,\"barcodes\":[]}";
                return Task.FromResult(request.Headers.Authorization?.Parameter == "T0K"
                    ? Json(HttpStatusCode.OK, $"{{\"items\":[{items}],\"total\":3,\"page\":{page},\"pageSize\":2}}")
                    : Json(HttpStatusCode.Unauthorized, "{\"message\":\"Oturum geçersiz\"}"));
            }
            return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
        }
    }

    [Theory]
    [InlineData("192.168.1.10", "http://192.168.1.10:4750")]
    [InlineData("http://localhost", "http://localhost:4750")]
    [InlineData("http://127.0.0.1/admin", "http://127.0.0.1:4750")]
    [InlineData("http://127.0.0.1:4803", "http://127.0.0.1:4803")]
    [InlineData("kasa-sunucu:4900/admin", "http://kasa-sunucu:4900")]
    [InlineData("http://x:80", "http://x:80")]
    [InlineData("https://pos.firma.com", "https://pos.firma.com")]
    public void Kernel_adresinde_port_yoksa_varsayilan_4750_eklenir(string input, string expected)
    {
        Assert.Equal(expected, KernelClient.NormalizeUrl(input));
        Assert.Equal("http://127.0.0.1:4750", KernelClient.DefaultUrl);
    }

    [Fact]
    public async Task Kernel_giris_ve_sayfali_urun_cekme()
    {
        var handler = new FakeHandler();
        using var client = new KernelClient("192.168.1.10:4750/admin", handler: handler);
        Assert.Equal("http://192.168.1.10:4750", client.BaseUrl);

        var bad = await Assert.ThrowsAsync<KernelAuthException>(() => client.LoginAsync("ali", "yanlis"));
        Assert.Contains("hatalı", bad.Message);

        var session = await client.LoginAsync("ali", "dogru");
        Assert.Equal("Market", session.Company);
        Assert.Equal("T0K", client.Token);

        var progress = new List<int>();
        var products = await client.GetAllProductsAsync(new Progress<(int done, int total)>(p => progress.Add(p.done)));
        Assert.Equal(new[] { "A", "B", "C" }, products.Select(p => p.Code));
        Assert.Equal(12.5m, products[0].GrossPrice);
        Assert.Equal(7, products[0].Plu);
        Assert.Equal(2, handler.Requests.Count(r => r.Contains("/api/helper/items")));

        client.Token = "eski";
        await Assert.ThrowsAsync<KernelAuthException>(() => client.GetAllProductsAsync());
    }

    [Fact]
    public async Task Kernel_ulasilamayinca_anlasilir_hata()
    {
        using var client = new KernelClient("http://127.0.0.1:1");
        var ex = await Assert.ThrowsAsync<KernelException>(() => client.LoginAsync("a", "b"));
        Assert.Contains("bağlanılamadı", ex.Message);
    }

    // ── Tabaka yerleşimi ───────────────────────────────────────────────────────────────
    [Fact]
    public void Tabaka_yerlesimi_satir_sutun_ve_sigmama()
    {
        var t = new LabelTemplate { LabelWidthMm = 63.5m, LabelHeightMm = 38.1m, Columns = 3, MarginLeftMm = 7.2m, MarginTopMm = 15.1m, GapXMm = 2.5m, GapYMm = 0 };
        Assert.Null(SheetLayout.Validate(t, PrintMode.Sheet)); // Avery L7160 benzeri 3x7
        Assert.Equal(7, SheetLayout.Rows(t));
        Assert.Equal(21, SheetLayout.LabelsPerSheet(t));
        Assert.Equal(2, SheetLayout.Sheets(t, 22));
        Assert.True(SheetLayout.RightMargin(t) >= 0);

        t.Columns = 4;
        Assert.Contains("sığmıyor", SheetLayout.Validate(t, PrintMode.Sheet));
        Assert.Null(SheetLayout.Validate(t, PrintMode.Single));

        t.LabelWidthMm = 5;
        Assert.Contains("küçük", SheetLayout.Validate(t, PrintMode.Single));
    }

    [Fact]
    public void Basim_plani_adet_ve_bos_hucre_atlama()
    {
        var lines = new[] { new PrintLine { Product = Tomato(), Copies = 2 }, new PrintLine { Product = Water(), Copies = 1 } };
        var items = LabelPlan.Expand(lines, 3, new DateTime(2026, 10, 13));
        Assert.Equal(6, items.Count);
        Assert.True(items.Take(3).All(i => i.IsBlank));
        Assert.Equal("49,90", items[3].PriceText);
        Assert.Equal("49", items[3].PriceInteger);
        Assert.Equal("90", items[3].PriceDecimal);
        Assert.Equal("13.10.2026", items[3].Date);
        Assert.Equal("8690000000011", items[3].Barcode);
        Assert.Equal("Kg", items[3].Unit);
        Assert.Equal("SU05", items[5].Code);
    }

    // ── Terazi dışa aktarımı ───────────────────────────────────────────────────────────
    [Fact]
    public void Terazi_secimi_ve_csv_icerigi()
    {
        var noPlu = new Product { Code = "KODSUZ", Name = "Kodsuz", Weighed = true, GrossPrice = 5 };
        var passive = Tomato(); passive.Code = "PASIF"; passive.Active = false; passive.WeightCodes = new List<string> { "00200" };
        var dup = Tomato(); dup.Code = "KOPYA";
        var (included, skipped) = ScaleExport.Select(new[] { Water(), noPlu, passive, Tomato(), dup });
        Assert.Equal(1, skipped);
        Assert.Equal(new[] { "DOM001" }, included.Select(p => p.Code)); // adet ürünü, pasif ve yinelenen PLU dışarıda

        var scale = new ScaleDevice { Name = "T1", Columns = "plu,name,price,unitno", Separator = ";", PriceFormat = "decimal-comma", NameMaxLength = 10, FoldTurkish = true };
        var csv = ScaleExport.BuildCsv(scale, included);
        Assert.Equal("PLU;Urun adi;Fiyat;Birim no\r\n123;Domates Ce;49,90;0\r\n", csv);

        scale.IncludeHeader = false; scale.PriceFormat = "kurus"; scale.Columns = "plu5,price,unit,barcodekind"; scale.BarcodeKind = "price";
        Assert.Equal("00123;4990;KG;1\r\n", ScaleExport.BuildCsv(scale, included));

        scale.Columns = "name"; scale.Separator = ";"; scale.NameMaxLength = 0;
        var odd = new Product { Code = "X", Name = "A;B\"C\nD", WeightCodes = { "00001" }, Weighed = true };
        Assert.Equal("A B'C D\r\n", ScaleExport.BuildCsv(scale, new[] { odd }));
    }

    [Fact]
    public async Task Terazi_csv_dosyasina_yazar_ve_tcp_ye_gonderir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "storehelper-" + Guid.NewGuid().ToString("N"));
        try
        {
            var scale = new ScaleDevice { Name = "Manav/Terazi", Output = ScaleOutput.CsvFile, OutputPath = dir, Encoding = "windows-1254", Columns = "plu,name" };
            var result = await ScaleExport.ExportAsync(scale, new[] { Tomato() });
            Assert.Equal(1, result.Count);
            var file = Path.Combine(dir, "Manav_Terazi.csv");
            Assert.True(File.Exists(file));
            Assert.False(File.Exists(file + ".tmp"));
            Assert.Equal("PLU;Ürün adı\r\n123;Domates Çeşme İnci\r\n", ScaleExport.GetEncoding("windows-1254").GetString(File.ReadAllBytes(file)));

            await ScaleExport.ExportAsync(scale, new[] { Tomato() }); // üzerine yazar
            Assert.Single(Directory.GetFiles(dir));

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var tcp = new ScaleDevice { Name = "TCP", Output = ScaleOutput.TcpText, Ip = "127.0.0.1", Port = port, Encoding = "utf-8", Columns = "plu,price", IncludeHeader = false };
                var accept = listener.AcceptTcpClientAsync();
                var send = ScaleExport.ExportAsync(tcp, new[] { Tomato() });
                using var peer = await accept;
                var buffer = new byte[256];
                var read = await peer.GetStream().ReadAsync(buffer, 0, buffer.Length);
                await send;
                Assert.Equal("123;49,90\r\n", Encoding.UTF8.GetString(buffer, 0, read));
            }
            finally { listener.Stop(); }

            var closed = new ScaleDevice { Name = "Kapalı", Output = ScaleOutput.TcpText, Ip = "127.0.0.1", Port = 1, Columns = "plu" };
            await Assert.ThrowsAnyAsync<Exception>(() => ScaleExport.ExportAsync(closed, new[] { Tomato() }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => ScaleExport.ExportAsync(new ScaleDevice { Name = "x", Output = ScaleOutput.TcpText }, new[] { Tomato() }));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Turkce_metin_araclari()
    {
        Assert.Equal("Cay Gunes Isik Ogun", TextTools.FoldToAscii("Çay Güneş Işık Öğün").Replace("ı", "i"));
        Assert.Equal(TextTools.SearchKey("ÇAY"), TextTools.SearchKey("cay"));
        Assert.Equal(TextTools.SearchKey("IŞIK"), TextTools.SearchKey("işik"));
    }
}
