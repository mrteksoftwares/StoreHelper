using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraTab;
using StoreHelper.App.Panels;
using StoreHelper.Core.Kernel;

namespace StoreHelper.App;

public sealed class MainForm : XtraForm
{
    readonly AppSession _session;
    readonly XtraTabControl _tabs = new XtraTabControl();
    readonly LabelControl _status = new LabelControl();
    readonly SimpleButton _sync = new SimpleButton();
    readonly ProgressBarControl _progress = new ProgressBarControl();
    readonly LabelControl _who = new LabelControl();
    LabelsPanel _labels;
    TemplatesPanel _templates;
    ScalesPanel _scales;
    SettingsPanel _settings;

    public MainForm(AppSession session)
    {
        _session = session;
        Text = "NemraPOS StoreHelper";
        this.WithIcon();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1240, 780);
        MinimumSize = new Size(1000, 640);

        _labels = new LabelsPanel(session);
        _templates = new TemplatesPanel(session);
        _scales = new ScalesPanel(session);
        _settings = new SettingsPanel(session);
        _templates.TemplatesChanged += (s, e) => _labels.ReloadTemplates();

        AddTab("Raf etiketi", _labels);
        AddTab("Etiket tasarımları", _templates);
        AddTab("Terazi aktarımı", _scales);
        AddTab("Bağlantı", _settings);
        _tabs.Dock = DockStyle.Fill;
        _tabs.SelectedPageChanged += (s, e) => { _labels.ReloadTemplates(); _settings.Refresh2(); };

        var top = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(12, 8, 12, 4) };
        _who.Location = new Point(12, 14);
        _who.Appearance.Font = new Font("Segoe UI Semibold", 10f);
        _sync.Text = "↻ Ürünleri eşitle";
        _sync.Size = new Size(150, 30);
        _sync.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _sync.Click += async (s, e) => await SyncAsync();
        _progress.Size = new Size(160, 18);
        _progress.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _progress.Visible = false;
        top.Controls.AddRange(new Control[] { _who, _sync, _progress });
        top.Resize += (s, e) => { _sync.Location = new Point(top.Width - _sync.Width - 12, 8); _progress.Location = new Point(_sync.Left - _progress.Width - 12, 14); };

        _status.Dock = DockStyle.Bottom;
        _status.Padding = new Padding(12, 4, 12, 4);

        Controls.Add(_tabs);
        Controls.Add(_status);
        Controls.Add(top);
        UpdateHeader();

        Shown += async (s, e) =>
        {
            // İlk açılışta (ürün yoksa) ya da kernel'e bağlıyken ürünler eskiyse otomatik eşitle.
            if (!_session.Offline && (_session.Store.CountProducts() == 0 || (DateTime.Now - (_session.Store.ProductsSyncedAt() ?? DateTime.MinValue)).TotalHours > 12)) await SyncAsync();
        };
    }

    public int TabCount => _tabs.TabPages.Count;
    public void SelectTab(int index) => _tabs.SelectedTabPageIndex = index;
    public void FillSampleList(int count) => _labels.FillSample(count);

    void AddTab(string title, Control content)
    {
        var page = new XtraTabPage { Text = title };
        page.Controls.Add(content);
        _tabs.TabPages.Add(page);
    }

    void UpdateHeader()
    {
        var company = string.IsNullOrEmpty(_session.User?.Company) ? "" : _session.User.Company + " · ";
        _who.Text = $"{company}{_session.User?.FullName ?? _session.User?.Username}";
        _status.Text = _session.Offline ? "Kernel'e ulaşılamıyor — son eşitlenen ürünlerle çalışılıyor." : $"Kernel: {_session.KernelUrl}";
        _status.Appearance.ForeColor = _session.Offline ? Color.DarkOrange : Color.Gray;
    }

    async System.Threading.Tasks.Task SyncAsync()
    {
        if (_session.Kernel == null || !_sync.Enabled) return;
        _sync.Enabled = false;
        _progress.Visible = true;
        _progress.Properties.Minimum = 0;
        _progress.Position = 0;
        _status.Text = "Ürünler kernel'den alınıyor…";
        try
        {
            var progress = new Progress<(int done, int total)>(p =>
            {
                _progress.Properties.Maximum = Math.Max(1, p.total);
                _progress.Position = p.done;
                _status.Text = $"Ürünler alınıyor… {p.done:N0} / {p.total:N0}";
            });
            var count = await _session.SyncProductsAsync(progress);
            UpdateHeader();
            _status.Text = $"{count:N0} ürün eşitlendi ({DateTime.Now:HH:mm}).";
            _settings.Refresh2();
        }
        catch (KernelAuthException)
        {
            Ui.Warn(this, "Kernel oturumunuz sona ermiş. Uygulama yeniden başlayacak, tekrar giriş yapın.");
            _session.Logout();
            Application.Restart();
        }
        catch (KernelException ex)
        {
            UpdateHeader();
            _status.Text = ex.Message;
            Ui.Warn(this, ex.Message + (_session.Store.CountProducts() > 0 ? "\n\nSon eşitlenen ürünlerle çalışmaya devam edebilirsiniz." : ""));
        }
        finally
        {
            _progress.Visible = false;
            _sync.Enabled = true;
        }
    }
}
