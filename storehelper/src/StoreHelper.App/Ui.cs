using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;

namespace StoreHelper.App;

/// <summary>Kod ile kurulan arayüzler için küçük yardımcılar.</summary>
static class Ui
{
    static System.Drawing.Icon _appIcon;

    /// <summary>Exe'ye gömülü "SH" ikonu (app.ico) — tüm pencerelerin başlık/görev çubuğu simgesi. Alınamazsa null (varsayılan simge).</summary>
    public static System.Drawing.Icon AppIcon
    {
        get
        {
            if (_appIcon == null)
            {
                try { _appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { /* simge yoksa varsayılanla devam */ }
            }
            return _appIcon;
        }
    }

    /// <summary>Pencereye uygulama ikonunu verir (Form.Icon).</summary>
    public static T WithIcon<T>(this T form) where T : Form
    {
        if (AppIcon != null) form.Icon = AppIcon;
        return form;
    }

    public static TableLayoutPanel Form(int labelWidth = 150)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 4, 0, 4) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    public static T Row<T>(this TableLayoutPanel table, string caption, T control, int height = 28) where T : Control
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new LabelControl { Text = caption, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 6) };
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 3, 0, 3);
        control.MinimumSize = new Size(0, height);
        control.Tag = label;
        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
        return control;
    }

    /// <summary>Row() ile eklenen alanı başlığıyla birlikte göster/gizle.</summary>
    public static void SetVisible(Control control, bool visible)
    {
        control.Visible = visible;
        if (control.Tag is Control caption) caption.Visible = visible;
    }

    public static SimpleButton Button(string text, EventHandler onClick, bool primary = false, int width = 0)
    {
        var button = new SimpleButton { Text = text, Margin = new Padding(0, 0, 8, 0), Height = 30 };
        if (width > 0) button.Width = width;
        else button.AutoSize = true;
        if (primary) button.Appearance.Font = new Font(button.Appearance.Font, FontStyle.Bold);
        button.Click += onClick;
        return button;
    }

    public static FlowLayoutPanel Buttons(params Control[] buttons)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 6, 0, 6) };
        panel.Controls.AddRange(buttons);
        return panel;
    }

    public static SpinEdit Number(decimal min, decimal max, decimal value, int decimals = 0)
    {
        var spin = new SpinEdit { Value = value };
        spin.Properties.MinValue = min;
        spin.Properties.MaxValue = max;
        spin.Properties.IsFloatValue = decimals > 0;
        spin.Properties.Increment = decimals > 0 ? 0.5m : 1;
        spin.Properties.Mask.EditMask = decimals > 0 ? "n" + decimals : "n0";
        spin.Properties.Mask.UseMaskAsDisplayFormat = true;
        return spin;
    }

    public static ComboBoxEdit Combo(params string[] items)
    {
        var combo = new ComboBoxEdit();
        combo.Properties.Items.AddRange(items);
        combo.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
        if (items.Length > 0) combo.SelectedIndex = 0;
        return combo;
    }

    /// <summary>Veri kaynağını bağlar.</summary>
    public static void Bind(this GridControl grid, GridView view, object dataSource)
    {
        grid.DataSource = dataSource;
    }

    /// <summary>Sütunları açıkça tanımlar (otomatik üretime güvenilmez: boş listede sütun oluşmaz).</summary>
    public static void Cols(GridView view, params (string Field, string Caption, int Width)[] columns)
    {
        view.OptionsBehavior.AutoPopulateColumns = false;
        view.Columns.Clear();
        foreach (var (field, caption, width) in columns)
        {
            var column = view.Columns.AddField(field);
            column.Caption = caption;
            column.Width = width;
            column.Visible = true;
        }
    }

    public static GridView SimpleGrid(GridControl grid, bool multiSelect = false, bool findPanel = false)
    {
        var view = new GridView(grid);
        grid.MainView = view;
        grid.ViewCollection.Add(view);
        view.OptionsBehavior.Editable = false;
        view.OptionsView.ShowGroupPanel = false;
        view.OptionsView.ColumnAutoWidth = true;
        view.OptionsView.ShowIndicator = false;
        view.OptionsFind.AlwaysVisible = findPanel;
        view.OptionsFind.FindNullPrompt = "Ürün kodu, adı, barkodu ara…";
        view.OptionsFind.ShowCloseButton = false;
        view.OptionsFind.ShowFindButton = false;
        view.OptionsFind.ShowClearButton = false;
        view.OptionsFind.FindDelay = 250;
        if (multiSelect)
        {
            view.OptionsSelection.MultiSelect = true;
            view.OptionsSelection.MultiSelectMode = GridMultiSelectMode.CheckBoxRowSelect;
        }
        return view;
    }

    public static void Info(IWin32Window owner, string text) => XtraMessageBox.Show(owner, text, "StoreHelper", MessageBoxButtons.OK, MessageBoxIcon.Information);
    public static void Warn(IWin32Window owner, string text) => XtraMessageBox.Show(owner, text, "StoreHelper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    public static void Error(IWin32Window owner, string text) => XtraMessageBox.Show(owner, text, "StoreHelper", MessageBoxButtons.OK, MessageBoxIcon.Error);
    public static bool Confirm(IWin32Window owner, string text) =>
        XtraMessageBox.Show(owner, text, "StoreHelper", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}
