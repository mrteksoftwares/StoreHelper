using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using StoreHelper.App.Reports;
using StoreHelper.Core;
using StoreHelper.Core.Data;
using StoreHelper.Core.Labels;

namespace StoreHelper.App.Panels;

/// <summary>Etiket tasarımları: ölçü/tabaka ayarları + DevExpress Rapor Tasarımcısı + dosya olarak dışa/içe aktarma (mağazalar arası kopyalama).</summary>
public sealed class TemplatesPanel : XtraUserControl
{
    sealed class Row
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Size { get; set; }
    }

    readonly AppSession _session;
    readonly Store _store;
    readonly GridControl _grid = new GridControl();
    GridView _view;
    readonly TextEdit _name = new TextEdit();
    readonly ComboBoxEdit _preset = new ComboBoxEdit();
    readonly SpinEdit _labelW = Ui.Number(10, 500, 60, 1), _labelH = Ui.Number(5, 500, 40, 1);
    readonly SpinEdit _sheetW = Ui.Number(20, 1000, 210, 1), _sheetH = Ui.Number(20, 1500, 297, 1);
    readonly SpinEdit _columns = Ui.Number(1, 20, 3);
    readonly SpinEdit _marginLeft = Ui.Number(0, 200, 7, 1), _marginTop = Ui.Number(0, 200, 10, 1);
    readonly SpinEdit _gapX = Ui.Number(0, 100, 2, 1), _gapY = Ui.Number(0, 100, 0, 1);
    readonly LabelControl _info = new LabelControl();
    readonly Panel _editor = new Panel();
    LabelTemplate _current;
    bool _loading;

    public event EventHandler TemplatesChanged;

    public TemplatesPanel(AppSession session)
    {
        _session = session;
        _store = session.Store;
        Dock = DockStyle.Fill;
        BuildUi();
        Reload();
    }

    void BuildUi()
    {
        var split = new SplitContainerControl { Dock = DockStyle.Fill, Horizontal = true, SplitterPosition = 330, FixedPanel = SplitFixedPanel.Panel1 };
        Controls.Add(split);

        _view = Ui.SimpleGrid(_grid);
        Ui.Cols(_view, ("Name", "Tasarım", 200), ("Size", "Etiket", 100));
        _grid.Dock = DockStyle.Fill;
        _view.FocusedRowChanged += (s, e) => { if (!_loading) SelectRow(); };
        var listBar = Ui.Buttons(Ui.Button("Yeni tasarım", (s, e) => NewTemplate(), primary: true), Ui.Button("İçe aktar…", (s, e) => ImportTemplate()));
        listBar.Dock = DockStyle.Bottom;
        split.Panel1.Controls.Add(_grid);
        split.Panel1.Controls.Add(listBar);

        _editor.Dock = DockStyle.Fill;
        _editor.AutoScroll = true;
        split.Panel2.Controls.Add(_editor);

        var form = Ui.Form(170);
        form.Row("Tasarım adı", _name);
        _preset.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
        _preset.Properties.Items.Add("(Ön ayar seçin…)");
        foreach (var p in LabelPreset.All) _preset.Properties.Items.Add(p.Name);
        _preset.SelectedIndex = 0;
        _preset.SelectedIndexChanged += (s, e) => ApplyPreset();
        form.Row("Hazır ölçü", _preset);
        form.Row("Etiket genişliği (mm)", _labelW);
        form.Row("Etiket yüksekliği (mm)", _labelH);
        form.Row("Tabaka genişliği (mm)", _sheetW);
        form.Row("Tabaka yüksekliği (mm)", _sheetH);
        form.Row("Yan yana etiket (sütun)", _columns);
        form.Row("Sol kenar boşluğu (mm)", _marginLeft);
        form.Row("Üst kenar boşluğu (mm)", _marginTop);
        form.Row("Yatay boşluk (mm)", _gapX);
        form.Row("Dikey boşluk (mm)", _gapY);
        foreach (var spin in new[] { _labelW, _labelH, _sheetW, _sheetH, _columns, _marginLeft, _marginTop, _gapX, _gapY })
            spin.EditValueChanged += (s, e) => { if (!_loading) UpdateInfo(); };

        _info.Dock = DockStyle.Top;
        _info.AutoSizeMode = LabelAutoSizeMode.Vertical;
        _info.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
        _info.Padding = new Padding(0, 8, 0, 8);

        var note = new LabelControl
        {
            Dock = DockStyle.Top, AutoSizeMode = LabelAutoSizeMode.Vertical, Padding = new Padding(0, 4, 0, 4),
            Text = "Tek etiket (rulo) yazdırmada sayfa = etiket ölçüsüdür; A4/tabaka yazdırmada etiketler tabakaya sütun/satır olarak dizilir. " +
                   "Etiketin içini \"Tasarla\" ile DevExpress Rapor Tasarımcısı'nda çizersiniz (alanlar: Name, PriceText, Unit, Barcode, Code, Date…)."
        };
        note.Appearance.ForeColor = Color.Gray;
        note.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;

        var buttons = Ui.Buttons(
            Ui.Button("Kaydet", (s, e) => Save(), primary: true),
            Ui.Button("Tasarla…", (s, e) => Design(), primary: true),
            Ui.Button("Kopyala", (s, e) => Duplicate()),
            Ui.Button("Dışa aktar…", (s, e) => ExportTemplate()),
            Ui.Button("Sil", (s, e) => Delete()));

        _editor.Controls.Add(buttons);
        _editor.Controls.Add(note);
        _editor.Controls.Add(_info);
        _editor.Controls.Add(form);
        _editor.Padding = new Padding(16, 8, 16, 8);
        _editor.Enabled = false;
    }

    // ── liste ───────────────────────────────────────────────────────────────────────────
    public void Reload(long? select = null)
    {
        _loading = true;
        var all = _store.GetTemplates();
        _grid.Bind(_view, new BindingList<Row>(all.Select(t => new Row { Id = t.Id, Name = t.Name, Size = $"{t.LabelWidthMm:0.#} × {t.LabelHeightMm:0.#} mm" }).ToList()));
        _loading = false;
        var index = select.HasValue ? all.FindIndex(t => t.Id == select.Value) : (_current != null ? all.FindIndex(t => t.Id == _current.Id) : 0);
        _view.FocusedRowHandle = index >= 0 ? index : 0;
        SelectRow();
        TemplatesChanged?.Invoke(this, EventArgs.Empty);
    }

    void SelectRow()
    {
        var row = _view.GetFocusedRow() as Row;
        _current = row == null ? null : _store.GetTemplate(row.Id);
        _editor.Enabled = _current != null;
        if (_current == null) return;
        _loading = true;
        _name.Text = _current.Name;
        _labelW.Value = _current.LabelWidthMm; _labelH.Value = _current.LabelHeightMm;
        _sheetW.Value = _current.SheetWidthMm; _sheetH.Value = _current.SheetHeightMm;
        _columns.Value = _current.Columns;
        _marginLeft.Value = _current.MarginLeftMm; _marginTop.Value = _current.MarginTopMm;
        _gapX.Value = _current.GapXMm; _gapY.Value = _current.GapYMm;
        _preset.SelectedIndex = 0;
        _loading = false;
        UpdateInfo();
    }

    LabelTemplate ReadFields()
    {
        var t = _current.Clone();
        t.Name = _name.Text.Trim();
        t.LabelWidthMm = _labelW.Value; t.LabelHeightMm = _labelH.Value;
        t.SheetWidthMm = _sheetW.Value; t.SheetHeightMm = _sheetH.Value;
        t.Columns = (int)_columns.Value;
        t.MarginLeftMm = _marginLeft.Value; t.MarginTopMm = _marginTop.Value;
        t.GapXMm = _gapX.Value; t.GapYMm = _gapY.Value;
        return t;
    }

    void UpdateInfo()
    {
        if (_current == null) return;
        var t = ReadFields();
        var error = SheetLayout.Validate(t, PrintMode.Sheet);
        if (error == null)
        {
            _info.Text = $"A4/tabaka: {t.Columns} sütun × {SheetLayout.Rows(t)} satır = {SheetLayout.LabelsPerSheet(t)} etiket/sayfa.";
            _info.Appearance.ForeColor = Color.SeaGreen;
        }
        else
        {
            _info.Text = "Tabaka yerleşimi: " + error + " (Tek etiket/rulo yazdırma bundan etkilenmez.)";
            _info.Appearance.ForeColor = Color.DarkOrange;
        }
    }

    void ApplyPreset()
    {
        if (_loading || _current == null || _preset.SelectedIndex <= 0) return;
        var t = ReadFields();
        LabelPreset.All[_preset.SelectedIndex - 1].ApplyTo(t);
        _loading = true;
        _labelW.Value = t.LabelWidthMm; _labelH.Value = t.LabelHeightMm; _sheetW.Value = t.SheetWidthMm; _sheetH.Value = t.SheetHeightMm;
        _columns.Value = t.Columns; _marginLeft.Value = t.MarginLeftMm; _marginTop.Value = t.MarginTopMm; _gapX.Value = t.GapXMm; _gapY.Value = t.GapYMm;
        _loading = false;
        UpdateInfo();
    }

    // ── işlemler ────────────────────────────────────────────────────────────────────────
    string UniqueName(string wanted)
    {
        var names = _store.GetTemplates().Select(t => t.Name).ToList();
        if (!names.Contains(wanted, StringComparer.CurrentCultureIgnoreCase)) return wanted;
        for (var i = 2; ; i++) if (!names.Contains($"{wanted} ({i})", StringComparer.CurrentCultureIgnoreCase)) return $"{wanted} ({i})";
    }

    void NewTemplate()
    {
        var name = XtraInputBox.Show("Yeni etiket tasarımının adı:", "Yeni tasarım", "Raf etiketi");
        if (string.IsNullOrWhiteSpace(name)) return;
        var t = new LabelTemplate { Name = UniqueName(name.Trim()) };
        LabelPreset.All[0].ApplyTo(t); // rulo 60 × 40; sonra "Hazır ölçü"den değiştirilir
        t.SheetWidthMm = 210; t.SheetHeightMm = 297; t.Columns = 3;
        t.Repx = LabelReportFactory.ToRepx(LabelReportFactory.CreateDefault(t));
        var id = _store.SaveTemplate(t);
        Reload(id);
        _name.Focus();
    }

    /// <summary>Alanları kaydeder. Etiket ölçüsü değiştiyse tasarımın sayfa boyutu da güncellenir (içerik ölçeklenmez).</summary>
    bool Save()
    {
        if (_current == null) return false;
        var t = ReadFields();
        if (t.Name.Length == 0) { Ui.Warn(this, "Tasarım adı boş olamaz."); return false; }
        if (_store.GetTemplates().Any(x => x.Id != t.Id && string.Equals(x.Name, t.Name, StringComparison.CurrentCultureIgnoreCase))) { Ui.Warn(this, "Bu adla başka bir tasarım var."); return false; }
        var sizeChanged = t.LabelWidthMm != _current.LabelWidthMm || t.LabelHeightMm != _current.LabelHeightMm;
        if (sizeChanged && !string.IsNullOrWhiteSpace(t.Repx))
        {
            using var report = LabelReportFactory.LoadOrCreate(t);
            report.PageWidth = (int)Math.Round(t.LabelWidthMm * 10m);
            report.PageHeight = (int)Math.Round(t.LabelHeightMm * 10m);
            var detail = report.Bands.OfType<DevExpress.XtraReports.UI.DetailBand>().FirstOrDefault();
            if (detail != null) detail.HeightF = report.PageHeight;
            t.Repx = LabelReportFactory.ToRepx(report);
        }
        _store.SaveTemplate(t);
        Reload(t.Id);
        return true;
    }

    void Design()
    {
        if (_current == null || !Save()) return;
        var template = _store.GetTemplate(_current.Id);
        if (DesignerHost.Edit(template, _store, FindForm())) Reload(template.Id);
    }

    void Duplicate()
    {
        if (_current == null) return;
        var copy = _store.GetTemplate(_current.Id);
        copy.Id = 0;
        copy.Name = UniqueName(copy.Name + " (kopya)");
        Reload(_store.SaveTemplate(copy));
    }

    void Delete()
    {
        if (_current == null || !Ui.Confirm(this, $"“{_current.Name}” tasarımı silinsin mi?")) return;
        _store.DeleteTemplate(_current.Id);
        _current = null;
        Reload();
    }

    void ExportTemplate()
    {
        if (_current == null) return;
        using var dialog = new SaveFileDialog { Filter = "StoreHelper etiket tasarımı (*.nlabel)|*.nlabel", FileName = _current.Name + ".nlabel", Title = "Tasarımı dışa aktar" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, Store.ExportTemplate(_store.GetTemplate(_current.Id)), new System.Text.UTF8Encoding(false));
        Ui.Info(this, "Tasarım dosyaya yazıldı. Diğer mağazalardaki StoreHelper'da \"İçe aktar\" ile yükleyebilirsiniz.");
    }

    void ImportTemplate()
    {
        using var dialog = new OpenFileDialog { Filter = "StoreHelper etiket tasarımı (*.nlabel)|*.nlabel", Title = "Tasarımı içe aktar", Multiselect = true };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        long? last = null;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var t = Store.ImportTemplate(File.ReadAllText(file));
                t.Name = UniqueName(string.IsNullOrWhiteSpace(t.Name) ? Path.GetFileNameWithoutExtension(file) : t.Name);
                last = _store.SaveTemplate(t);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is Newtonsoft.Json.JsonException)
            {
                Ui.Error(this, $"{Path.GetFileName(file)} içe aktarılamadı:\n{ex.Message}");
            }
        }
        if (last.HasValue) Reload(last);
    }
}
