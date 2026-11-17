using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StoreHelper.Core.Data;
using StoreHelper.Core.Kernel;

namespace StoreHelper.App;

/// <summary>Uygulamanın tek oturum nesnesi: yerel veritabanı + kernel bağlantısı.</summary>
public sealed class AppSession : IDisposable
{
    public Store Store { get; }
    public KernelClient Kernel { get; private set; }
    public KernelSession User { get; private set; }
    /// <summary>Kernel'e ulaşılamadığında önbellekle çalışılıyor.</summary>
    public bool Offline { get; private set; }

    public event EventHandler ProductsChanged;

    public AppSession(Store store) => Store = store;

    public static string DataDir
    {
        get
        {
            var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            try
            {
                Directory.CreateDirectory(local);
                var probe = Path.Combine(local, ".write-test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return local; // "bulunduğu klasörde": kurulum klasörü yazılabilirse veritabanı yanında durur
            }
            catch
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NemraPOS", "StoreHelper");
            }
        }
    }

    public static string DbPath(string dataDir) => Path.Combine(dataDir, "storehelper.db");

    public string KernelUrl => Store.GetSetting("kernel.url");
    public string LastUser => Store.GetSetting("kernel.user");

    static string Protect(string text) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));

    static string Unprotect(string text)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    /// <summary>Kayıtlı token'la oturumu geri yükler. true: giriş gerekmez (çevrimdışı olsa da). false: giriş penceresi gerekir.</summary>
    public bool TryRestore()
    {
        var url = KernelUrl;
        var protectedToken = Store.GetSetting("kernel.token");
        if (url.Length == 0 || protectedToken.Length == 0) return false;
        var token = Unprotect(protectedToken);
        if (token.Length == 0) return false;
        try
        {
            Kernel = new KernelClient(url, token);
            User = Task.Run(() => Kernel.MeAsync()).GetAwaiter().GetResult();
            Offline = false;
            return true;
        }
        catch (KernelAuthException)
        {
            return false; // oturum bitmiş
        }
        catch (KernelException)
        {
            // Kernel ulaşılamıyor: son bilinen kullanıcıyla önbellekten çalış.
            User = new KernelSession { Token = token, Username = LastUser, FullName = Store.GetSetting("kernel.fullName", LastUser), Company = Store.GetSetting("kernel.company") };
            Offline = true;
            return true;
        }
    }

    public async Task LoginAsync(string url, string username, string password)
    {
        var client = new KernelClient(url);
        try
        {
            User = await client.LoginAsync(username, password);
        }
        catch
        {
            client.Dispose();
            throw;
        }
        Kernel?.Dispose();
        Kernel = client;
        Offline = false;
        Store.SetSetting("kernel.url", client.BaseUrl);
        Store.SetSetting("kernel.user", User.Username ?? username);
        Store.SetSetting("kernel.fullName", User.FullName ?? "");
        Store.SetSetting("kernel.company", User.Company ?? "");
        Store.SetSetting("kernel.token", Protect(User.Token));
    }

    public void Logout()
    {
        Store.DeleteSetting("kernel.token");
        Kernel?.Dispose();
        Kernel = null;
        User = null;
    }

    /// <summary>Ürün listesini kernel'den çekip yerel önbelleğe yazar.</summary>
    public async Task<int> SyncProductsAsync(IProgress<(int done, int total)> progress, CancellationToken ct = default)
    {
        if (Kernel == null) throw new InvalidOperationException("Kernel'e bağlı değilsiniz.");
        try
        {
            var products = await Kernel.GetAllProductsAsync(progress, ct);
            await Task.Run(() => Store.ReplaceProducts(products, Kernel.ServerTime), ct);
            Offline = false;
            ProductsChanged?.Invoke(this, EventArgs.Empty);
            return products.Count;
        }
        catch (KernelAuthException)
        {
            Store.DeleteSetting("kernel.token");
            throw;
        }
        catch (KernelException)
        {
            Offline = true;
            throw;
        }
    }

    public void Dispose()
    {
        Kernel?.Dispose();
        Store.Dispose();
    }
}
