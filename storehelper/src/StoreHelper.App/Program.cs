using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using DevExpress.LookAndFeel;
using DevExpress.XtraEditors;
using StoreHelper.Core.Data;

namespace StoreHelper.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var culture = new CultureInfo("tr-TR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        WindowsFormsSettings.SetPerMonitorDpiAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        WindowsFormsSettings.DefaultFont = new System.Drawing.Font("Segoe UI", 9.5f);
        UserLookAndFeel.Default.SetSkinStyle(SkinStyle.WXI);

        Application.ThreadException += (s, e) => XtraMessageBox.Show($"Beklenmeyen hata:\n{e.Exception.Message}", "StoreHelper", MessageBoxButtons.OK, MessageBoxIcon.Error);

        var renderIndex = Array.IndexOf(args, "--render-sample");
        if (renderIndex >= 0)
        {
            var dir = renderIndex + 1 < args.Length ? args[renderIndex + 1] : System.IO.Path.Combine(AppSession.DataDir, "sample");
            try { XtraMessageBox.Show(Reports.Diagnostics.RenderSamples(dir), "StoreHelper"); }
            catch (Exception ex) { XtraMessageBox.Show(ex.ToString(), "StoreHelper", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }

        var snapIndex = Array.IndexOf(args, "--ui-snapshot");
        if (snapIndex >= 0 && args.Length >= snapIndex + 5)
        {
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(args[snapIndex + 1], "done.txt"), Reports.UiSnapshot.Run(args[snapIndex + 1], args[snapIndex + 2], args[snapIndex + 3], args[snapIndex + 4])); }
            catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(args[snapIndex + 1], "error.txt"), ex.ToString()); }
            return;
        }

        Store store;
        try
        {
            store = new Store(AppSession.DbPath(AppSession.DataDir));
        }
        catch (Exception ex)
        {
            XtraMessageBox.Show($"Yerel veritabanı açılamadı:\n{ex.Message}", "StoreHelper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var session = new AppSession(store);
        if (!session.TryRestore())
        {
            using var login = new LoginForm(session);
            if (login.ShowDialog() != DialogResult.OK) return;
        }

        Application.Run(new MainForm(session));
    }
}
