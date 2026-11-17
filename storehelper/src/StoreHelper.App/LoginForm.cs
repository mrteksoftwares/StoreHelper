using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using StoreHelper.Core.Kernel;

namespace StoreHelper.App;

/// <summary>Kernel adresi + kullanıcı adı/şifre (yönetim paneli hesabı) ile giriş.</summary>
public sealed class LoginForm : XtraForm
{
    readonly AppSession _session;
    readonly TextEdit _url = new TextEdit();
    readonly TextEdit _user = new TextEdit();
    readonly TextEdit _password = new TextEdit();
    readonly SimpleButton _login = new SimpleButton();
    readonly LabelControl _error = new LabelControl();

    public LoginForm(AppSession session)
    {
        _session = session;
        Text = "StoreHelper — Kernel'e bağlan";
        this.WithIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 372);

        var title = new LabelControl { Text = "NemraPOS StoreHelper", Location = new Point(24, 18) };
        title.Appearance.Font = new Font("Segoe UI Semibold", 15f);
        var sub = new LabelControl { Text = "Raf etiketi ve terazi aktarımı", Location = new Point(24, 48) };
        sub.Appearance.ForeColor = Color.Gray;

        AddRow("Kernel adresi", _url, 84);
        AddRow("Kullanıcı adı", _user, 156);
        AddRow("Şifre", _password, 228);
        _url.Text = string.IsNullOrEmpty(session.KernelUrl) ? KernelClient.DefaultUrl : session.KernelUrl;
        _url.Properties.NullValuePrompt = KernelClient.DefaultUrl;
        _user.Text = session.LastUser;
        _password.Properties.PasswordChar = '●';

        _error.Location = new Point(24, 296);
        _error.AutoSizeMode = LabelAutoSizeMode.None;
        _error.Size = new Size(372, 20);
        _error.Appearance.ForeColor = Color.Firebrick;
        _error.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;

        _login.Text = "Giriş yap";
        _login.Appearance.Font = new Font("Segoe UI Semibold", 9.5f);
        _login.Location = new Point(24, 326);
        _login.Size = new Size(372, 30);
        _login.Click += async (s, e) => await DoLoginAsync();
        AcceptButton = null;

        Controls.AddRange(new Control[] { title, sub, _error, _login });
        foreach (var edit in new[] { _url, _user, _password })
            edit.KeyDown += async (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                if (edit == _password) await DoLoginAsync();
                else SelectNextControl(edit, true, true, true, true);
            };
        Shown += (s, e) => (string.IsNullOrEmpty(_session.LastUser) ? _user : _password).Focus();
    }

    void AddRow(string caption, TextEdit edit, int top)
    {
        Controls.Add(new LabelControl { Text = caption, Location = new Point(24, top) });
        edit.Location = new Point(24, top + 24);
        edit.Size = new Size(372, 30);
        Controls.Add(edit);
    }

    async System.Threading.Tasks.Task DoLoginAsync()
    {
        if (!_login.Enabled) return;
        _error.Text = "";
        if (string.IsNullOrWhiteSpace(_user.Text) || string.IsNullOrEmpty(_password.Text)) { _error.Text = "Kullanıcı adı ve şifreyi girin."; return; }
        _login.Enabled = false;
        _login.Text = "Bağlanılıyor…";
        try
        {
            await _session.LoginAsync(_url.Text, _user.Text.Trim(), _password.Text);
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException ex) { _error.Text = ex.Message; }
        catch (UriFormatException) { _error.Text = "Kernel adresi geçersiz. Örnek: http://192.168.1.10:4750"; }
        catch (KernelException ex) { _error.Text = ex.Message; }
        finally
        {
            _login.Enabled = true;
            _login.Text = "Giriş yap";
        }
    }
}
