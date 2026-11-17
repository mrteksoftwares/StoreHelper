using System;
using System.Windows.Forms;
using DevExpress.XtraReports.UI;
using DevExpress.XtraReports.UserDesigner;
using StoreHelper.Core;
using StoreHelper.Core.Data;

namespace StoreHelper.App.Reports;

/// <summary>DevExpress Rapor Tasarımcısı'nı (ribbon) açar; Kaydet komutunu yerel SQLite'a yönlendirir.</summary>
public static class DesignerHost
{
    /// <summary>Tasarımı düzenler. Kaydedildiyse şablonu (REPX + etiket ölçüsü) günceller ve true döner.</summary>
    public static bool Edit(LabelTemplate template, Store store, IWin32Window owner)
    {
        var report = LabelReportFactory.LoadOrCreate(template);
        var saved = false;

        using var tool = new ReportDesignTool(report);
        var form = tool.DesignRibbonForm;
        if (form is Form window)
        {
            window.Text = $"Etiket tasarımı — {template.Name}";
            window.WithIcon();
        }
        form.DesignMdiController.AddCommandHandler(new SaveHandler(form.DesignMdiController, () =>
        {
            var edited = form.DesignMdiController.ActiveDesignPanel?.Report ?? report;
            template.Repx = LabelReportFactory.ToRepx(edited);
            var (w, h) = LabelReportFactory.LabelSizeMm(edited);
            template.LabelWidthMm = w;
            template.LabelHeightMm = h;
            store.SaveTemplate(template);
            saved = true;
        }));
        tool.ShowRibbonDesignerDialog();
        return saved;
    }

    sealed class SaveHandler : ICommandHandler
    {
        readonly XRDesignMdiController _controller;
        readonly Action _save;

        public SaveHandler(XRDesignMdiController controller, Action save)
        {
            _controller = controller;
            _save = save;
        }

        public bool CanHandleCommand(ReportCommand command, ref bool useNextHandler)
        {
            var mine = command == ReportCommand.SaveFile || command == ReportCommand.SaveFileAs;
            useNextHandler = !mine;
            return mine;
        }

        public void HandleCommand(ReportCommand command, object[] args)
        {
            _save();
            var panel = _controller.ActiveDesignPanel;
            if (panel != null) panel.ReportState = ReportState.Saved;
        }
    }
}
