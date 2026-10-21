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
/// Settings &gt; Protection. The Rescuer is created here after the first Lock (or from this window at any
/// time), a password change is proved by the current password or key, and a new key is only offered after
/// the current one has opened a protected file. The DPAPI persistence status and the manual re-entry
/// fallback are shown in the same card.
/// </summary>
internal sealed class LockSettingsWindow : Window
{
    private readonly TextBlock _status;
    private readonly TextBlock _busy;
    private readonly TextBlock _rescuerState;
    private readonly TextBlock _persistence;
    private readonly Border _createRescuer;
    private readonly Border _changePassword;
    private readonly Border _enterCredentials;
    private readonly Border _newKey;
    private readonly TextBlock _newKeyHint;
    private readonly List<string> _pendingContainers;
    private readonly RescuerSecrets? _provisional;
    private bool _autoSetup;

    public LockSettingsWindow() : this(null, null)
    {
    }

    /// <param name="pendingContainers">
    /// The containers written by the first Lock. They are adopted (both Rescuer slots rewritten) as soon as
    /// the user creates the real Rescuer here; cancelling leaves them openable by their file password.
    /// </param>
    /// <param name="provisional">The temporary pair those containers currently carry.</param>
    internal LockSettingsWindow(IReadOnlyList<string>? pendingContainers, RescuerSecrets? provisional)
    {
        LockRuntime.Initialize();
        _pendingContainers = pendingContainers?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        _provisional = provisional;
        _autoSetup = _pendingContainers.Count > 0 && provisional?.IsComplete == true &&
                     !LockRuntime.Settings.RescuerConfigured;

        var body = LockUi.Configure(this, T("tool.lock.settings.title"), 540, 720);
        _status = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.LockMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 2, 8),
        };
        _busy = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.LockBadgeText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 8),
        };

        // ---- Rescuer
        var rescuer = new StackPanel();
        rescuer.Children.Add(new TextBlock
        {
            Text = T("tool.lock.settings.rescuerExplained"),
            FontSize = 12,
            Foreground = Theme.LockMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        _rescuerState = new TextBlock
        {
            FontSize = 12,
            Foreground = Theme.LockMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        rescuer.Children.Add(_rescuerState);
        _persistence = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.LockMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        rescuer.Children.Add(_persistence);

        var buttons = new WrapPanel();
        _createRescuer = Controls.Button(T("tool.lock.settings.createRescuer"), BeginFirstTimeSetup, primary: true);
        _changePassword = Controls.Button(T("tool.lock.settings.changePassword"), ChangeRescuerPassword);
        _enterCredentials = Controls.Button(T("tool.lock.settings.useCredentials"), EnterCredentials);
        _newKey = Controls.Button(T("tool.lock.settings.newKey"), CreateNewKey);
        foreach (var button in new[] { _createRescuer, _changePassword, _enterCredentials, _newKey })
        {
            button.Margin = new Thickness(0, 0, 8, 6);
            buttons.Children.Add(button);
        }
        rescuer.Children.Add(buttons);
        _newKeyHint = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.LockBadgeText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        rescuer.Children.Add(_newKeyHint);
        body.Children.Add(LockUi.Card(rescuer));

        // ---- Update existing files
        var update = new StackPanel();
        update.Children.Add(new TextBlock
        {
            Text = T("tool.lock.settings.updateExplained"),
            FontSize = 12,
            Foreground = Theme.LockMuted,
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
            Foreground = Theme.LockMuted,
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
            Foreground = Theme.LockMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 6, 2, 0),
        });
        var close = Controls.Button(T("action.close"), Close);
        body.Children.Add(new Border { Margin = new Thickness(0, 14, 0, 0), Child = close });

        Loaded += (_, _) =>
        {
            RefreshState();
            if (!_autoSetup)
                return;
            _autoSetup = false;
            // Let Show() finish before opening the modal setup dialog over this window.
            _ = Dispatcher.BeginInvoke(new Action(BeginFirstTimeSetup),
                System.Windows.Threading.DispatcherPriority.Background);
        };
        Activated += (_, _) => RefreshState();
    }

    public static void ShowSingle()
    {
        var window = new LockSettingsWindow();
        window.Show();
        window.Activate();
    }

    /// <summary>Opens Protection settings (non-modal) right after the first successful Lock.</summary>
    public static void ShowFirstTime(IReadOnlyList<string> containers, RescuerSecrets provisional)
    {
        var window = new LockSettingsWindow(containers, provisional);
        window.Show();
        window.Activate();
    }

    private void RefreshState()
    {
        var stored = LockRuntime.Vault.Load();
        var ready = stored is not null && stored.IsComplete;
        if (ready && !LockRuntime.Settings.RescuerConfigured)
        {
            // A DPAPI vault from an earlier build already proves the Rescuer exists.
            LockRuntime.Settings.RescuerConfigured = true;
            try
            {
                LockRuntime.Settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        var configured = LockRuntime.Settings.RescuerConfigured;

        if (!configured && !ready)
        {
            _rescuerState.Text = T("tool.lock.settings.rescuerMissing");
            _rescuerState.Foreground = Theme.LockBadgeText;
        }
        else if (!ready)
        {
            _rescuerState.Text = T("tool.lock.settings.credentialsMissing");
            _rescuerState.Foreground = Theme.LockBadgeText;
        }
        else
        {
            _rescuerState.Text = T("tool.lock.settings.rescuerReady");
            _rescuerState.Foreground = Theme.BadgeSuccessText;
        }

        _persistence.Text = !ready
            ? string.Empty
            : LockRuntime.RescuerPersists
                ? T("tool.lock.settings.persisted")
                : T("tool.lock.settings.sessionOnly");

        _createRescuer.Visibility = !configured && !ready ? Visibility.Visible : Visibility.Collapsed;
        _changePassword.Visibility = configured || ready ? Visibility.Visible : Visibility.Collapsed;
        _enterCredentials.Visibility = configured && !ready ? Visibility.Visible : Visibility.Collapsed;
        var canRotate = ready && RescuerPolicy.CanCreateNewKey(stored);
        _newKey.Visibility = configured || ready ? Visibility.Visible : Visibility.Collapsed;
        _newKey.IsEnabled = canRotate;
        _newKey.Opacity = canRotate ? 1 : 0.55;
        _newKeyHint.Text = ready && !canRotate ? T("tool.lock.settings.newKeyNeedsUse") : string.Empty;
    }

    private void BeginFirstTimeSetup()
    {
        if (LockRuntime.Settings.RescuerConfigured && LockRuntime.TryGetRescuerCredentials() is not null)
            return;
        var setup = new RescuerSetupWindow(RescuerSetupMode.FirstTime) { Owner = this };
        if (setup.ShowDialog() != true || setup.Result is null)
        {
            _status.Text = T("tool.lock.settings.setupSkipped");
            return;
        }
        RefreshState();
        if (_pendingContainers.Count > 0 && _provisional is not null && _provisional.IsComplete)
            AdoptPendingContainers(setup.Result);
        else
            _status.Text = T("tool.lock.settings.setupDone");
    }

    /// <summary>
    /// Rewrites both Rescuer slots of the files written by the first Lock. The payload and the file password
    /// never change, and a failure leaves that file exactly as it was.
    /// </summary>
    private void AdoptPendingContainers(RescuerSecrets created)
    {
        var containers = _pendingContainers.ToArray();
        var provisional = _provisional!;
        _busy.Text = T("tool.lock.settings.setupWorking", 0, containers.Length);
        var progress = new Progress<(int Done, int Total)>(value =>
            _busy.Text = T("tool.lock.settings.setupWorking", value.Done, value.Total));
        _ = Task.Run(() =>
        {
            var failures = 0;
            var done = 0;
            foreach (var container in containers)
            {
                try
                {
                    LockRuntime.Service.AdoptRescuer(container, provisional.Password!, created);
                }
                catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                               or LockPolicyException or ArgumentException)
                {
                    failures++;
                }
                ((IProgress<(int, int)>)progress).Report((++done, containers.Length));
            }
            Dispatcher.BeginInvoke((Action)(() =>
            {
                _busy.Text = string.Empty;
                if (failures == 0)
                {
                    _status.Text = T("tool.lock.settings.setupApplied");
                    _status.Foreground = Theme.BadgeSuccessText;
                    _pendingContainers.Clear();
                }
                else
                {
                    _status.Text = T("tool.lock.settings.setupPartial", containers.Length - failures,
                        containers.Length);
                    _status.Foreground = Theme.BadgeErrorText;
                }
            }));
        });
    }

    /// <summary>The stored pair, or the manual "Rescuer credentials" fallback when DPAPI has nothing.</summary>
    private RescuerSecrets? EnsureCredentials()
    {
        var stored = LockRuntime.TryGetRescuerCredentials();
        if (stored is not null)
            return stored;
        if (!LockRuntime.Settings.RescuerConfigured)
        {
            BeginFirstTimeSetup();
            return LockRuntime.TryGetRescuerCredentials();
        }
        var prompted = LockRuntime.RequireRescuer(this);
        if (prompted is not null)
            RefreshState();
        return prompted;
    }

    private void EnterCredentials()
    {
        if (EnsureCredentials() is not null)
            _status.Text = T("tool.lock.settings.credentialsSaved");
    }

    private void ChangeRescuerPassword()
    {
        var stored = EnsureCredentials();
        if (stored is null)
            return;
        var proof = new SecretPromptWindow(T("tool.lock.rescuer.currentSecret"),
            T("tool.lock.settings.changePasswordHint"),
            value => RescuerPolicy.TryMatch(stored, value, out _) ? null : T("tool.lock.error.wrongSecret"))
        {
            Owner = this,
        };
        if (proof.ShowDialog() != true)
            return;
        var window = new RescuerSetupWindow(RescuerSetupMode.ResetPassword, stored.Key) { Owner = this };
        if (window.ShowDialog() != true)
            return;
        RefreshState();
        _status.Text = T("tool.lock.settings.passwordChanged");
        _busy.Text = T("tool.lock.settings.updateReminder");
    }

    private void CreateNewKey()
    {
        var stored = EnsureCredentials();
        if (stored is null)
            return;
        if (!RescuerPolicy.CanCreateNewKey(stored))
        {
            _status.Text = T("tool.lock.settings.newKeyNeedsUse");
            return;
        }
        var proof = new SecretPromptWindow(T("tool.lock.settings.newKeyPassword"),
            T("tool.lock.settings.newKeyPasswordHint"),
            value => RescuerPolicy.MatchesPassword(stored, value) ? null : T("tool.lock.error.wrongSecret"))
        {
            Owner = this,
        };
        if (proof.ShowDialog() != true)
            return;
        // The password is proved; this window generates and displays the replacement key exactly once.
        var window = new RescuerSetupWindow(RescuerSetupMode.NewKey, verifiedPassword: stored.Password)
        {
            Owner = this,
        };
        if (window.ShowDialog() != true || window.Result is null)
            return;
        RefreshState();
        _status.Text = T("tool.lock.settings.newKeyCreated");
    }

    private void UpdateProtectedFiles()
    {
        var stored = EnsureCredentials();
        if (stored is null)
            return;
        var folder = PickFolder();
        if (folder is null)
            return;
        var files = EnumerateProtected(folder);
        if (files.Count == 0)
        {
            _status.Text = T("tool.lock.settings.noneFound");
            return;
        }

        var setup = new RescuerSetupWindow(RescuerSetupMode.ResetPassword, stored.Key) { Owner = this };
        if (setup.ShowDialog() != true || setup.Result is null)
            return;
        var newPassword = setup.Result.Password!;
        var prompt = new SecretPromptWindow(T("tool.lock.settings.currentSecret"),
            T("tool.lock.settings.currentSecretHint")) { Owner = this };
        if (prompt.ShowDialog() != true || string.IsNullOrEmpty(prompt.Secret))
            return;
        var current = prompt.Secret;
        RefreshState();
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
                _status.Foreground = failures == 0 ? Theme.BadgeSuccessText : Theme.BadgeErrorText;
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

/// <summary>A neutral one-field prompt for a current Rescuer password or key, with an inline validator.</summary>
internal sealed class SecretPromptWindow : Window
{
    private readonly SecretField _secret;
    private readonly TextBlock _error;
    private readonly Func<string, string?>? _validate;

    public SecretPromptWindow(string title, string hint, Func<string, string?>? validate = null)
    {
        _validate = validate;
        var body = LockUi.Configure(this, title, 430, 290);
        body.Children.Add(new TextBlock
        {
            Text = hint,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 0, 0, 10),
        });
        _secret = new SecretField(title);
        body.Children.Add(_secret);
        _error = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.BadgeErrorText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        _secret.Changed += () => _error.Text = string.Empty;
        body.Children.Add(_error);
        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Controls.Button(T("tool.lock.cancel"), Close);
        var ok = Controls.Button(T("action.ok"), Submit, primary: true);
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
            else if (e.Key == Key.Enter)
            {
                Submit();
                e.Handled = true;
            }
        };
        Closing += (_, _) => _secret.HideReveal();
        Loaded += (_, _) => _secret.FocusInput();
    }

    public string Secret => _secret.Text;

    private void Submit()
    {
        if (string.IsNullOrEmpty(_secret.Text))
            return;
        var problem = _validate?.Invoke(_secret.Text);
        if (!string.IsNullOrEmpty(problem))
        {
            _error.Text = problem;
            return;
        }
        DialogResult = true;
        Close();
    }
}
