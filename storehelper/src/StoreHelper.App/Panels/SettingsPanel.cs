using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using DevExpress.XtraEditors;

namespace StoreHelper.App.Panels;

/// <summary>Bağlantı ve veri bilgisi, oturumu kapatma.</summary>
public sealed class SettingsPanel : XtraUserControl
{
    readonly AppSession _session;
    readonly LabelControl _kernel = new LabelControl();
    readonly LabelControl _user = new LabelControl();
    readonly LabelControl _products = new LabelControl();
    readonly LabelControl _db = new LabelControl();
    readonly LabelControl _version = new LabelControl();

    public SettingsPanel(AppSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;
        Padding = new Padding(24, 16, 24, 16);

        var form = Ui.Form(170);
        form.Row("Kernel adresi", _kernel);
        form.Row("Kullanıcı", _user);
        form.Row("Ürün önbelleği", _products);
        form.Row("Yerel veritabanı", _db);
        form.Row("Sürüm", _version);
        var buttons = Ui.Buttons(
            Ui.Button("Veri klasörünü aç", (s, e) => Process.Start("explorer.exe", Path.GetDirectoryName(_session.Store.Path))),
            Ui.Button("Oturumu kapat", (s, e) =>
            {
                if (!Ui.Confirm(this, "Oturum kapatılsın mı? Uygulama yeniden başlar ve kernel girişi istenir. Yerel veriler (tasarımlar, teraziler) silinmez.")) return;
                _session.Logout();
                Application.Restart();
            }));
        Controls.Add(buttons);
        Controls.Add(form);
        Refresh2();
    }

    public void Refresh2()
    {
        _kernel.Text = _session.KernelUrl + (_session.Offline ? "  (çevrimdışı — önbellekle çalışıyor)" : "");
        _user.Text = $"{_session.User?.FullName} ({_session.User?.Username})" + (string.IsNullOrEmpty(_session.User?.Company) ? "" : $" · {_session.User.Company}");
        var synced = _session.Store.ProductsSyncedAt();
        _products.Text = $"{_session.Store.CountProducts():N0} ürün" + (synced.HasValue ? $" · son eşitleme {synced:dd.MM.yyyy HH:mm}" : " · henüz eşitlenmedi");
        _db.Text = _session.Store.Path;
        _version.Text = typeof(SettingsPanel).Assembly.GetName().Version.ToString(3);
    }
}
