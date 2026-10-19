using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.App.UI;
using FileAction.Core.Lock;
using Microsoft.Win32;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// Settings &gt; Protection. Rescuer password set/change (explaining that already-protected files keep
/// their old Rescuer until updated), the bulk update, a new Rescuer key, the Recycle-Bin and auto-lock
/// preferences, and removing the file association.
/// </summary>
internal sealed class LockSettingsWindow : Window
{
    private readonly TextBlock _status;
    private readonly TextBlock _busy;

    public LockSettingsWindow()
    {
        LockRuntime.Initialize();
        var body = LockUi.Configure(this, T("tool.lock.settings.title"), 520, 660);
        _status = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 2, 8),
        };
        _busy = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.ProtectBadge,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 8),
        };

        // ---- Rescuer
        var rescuer = new StackPanel();
        rescuer.Children.Add(new TextBlock
        {
            Text = T("tool.lock.settings.rescuerExplained"),
            FontSize = 12,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        var change = Controls.Button(T("tool.lock.settings.changePassword"), ChangeRescuerPassword);
        var newKey = Controls.Button(T("tool.lock.settings.newKey"), CreateNewKey);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(change);
        buttons.Children.Add(new Border { Width = 8 });
        buttons.Children.Add(newKey);
        rescuer.Children.Add(buttons);
        body.Children.Add(LockUi.Card(rescuer));

        // ---- Update existing files
        var update = new StackPanel();
        update.Children.Add(new TextBlock
        {
            Text = T("tool.lock.settings.updateExplained"),
            FontSize = 12,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        update.Children.Add(Controls.Button(T("tool.lock.settings.updateFiles"), UpdateProtectedFiles));
        body.Children.Add(LockUi.Card(update));

        // ---- Auto-lock
        var auto = new StackPanel();
        auto.Children.Add(Controls.Card(T("tool.lock.settings.autoLock"),
            T("tool.lock.settings.autoLockHint"),
            Controls.Toggle(LockRuntime.Settings.AutoLockOnClose, value =>
            {
                LockRuntime.Settings.AutoLockOnClose = value;
                LockRuntime.Settings.Save();
            })));
        auto.Children.Add(Controls.Card(T("tool.lock.settings.grace"),
            T("tool.lock.settings.graceHint"),
            Controls.Slider(AutoLockRules.MinimumGraceSeconds, AutoLockRules.MaximumGraceSeconds,
                LockRuntime.Settings.GraceSeconds, value => LockRuntime.Settings.GraceSeconds = value,
                () => LockRuntime.Settings.Save())));
        body.Children.Add(auto);

        // ---- Recycle bin
        body.Children.Add(Controls.Card(T("tool.lock.settings.recycle"),
            T("tool.lock.settings.recycleHint"),
            Controls.Toggle(LockRuntime.Settings.RecycleContainerAfterUnlock, value =>
            {
                LockRuntime.Settings.RecycleContainerAfterUnlock = value;
                LockRuntime.Settings.Save();
            })));

        // ---- Association
        var association = new StackPanel();
        association.Children.Add(new TextBlock
        {
            Text = T("tool.lock.settings.associationExplained"),
            FontSize = 12,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        association.Children.Add(Controls.Button(T("tool.lock.settings.removeAssociation"), () =>
        {
            LockRuntime.RemoveAssociation();
            _status.Text = T("tool.lock.settings.associationRemoved");
        }));
        body.Children.Add(LockUi.Card(association));

        body.Children.Add(_busy);
        body.Children.Add(_status);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.settings.footer"),
            FontSize = 11,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 6, 2, 0),
        });
        var close = Controls.Button(T("action.close"), Close);
        body.Children.Add(new Border { Margin = new Thickness(0, 14, 0, 0), Child = close });
        RefreshStatus();
    }

    public static void ShowSingle()
    {
        var window = new LockSettingsWindow();
        window.Show();
        window.Activate();
    }

    private void RefreshStatus()
    {
        var stored = LockRuntime.Vault.Load();
        if (stored is null)
        {
            _status.Text = T("tool.lock.settings.rescuerMissing");
            _status.Foreground = Theme.ProtectBadge;
            return;
        }
        _status.Text = string.IsNullOrEmpty(stored.Password)
            ? T("tool.lock.settings.rescuerNoPassword", Bidi.Value(RescuerKey.Format(stored.Key)))
            : T("tool.lock.settings.rescuerReady", Bidi.Value(RescuerKey.Format(stored.Key)));
        _status.Foreground = Theme.Muted;
    }

    private void ChangeRescuerPassword()
    {
        var window = new RescuerSetupWindow(RescuerSetupMode.ResetPassword) { Owner = this };
        if (window.ShowDialog() != true)
            return;
        RefreshStatus();
        _status.Text = T("tool.lock.settings.passwordChanged");
        _busy.Text = T("tool.lock.settings.updateReminder");
    }

    private void CreateNewKey()
    {
        var old = LockRuntime.Vault.Load();
        var window = new RescuerSetupWindow(RescuerSetupMode.NewKey) { Owner = this };
        if (window.ShowDialog() != true || window.Result is null)
            return;
        RefreshStatus();
        if (old is null)
            return;
        if (Dialogs.Show(this, T("tool.lock.settings.updateNowAfterKey"), Title, MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var folder = PickFolder();
        if (folder is null)
            return;
        var newKey = window.Result.Key;
        RunUpdate(EnumerateProtected(folder), secret => LockRuntime.Service.SetRescuerKey(secret, old.Key, newKey),
            T("tool.lock.settings.keysUpdated"));
    }

    private void UpdateProtectedFiles()
    {
        var folder = PickFolder();
        if (folder is null)
            return;
        var files = EnumerateProtected(folder);
        if (files.Count == 0)
        {
            _status.Text = T("tool.lock.settings.noneFound");
            return;
        }

        var setup = new RescuerSetupWindow(RescuerSetupMode.ResetPassword) { Owner = this };
        if (setup.ShowDialog() != true || setup.Result is null)
            return;
        var newPassword = setup.Result.Password!;
        var prompt = new SecretPromptWindow(T("tool.lock.settings.currentSecret"),
            T("tool.lock.settings.currentSecretHint")) { Owner = this };
        if (prompt.ShowDialog() != true || string.IsNullOrEmpty(prompt.Secret))
            return;
        var current = prompt.Secret;
        RefreshStatus();
        RunUpdate(files, path => LockRuntime.Service.SetRescuerPassword(path, current, newPassword),
            T("tool.lock.settings.updated"));
    }

    private void RunUpdate(IReadOnlyList<string> files, Action<string> update, string doneText)
    {
        if (files.Count == 0)
        {
            _status.Text = T("tool.lock.settings.noneFound");
            return;
        }
        _busy.Text = T("tool.lock.settings.working", 0, files.Count);
        var progress = new Progress<int>(done => _busy.Text = T("tool.lock.settings.working", done, files.Count));
        _ = Task.Run(() =>
        {
            var failures = 0;
            var done = 0;
            foreach (var file in files)
            {
                try
                {
                    update(file);
                }
                catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                               or LockPolicyException or ArgumentException)
                {
                    failures++;
                }
                ((IProgress<int>)progress).Report(++done);
            }
            Dispatcher.BeginInvoke((Action)(() =>
            {
                _busy.Text = string.Empty;
                _status.Text = failures == 0 ? doneText : T("tool.lock.settings.partial", done - failures, failures);
                _status.Foreground = failures == 0 ? Theme.Success : Theme.Error;
            }));
        });
    }

    private string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = T("tool.lock.settings.pickFolder") };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private static List<string> EnumerateProtected(string folder)
    {
        var files = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (LockFormat.IsProtectedName(file))
                    files.Add(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return files;
    }
}

/// <summary>A neutral one-field prompt for "current Rescuer password or key".</summary>
internal sealed class SecretPromptWindow : Window
{
    private readonly SecretField _secret;

    public SecretPromptWindow(string title, string hint)
    {
        var body = LockUi.Configure(this, title, 430, 250);
        body.Children.Add(new TextBlock
        {
            Text = hint,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 0, 0, 10),
        });
        _secret = new SecretField(title);
        body.Children.Add(_secret);
        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Controls.Button(T("tool.lock.cancel"), Close);
        var ok = Controls.Button(T("action.ok"), () =>
        {
            if (string.IsNullOrEmpty(_secret.Text))
                return;
            DialogResult = true;
            Close();
        }, primary: true);
        DockPanel.SetDock(cancel, Dock.Left);
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && !string.IsNullOrEmpty(_secret.Text))
            {
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        };
        Closing += (_, _) => _secret.HideReveal();
        Loaded += (_, _) => _secret.FocusInput();
    }

    public string Secret => _secret.Text;
}
