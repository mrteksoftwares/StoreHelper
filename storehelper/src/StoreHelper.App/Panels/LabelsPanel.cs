using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraReports.UI;
using StoreHelper.Core;
using StoreHelper.Core.Labels;
using StoreHelper.App.Reports;

namespace StoreHelper.App.Panels;

/// <summary>Raf etiketi: ürün seç (tümü ya da son basımdan beri eklenen/fiyatı değişen) → basım listesi (adet) → şablon + yazıcı modu → önizleme / yazdır.</summary>
public sealed class LabelsPanel : XtraUserControl
{
    sealed class PrintRow
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Copies { get; set; } = 1;
        [Browsable(false)] public Product Product { get; set; }
    }

    readonly AppSession _session;
    readonly GridControl _productsGrid = new GridControl();
    readonly GridControl _listGrid = new GridControl();
    GridView _productsView, _listView;
    readonly BindingList<PrintRow> _rows = new BindingList<PrintRow>();
    readonly LabelControl _productsTitle = new LabelControl();
    readonly RadioGroup _show = new RadioGroup();
    readonly DateEdit _since = new DateEdit();
    readonly TextEdit _scan = new TextEdit();
    readonly LabelControl _scanInfo = new LabelControl();
    readonly ComboBoxEdit _template = new ComboBoxEdit();
    readonly RadioGroup _mode = new RadioGroup();
    readonly SpinEdit _skip = Ui.Number(0, 500, 0);
    readonly ComboBoxEdit _printer = new ComboBoxEdit();
    readonly LabelControl _summary = new LabelControl();
    List<LabelTemplate> _templates = new List<LabelTemplate>();
    List<Product> _products = new List<Product>();
    Control _skipRow, _printerRow;
    bool _loadingSince;

    const int ShowChanged = 0, ShowAll = 1;

    public LabelsPanel(AppSession session)
    {
        _session = session;
        Dock = DockStyle.Fill;
        BuildUi();
        _session.ProductsChanged += (s, e) => { if (IsHandleCreated) BeginInvoke((Action)ReloadProducts); };
        ReloadProducts();
        ReloadTemplates();
    }

    void BuildUi()
    {
        var split = new SplitContainerControl { Dock = DockStyle.Fill, Horizontal = true, SplitterPosition = 620, FixedPanel = SplitFixedPanel.None };
        split.Panel1.Text = "Ürünler";
        split.Panel2.Text = "Basım listesi";
        Controls.Add(split);

        // Sol: ürünler
        _productsView = Ui.SimpleGrid(_productsGrid, multiSelect: true, findPanel: true);
        Ui.Cols(_productsView, ("Code", "Kod", 110), ("Name", "Ürün adı", 260), ("Price", "Fiyat (KDV dahil)", 130), ("Unit", "Birim", 80), ("Category", "Kategori", 130), ("Barcodes", "Barkodlar", 170), ("Changed", "Değişim", 110));
        _productsGrid.Dock = DockStyle.Fill;
        _productsView.DoubleClick += (s, e) => AddSelected();
        _productsTitle.Dock = DockStyle.Top;
        _productsTitle.Padding = new Padding(2, 4, 0, 6);
        _productsTitle.Appearance.Font = new Font("Segoe UI Semibold", 10f);
        var addBar = Ui.Buttons(Ui.Button("Seçilenleri listeye ekle  →", (s, e) => AddSelected(), primary: true), Ui.Button("Görünenlerin tümünü ekle", (s, e) => AddVisible()));
        addBar.Dock = DockStyle.Bottom;

        // Filtre: son etiket basımından (elle değiştirilebilir) sonra eklenen / fiyatı değişen ürünler ya da tümü.
        _show.Properties.Items.AddRange(new[]
        {
            new DevExpress.XtraEditors.Controls.RadioGroupItem(ShowChanged, "Değişenler"),
            new DevExpress.XtraEditors.Controls.RadioGroupItem(ShowAll, "Tümü")
        });
        _show.Properties.ItemsLayout = DevExpress.XtraEditors.RadioGroupItemsLayout.Flow;
        _show.Size = new Size(200, 36);
        _show.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
        _show.BackColor = Color.Transparent;
        _show.Margin = new Padding(0, 1, 12, 0);
        _show.EditValue = _session.Store.GetSetting("labels.show", "0") == "1" ? ShowAll : ShowChanged;
        _show.SelectedIndexChanged += (s, e) => { _session.Store.SetSetting("labels.show", Convert.ToInt32(_show.EditValue).ToString()); ApplyFilter(); };
        _since.Properties.CalendarTimeEditing = DevExpress.Utils.DefaultBoolean.True;
        _since.Properties.Mask.EditMask = "dd.MM.yyyy HH:mm";
        _since.Properties.Mask.UseMaskAsDisplayFormat = true;
        _since.Properties.NullValuePrompt = "henüz basım yok";
        _since.Width = 140;
        _since.Margin = new Padding(0, 4, 6, 0);
        _since.EditValueChanged += (s, e) => { if (_loadingSince) return; _session.Store.LastLabelPrintAt = _since.EditValue as DateTime?; ApplyFilter(); };
        var sinceCaption = new LabelControl { Text = "Son etiket basımı:", Margin = new Padding(0, 8, 6, 0) };
        var now = Ui.Button("Şimdi", (s, e) => SetSince(DateTime.Now));
        var today = Ui.Button("Bugün 00:00", (s, e) => SetSince(DateTime.Today));
        now.Margin = today.Margin = new Padding(0, 2, 6, 0);
        var filterBar = Ui.Buttons(_show, sinceCaption, _since, now, today);
        filterBar.Padding = new Padding(0, 0, 0, 4);

        split.Panel1.Controls.Add(_productsGrid);
        split.Panel1.Controls.Add(addBar);
        split.Panel1.Controls.Add(filterBar);
        split.Panel1.Controls.Add(_productsTitle);

        // Sağ: basım listesi + seçenekler
        _listView = Ui.SimpleGrid(_listGrid);
        _listGrid.Dock = DockStyle.Fill;
        Ui.Cols(_listView, ("Code", "Kod", 90), ("Name", "Ürün", 200), ("Price", "Fiyat", 80), ("Copies", "Adet", 70));
        _listGrid.Bind(_listView, _rows);
        _listView.Columns["Price"].DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
        _listView.Columns["Price"].DisplayFormat.FormatString = "N2";
        _listView.Columns["Copies"].OptionsColumn.AllowEdit = true;
        var spin = new RepositoryItemSpinEdit { MinValue = 1, MaxValue = 999 };
        spin.Mask.EditMask = "n0";
        _listGrid.RepositoryItems.Add(spin);
        _listView.Columns["Copies"].ColumnEdit = spin;
        _listView.OptionsBehavior.Editable = true;
        _listView.OptionsView.ShowAutoFilterRow = false;
        _listView.CellValueChanged += (s, e) => UpdateSummary();
        _rows.ListChanged += (s, e) => UpdateSummary();

        _scan.Properties.NullValuePrompt = "Barkod okutun veya ürün kodu yazıp Enter'a basın";
        _scan.Dock = DockStyle.Top;
        _scan.Margin = new Padding(0, 0, 0, 4);
        _scan.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            ScanAdd();
        };
        _scanInfo.Dock = DockStyle.Top;
        _scanInfo.Padding = new Padding(2, 2, 0, 4);

        var listBar = Ui.Buttons(
            Ui.Button("Seçili satırı sil", (s, e) => { if (_listView.FocusedRowHandle >= 0) _rows.RemoveAt(_listView.GetDataSourceRowIndex(_listView.FocusedRowHandle)); }),
            Ui.Button("Listeyi temizle", (s, e) => _rows.Clear()));
        listBar.Dock = DockStyle.Bottom;

        var options = Ui.Form(130);
        options.Dock = DockStyle.Bottom;
        options.Row("Etiket tasarımı", _template);
        _template.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
        _template.SelectedIndexChanged += (s, e) => { SaveChoice(); UpdateSummary(); };
        _mode.Properties.Items.AddRange(new[]
        {
            new DevExpress.XtraEditors.Controls.RadioGroupItem(0, "Etiket yazıcısı / rulo (her etiket ayrı sayfa)"),
            new DevExpress.XtraEditors.Controls.RadioGroupItem(1, "A4 / tabaka yazıcısı (ızgara)")
        });
        _mode.Properties.Columns = 1;
        _mode.EditValue = 0;
        _mode.SelectedIndexChanged += (s, e) => { UpdateModeVisibility(); SaveChoice(); UpdateSummary(); };
        options.Row("Yazdırma türü", _mode, 52);
        _skipRow = options.Row("İlk boş hücre sayısı", _skip);
        _printer.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
        _printer.Properties.Items.Add("(Yazdırırken sor)");
        foreach (string name in PrinterSettings.InstalledPrinters) _printer.Properties.Items.Add(name);
        _printer.SelectedIndex = 0;
        _printer.SelectedIndexChanged += (s, e) => SaveChoice();
        _printerRow = options.Row("Yazıcı", _printer);
        _skip.EditValueChanged += (s, e) => UpdateSummary();

        _summary.Dock = DockStyle.Top;
        _summary.Padding = new Padding(2, 6, 0, 2);
        _summary.Appearance.Font = new Font("Segoe UI Semibold", 9.5f);
        var actions = Ui.Buttons(Ui.Button("Önizleme", (s, e) => Print(preview: true), width: 120), Ui.Button("Yazdır", (s, e) => Print(preview: false), primary: true, width: 120));
        actions.Dock = DockStyle.Bottom;

        split.Panel2.Controls.Add(_listGrid);
        split.Panel2.Controls.Add(_scanInfo);
        split.Panel2.Controls.Add(_scan);
        split.Panel2.Controls.Add(listBar);
        split.Panel2.Controls.Add(_summary);
        split.Panel2.Controls.Add(options);
        split.Panel2.Controls.Add(actions);

        RestoreChoice();
        UpdateModeVisibility();
    }

    // ── veri ────────────────────────────────────────────────────────────────────────────
    public void ReloadProducts()
    {
        _products = _session.Store.GetProducts().Where(p => p.Active).ToList();
        LoadSince();
        ApplyFilter();
        // Listedeki fiyatlar yeni eşitlemeyle güncellenir.
        foreach (var row in _rows)
        {
            var fresh = _products.FirstOrDefault(p => p.Code == row.Code);
            if (fresh != null) { row.Product = fresh; row.Name = fresh.Name; row.Price = fresh.GrossPrice; }
        }
        _listGrid.RefreshDataSource();
    }

    /// <summary>Sol listeyi "Değişenler" (son basımdan sonra eklenen/fiyatı değişen) ya da "Tümü" seçimine göre doldurur.</summary>
    void ApplyFilter()
    {
        var since = _since.EditValue as DateTime?;
        var changedMode = Convert.ToInt32(_show.EditValue) == ShowChanged;
        var changedOnly = changedMode && since.HasValue;
        var shown = changedOnly ? _products.Where(p => p.ChangedSince(since.Value)).ToList() : _products;
        _productsGrid.Bind(_productsView, new BindingList<ProductRow>(shown.Select(p => new ProductRow(p)).ToList()));
        var v = _productsView;
        v.Columns["Price"].DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
        v.Columns["Price"].DisplayFormat.FormatString = "N2";
        v.Columns["Changed"].DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
        v.Columns["Changed"].DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";
        var synced = _session.Store.ProductsSyncedAt();
        var syncText = synced.HasValue ? $" · son eşitleme {synced:dd.MM.yyyy HH:mm}" : "";
        if (_products.Count == 0) _productsTitle.Text = "Ürün yok — üstteki \"Ürünleri eşitle\" ile kernel'den çekin.";
        else if (changedOnly) _productsTitle.Text = $"{since:dd.MM.yyyy HH:mm} sonrası eklenen / fiyatı değişen: {shown.Count:N0} ürün (toplam {_products.Count:N0})" + syncText;
        else _productsTitle.Text = $"Ürünler ({_products.Count:N0})" + (changedMode ? " · son basım tarihi yok, tümü gösteriliyor" : "") + syncText;
    }

    void LoadSince()
    {
        _loadingSince = true;
        try { _since.EditValue = _session.Store.LastLabelPrintAt; }
        finally { _loadingSince = false; }
    }

    void SetSince(DateTime value)
    {
        _since.EditValue = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0);
        _show.EditValue = ShowChanged;
    }

    public void ReloadTemplates()
    {
        var keep = (_template.EditValue as string) ?? _session.Store.GetSetting("labels.template");
        _templates = _session.Store.GetTemplates();
        _template.Properties.Items.Clear();
        _template.Properties.Items.AddRange(_templates.Select(t => t.Name).ToArray());
        var index = _templates.FindIndex(t => t.Name == keep);
        if (index >= 0) _template.SelectedIndex = index;
        else if (_templates.Count > 0) _template.SelectedIndex = 0;
        UpdateSummary();
    }

    sealed class ProductRow
    {
        public ProductRow(Product p)
        {
            Product = p; Code = p.Code; Name = p.Name; Price = p.GrossPrice; Unit = p.UnitName; Category = p.CategoryName; Barcodes = p.BarcodesText;
            Changed = new[] { p.CreatedAt, p.PriceChangedAt }.Max();
        }
        public string Code { get; }
        public string Name { get; }
        public decimal Price { get; }
        public string Unit { get; }
        public string Category { get; }
        public string Barcodes { get; }
        /// <summary>Eklenme / son fiyat değişimi (hangisi sonraysa).</summary>
        public DateTime? Changed { get; }
        [Browsable(false)] public Product Product { get; }
    }

    /// <summary>Tanı/ekran görüntüsü: ilk N ürünü basım listesine ekler.</summary>
    public void FillSample(int count)
    {
        foreach (var p in _products.Take(count)) AddProduct(p, 2);
    }

    // ── liste işlemleri ─────────────────────────────────────────────────────────────────
    void AddProduct(Product p, int copies = 1)
    {
        var existing = _rows.FirstOrDefault(r => r.Code == p.Code);
        if (existing != null) { existing.Copies += copies; _listGrid.RefreshDataSource(); UpdateSummary(); return; }
        _rows.Add(new PrintRow { Code = p.Code, Name = p.Name, Price = p.GrossPrice, Copies = copies, Product = p });
    }

    void AddSelected()
    {
        var rows = _productsView.GetSelectedRows().Select(h => _productsView.GetRow(h) as ProductRow).Where(r => r != null).ToList();
        if (rows.Count == 0 && _productsView.FocusedRowHandle >= 0 && _productsView.GetFocusedRow() is ProductRow focused) rows.Add(focused);
        foreach (var r in rows) AddProduct(r.Product);
        _productsView.ClearSelection();
    }

    /// <summary>Soldaki listede görünen (filtre + arama sonrası) tüm ürünleri ekler.</summary>
    void AddVisible()
    {
        var rows = Enumerable.Range(0, _productsView.DataRowCount).Select(i => _productsView.GetRow(i) as ProductRow).Where(r => r != null).ToList();
        if (rows.Count == 0) { Ui.Info(this, "Listede eklenecek ürün yok."); return; }
        foreach (var r in rows) AddProduct(r.Product);
    }

    void ScanAdd()
    {
        var text = _scan.Text.Trim();
        if (text.Length == 0) return;
        var product = _session.Store.FindByCode(text);
        if (product == null) { _scanInfo.Text = $"“{text}” bulunamadı."; _scanInfo.Appearance.ForeColor = Color.Firebrick; }
        else { AddProduct(product); _scanInfo.Text = $"Eklendi: {product.Name} — {product.GrossPrice:N2} ₺"; _scanInfo.Appearance.ForeColor = Color.SeaGreen; }
        _scan.SelectAll();
        _scan.Text = "";
    }

    // ── seçenekler ──────────────────────────────────────────────────────────────────────
    PrintMode Mode => Convert.ToInt32(_mode.EditValue) == 1 ? PrintMode.Sheet : PrintMode.Single;

    LabelTemplate SelectedTemplate => _template.SelectedIndex >= 0 && _template.SelectedIndex < _templates.Count ? _templates[_template.SelectedIndex] : null;

    void UpdateModeVisibility()
    {
        var sheet = Mode == PrintMode.Sheet;
        Ui.SetVisible(_skipRow, sheet);
    }

    void SaveChoice()
    {
        if (!IsHandleCreated && _template.SelectedIndex < 0) return;
        var s = _session.Store;
        if (SelectedTemplate != null) s.SetSetting("labels.template", SelectedTemplate.Name);
        s.SetSetting("labels.mode", Convert.ToInt32(_mode.EditValue).ToString());
        s.SetSetting("labels.printer", _printer.SelectedIndex > 0 ? _printer.Text : "");
    }

    void RestoreChoice()
    {
        _mode.EditValue = _session.Store.GetSetting("labels.mode", "0") == "1" ? 1 : 0;
        var printer = _session.Store.GetSetting("labels.printer");
        var index = printer.Length == 0 ? 0 : _printer.Properties.Items.IndexOf(printer);
        _printer.SelectedIndex = index >= 0 ? index : 0;
    }

    void UpdateSummary()
    {
        var total = _rows.Sum(r => r.Copies);
        var t = SelectedTemplate;
        var text = $"{_rows.Count} ürün · {total} etiket";
        if (t != null && Mode == PrintMode.Sheet)
        {
            var labels = total + Convert.ToInt32(_skip.Value);
            var per = SheetLayout.LabelsPerSheet(t);
            if (per > 0) text += $" · {SheetLayout.Sheets(t, labels)} sayfa ({per} etiket/sayfa)";
        }
        _summary.Text = text;
    }

    // ── yazdırma ────────────────────────────────────────────────────────────────────────
    void Print(bool preview)
    {
        var template = SelectedTemplate;
        if (template == null) { Ui.Warn(this, "Önce \"Etiket tasarımları\" sekmesinden bir etiket tasarımı oluşturun."); return; }
        if (_rows.Count == 0) { Ui.Warn(this, "Basım listesi boş. Soldan ürün seçip listeye ekleyin."); return; }
        var error = SheetLayout.Validate(template, Mode);
        if (error != null) { Ui.Warn(this, $"“{template.Name}” bu yazdırma türü için uygun değil:\n{error}"); return; }

        try
        {
            var items = LabelPlan.Expand(_rows.Select(r => new PrintLine { Product = r.Product, Copies = r.Copies }), Mode == PrintMode.Sheet ? Convert.ToInt32(_skip.Value) : 0, DateTime.Now);
            using var report = LabelReportFactory.Build(template, Mode, items);
            using var tool = new ReportPrintTool(report);
            // StartPrint yalnızca yazıcıya gönderilince (önizlemedeki "Yazdır" dahil) tetiklenir; önizlemeyi açmak basım sayılmaz.
            tool.PrintingSystem.StartPrint += (s, e) => LogPrinted();
            var printer = _printer.SelectedIndex > 0 ? _printer.Text : null;
            if (preview)
            {
                if (tool.PreviewRibbonForm is Form previewForm) previewForm.WithIcon();
                tool.ShowRibbonPreviewDialog(DevExpress.LookAndFeel.UserLookAndFeel.Default);
            }
            else if (printer != null) tool.Print(printer);
            else tool.PrintDialog();
        }
        catch (Exception ex)
        {
            Ui.Error(this, $"Etiket yazdırılamadı:\n{ex.Message}");
        }
    }

    void LogPrinted()
    {
        foreach (var row in _rows) _session.Store.LogPrint(row.Code, row.Price, row.Copies);
        // Basılan fiyatlar son eşitlemedeki hâlidir: damga, ürünlerin kernel'den çekildiği an (KERNEL saatiyle; ürün değişim
        // zamanları da kernel saatidir, iki makinenin saat farkı değişenleri gizlemez). Eski kernel: yerel eşitleme anı.
        var synced = _session.Store.ProductsSyncedAt();
        _session.Store.LastLabelPrintAt = _session.Store.ProductsKernelTime() ?? (synced.HasValue && synced.Value < DateTime.Now ? synced.Value : DateTime.Now);
        if (IsHandleCreated) BeginInvoke((Action)(() => { LoadSince(); ApplyFilter(); }));
    }
}
