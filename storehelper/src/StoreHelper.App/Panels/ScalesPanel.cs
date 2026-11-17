using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using StoreHelper.Core;
using StoreHelper.Core.Data;
using StoreHelper.Core.Scales;

namespace StoreHelper.App.Panels;

/// <summary>Terazi aktarımı: terazi tanımları (IP/CSV yolu/biçim) + ürün listesini CSV dosyasına veya TCP'ye gönderme.</summary>
public sealed class ScalesPanel : XtraUserControl
{
    sealed class Row
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Target { get; set; }
        public string Last { get; set; }
    }

    static readonly string[] SeparatorNames = { "; (noktalı virgül)", ", (virgül)", "| (dikey çizgi)", "Sekme (Tab)" };
    static readonly string[] SeparatorValues = { ";", ",", "|", "\\t" };
    static readonly string[] EncodingNames = { "Windows-1254 (Türkçe, çoğu terazi)", "UTF-8", "ASCII (Türkçe karakterler sadeleşir)" };
    static readonly string[] EncodingValues = { "windows-1254", "utf-8", "ascii" };
    static readonly string[] PriceNames = { "12,50 (virgüllü)", "12.50 (noktalı)", "1250 (kuruş, tam sayı)" };
    static readonly string[] PriceValues = { "decimal-comma", "decimal-dot", "kurus" };

    readonly AppSession _session;
    readonly Store _store;
    readonly GridControl _grid = new GridControl();
    GridView _view;
    readonly Panel _editor = new Panel();
    readonly TextEdit _name = new TextEdit();
    readonly ComboBoxEdit _output = Ui.Combo("CSV dosyası (klasöre / ağ yoluna yaz)", "TCP ile gönder (IP:port'a metin)");
    readonly ButtonEdit _path = new ButtonEdit();
    readonly TextEdit _ip = new TextEdit();
    readonly SpinEdit _port = Ui.Number(0, 65535, 0);
    readonly ComboBoxEdit _separator = Ui.Combo(SeparatorNames);
    readonly ComboBoxEdit _encoding = Ui.Combo(EncodingNames);
    readonly ComboBoxEdit _price = Ui.Combo(PriceNames);
    readonly SpinEdit _nameMax = Ui.Number(0, 200, 24);
    readonly ComboBoxEdit _barcodeKind = Ui.Combo("Ağırlıklı barkod (tartı)", "Tutarlı barkod (fiyat gömülü)");
    readonly ToggleSwitch _fold = new ToggleSwitch();
    readonly ToggleSwitch _header = new ToggleSwitch();
    readonly ToggleSwitch _active = new ToggleSwitch();
    readonly CheckedListBoxControl _columns = new CheckedListBoxControl();
    readonly LabelControl _summary = new LabelControl();
    Control _pathRow, _ipRow, _portRow;
    ScaleDevice _current;
    bool _loading;

    public ScalesPanel(AppSession session)
    {
        _session = session;
        _store = session.Store;
        Dock = DockStyle.Fill;
        BuildUi();
        Reload();
        _session.ProductsChanged += (s, e) => { if (IsHandleCreated) BeginInvoke((Action)UpdateSummary); };
    }

    void BuildUi()
    {
        var split = new SplitContainerControl { Dock = DockStyle.Fill, Horizontal = true, SplitterPosition = 330, FixedPanel = SplitFixedPanel.Panel1 };
        Controls.Add(split);

        _view = Ui.SimpleGrid(_grid);
        Ui.Cols(_view, ("Name", "Terazi", 150), ("Target", "Hedef", 150), ("Last", "Son gönderim", 90));
        _grid.Dock = DockStyle.Fill;
        _view.FocusedRowChanged += (s, e) => { if (!_loading) SelectRow(); };
        var listBar = Ui.Buttons(Ui.Button("Yeni terazi", (s, e) => NewScale(), primary: true), Ui.Button("Tümüne gönder", async (s, e) => await SendAllAsync()));
        listBar.Dock = DockStyle.Bottom;
        split.Panel1.Controls.Add(_grid);
        split.Panel1.Controls.Add(listBar);

        _editor.Dock = DockStyle.Fill;
        _editor.AutoScroll = true;
        _editor.Padding = new Padding(16, 8, 16, 8);
        split.Panel2.Controls.Add(_editor);

        var form = Ui.Form(270);
        form.Row("Terazi adı", _name);
        form.Row("Aktarım şekli", _output);
        _output.SelectedIndexChanged += (s, e) => UpdateOutputVisibility();
        _pathRow = form.Row("Klasör / dosya yolu", _path);
        _path.Properties.ButtonClick += (s, e) => BrowseFolder();
        _path.Properties.NullValuePrompt = @"C:\Terazi  veya  \\sunucu\paylasim\terazi.csv";
        _ipRow = form.Row("Terazi IP adresi", _ip);
        _portRow = form.Row("Port", _port);
        form.Row("Alan ayırıcı", _separator);
        form.Row("Karakter kodlaması", _encoding);
        form.Row("Fiyat biçimi", _price);
        form.Row("Ürün adı en çok (karakter)", _nameMax);
        form.Row("Barkod türü", _barcodeKind);
        form.Row("Türkçe karakterleri sadeleştir", _fold);
        form.Row("İlk satırda başlık olsun", _header);
        form.Row("Terazi aktif (toplu gönderime dahil)", _active);

        _columns.Height = 190;
        _columns.CheckOnClick = true;
        var columnsHint = new LabelControl { Dock = DockStyle.Top, Text = "Dosyaya yazılacak alanlar (işaretli olanlar, listedeki sırayla):", Padding = new Padding(0, 6, 0, 4) };
        var columnButtons = Ui.Buttons(Ui.Button("↑ Yukarı", (s, e) => MoveColumn(-1)), Ui.Button("↓ Aşağı", (s, e) => MoveColumn(1)));
        var columnsHost = new Panel { Dock = DockStyle.Top, Height = 200, Padding = new Padding(0) };
        _columns.Dock = DockStyle.Fill;
        columnsHost.Controls.Add(_columns);

        _summary.Dock = DockStyle.Top;
        _summary.AutoSizeMode = LabelAutoSizeMode.Vertical;
        _summary.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
        _summary.Padding = new Padding(0, 8, 0, 8);

        var buttons = Ui.Buttons(
            Ui.Button("Kaydet", (s, e) => Save(), primary: true),
            Ui.Button("Önizle (CSV)", (s, e) => Preview()),
            Ui.Button("Bu teraziye gönder", async (s, e) => await SendAsync(), primary: true),
            Ui.Button("Sil", (s, e) => Delete()));

        _editor.Controls.Add(buttons);
        _editor.Controls.Add(_summary);
        _editor.Controls.Add(columnButtons);
        _editor.Controls.Add(columnsHost);
        _editor.Controls.Add(columnsHint);
        _editor.Controls.Add(form);
        _editor.Enabled = false;
    }

    // ── liste/alanlar ───────────────────────────────────────────────────────────────────
    void Reload(long? select = null)
    {
        _loading = true;
        var all = _store.GetScales();
        _grid.Bind(_view, new BindingList<Row>(all.Select(s => new Row
        {
            Id = s.Id, Name = s.Name + (s.Active ? "" : " (pasif)"),
            Target = s.Output == ScaleOutput.TcpText ? $"{s.Ip}:{s.Port}" : (string.IsNullOrEmpty(s.OutputPath) ? "—" : s.OutputPath),
            Last = s.LastExportAt.HasValue ? s.LastExportAt.Value.ToString("dd.MM HH:mm") : "—"
        }).ToList()));
        _loading = false;
        var index = select.HasValue ? all.FindIndex(s => s.Id == select.Value) : (_current != null ? all.FindIndex(s => s.Id == _current.Id) : 0);
        _view.FocusedRowHandle = index >= 0 ? index : 0;
        SelectRow();
    }

    void SelectRow()
    {
        var row = _view.GetFocusedRow() as Row;
        _current = row == null ? null : _store.GetScales().FirstOrDefault(s => s.Id == row.Id);
        _editor.Enabled = _current != null;
        if (_current == null) return;
        _loading = true;
        _name.Text = _current.Name;
        _output.SelectedIndex = _current.Output == ScaleOutput.TcpText ? 1 : 0;
        _path.Text = _current.OutputPath;
        _ip.Text = _current.Ip;
        _port.Value = _current.Port;
        _separator.SelectedIndex = Math.Max(0, Array.IndexOf(SeparatorValues, _current.Separator));
        _encoding.SelectedIndex = Math.Max(0, Array.IndexOf(EncodingValues, _current.Encoding));
        _price.SelectedIndex = Math.Max(0, Array.IndexOf(PriceValues, _current.PriceFormat));
        _nameMax.Value = _current.NameMaxLength;
        _barcodeKind.SelectedIndex = _current.BarcodeKind == "price" ? 1 : 0;
        _fold.IsOn = _current.FoldTurkish;
        _header.IsOn = _current.IncludeHeader;
        _active.IsOn = _current.Active;

        var chosen = _current.ColumnKeys.ToList();
        _columns.Items.Clear();
        foreach (var key in chosen.Concat(ScaleColumns.All.Select(c => c.Key).Where(k => !chosen.Contains(k))).Where(k => ScaleColumns.All.Any(c => c.Key == k)))
        {
            var column = ScaleColumns.All.First(c => c.Key == key);
            _columns.Items.Add(key, $"{column.Title} — {column.Description}", chosen.Contains(key) ? CheckState.Checked : CheckState.Unchecked, true);
        }
        _loading = false;
        UpdateOutputVisibility();
        UpdateSummary();
    }

    void UpdateOutputVisibility()
    {
        var tcp = _output.SelectedIndex == 1;
        Ui.SetVisible(_pathRow, !tcp);
        Ui.SetVisible(_ipRow, tcp);
        Ui.SetVisible(_portRow, tcp);
    }

    ScaleDevice ReadFields()
    {
        var s = new ScaleDevice
        {
            Id = _current.Id, Name = _name.Text.Trim(), Output = _output.SelectedIndex == 1 ? ScaleOutput.TcpText : ScaleOutput.CsvFile,
            OutputPath = _path.Text.Trim(), Ip = _ip.Text.Trim(), Port = (int)_port.Value,
            Separator = SeparatorValues[Math.Max(0, _separator.SelectedIndex)], Encoding = EncodingValues[Math.Max(0, _encoding.SelectedIndex)],
            PriceFormat = PriceValues[Math.Max(0, _price.SelectedIndex)], NameMaxLength = (int)_nameMax.Value,
            BarcodeKind = _barcodeKind.SelectedIndex == 1 ? "price" : "weight",
            FoldTurkish = _fold.IsOn, IncludeHeader = _header.IsOn, Active = _active.IsOn,
            LastExportAt = _current.LastExportAt, LastExportResult = _current.LastExportResult
        };
        s.Columns = string.Join(",", Enumerable.Range(0, _columns.ItemCount).Where(i => _columns.GetItemChecked(i)).Select(i => (string)_columns.GetItemValue(i)));
        return s;
    }

    void MoveColumn(int delta)
    {
        var i = _columns.SelectedIndex;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _columns.ItemCount) return;
        var a = _columns.Items[i]; var b = _columns.Items[j];
        _columns.Items[i] = b; _columns.Items[j] = a;
        _columns.SelectedIndex = j;
    }

    void UpdateSummary()
    {
        var (included, skipped) = ScaleExport.Select(_store.GetProducts());
        _summary.Text = _store.CountProducts() == 0
            ? "Ürün önbelleği boş: önce üstteki \"Ürünleri eşitle\" ile kernel'den ürünleri çekin."
            : $"Teraziye gidecek ürün: {included.Count}" + (skipped > 0 ? $" · PLU (tartı kodu) tanımsız olduğu için atlanan: {skipped} (Mikro'da ürüne 5 haneli tartı barkodu/kısa kod tanımlayın)" : "")
              + (_current?.LastExportResult.Length > 0 ? $"\nSon gönderim: {_current.LastExportResult}" : "");
        _summary.Appearance.ForeColor = included.Count == 0 ? Color.DarkOrange : Color.SeaGreen;
    }

    // ── işlemler ────────────────────────────────────────────────────────────────────────
    void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Terazi CSV dosyasının yazılacağı klasör", SelectedPath = _path.Text };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK) _path.Text = dialog.SelectedPath;
    }

    void NewScale()
    {
        var name = XtraInputBox.Show("Terazinin adı:", "Yeni terazi", "Terazi 1");
        if (string.IsNullOrWhiteSpace(name)) return;
        var scale = new ScaleDevice { Name = name.Trim() };
        Reload(_store.SaveScale(scale));
    }

    bool Save()
    {
        if (_current == null) return false;
        var s = ReadFields();
        if (s.Name.Length == 0) { Ui.Warn(this, "Terazi adı boş olamaz."); return false; }
        if (s.Columns.Length == 0) { Ui.Warn(this, "En az bir alan işaretleyin."); return false; }
        _store.SaveScale(s);
        Reload(s.Id);
        return true;
    }

    void Delete()
    {
        if (_current == null || !Ui.Confirm(this, $"“{_current.Name}” terazisi silinsin mi?")) return;
        _store.DeleteScale(_current.Id);
        _current = null;
        Reload();
    }

    void Preview()
    {
        if (_current == null) return;
        try
        {
            var (included, _) = ScaleExport.Select(_store.GetProducts());
            var csv = ScaleExport.BuildCsv(ReadFields(), included.Take(200));
            using var form = new XtraForm { Text = $"CSV önizleme — ilk {Math.Min(200, included.Count)} ürün", Size = new Size(760, 520), StartPosition = FormStartPosition.CenterParent }.WithIcon();
            var memo = new MemoEdit { Dock = DockStyle.Fill, Text = csv };
            memo.Properties.ReadOnly = true;
            memo.Properties.ScrollBars = ScrollBars.Both;
            memo.Properties.WordWrap = false;
            memo.Font = new Font("Consolas", 10f);
            form.Controls.Add(memo);
            form.ShowDialog(FindForm());
        }
        catch (Exception ex) { Ui.Error(this, ex.Message); }
    }

    async System.Threading.Tasks.Task SendAsync()
    {
        if (_current == null || !Save()) return;
        var scale = _store.GetScales().First(x => x.Id == _current.Id);
        var message = await SendOneAsync(scale);
        Ui.Info(this, message);
        Reload(scale.Id);
    }

    async System.Threading.Tasks.Task SendAllAsync()
    {
        var scales = _store.GetScales().Where(s => s.Active).ToList();
        if (scales.Count == 0) { Ui.Warn(this, "Aktif terazi yok."); return; }
        var lines = new System.Collections.Generic.List<string>();
        foreach (var scale in scales) lines.Add($"{scale.Name}: {await SendOneAsync(scale)}");
        Ui.Info(this, string.Join("\n", lines));
        Reload();
    }

    async System.Threading.Tasks.Task<string> SendOneAsync(ScaleDevice scale)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var result = await ScaleExport.ExportAsync(scale, _store.GetProducts());
            _store.RecordScaleExport(scale.Id, result.Message);
            return result.Message;
        }
        catch (Exception ex)
        {
            var message = "Gönderilemedi: " + ex.Message;
            _store.RecordScaleExport(scale.Id, message);
            return message;
        }
        finally { Cursor = Cursors.Default; }
    }
}
