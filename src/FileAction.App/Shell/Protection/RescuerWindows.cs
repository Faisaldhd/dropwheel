using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FileAction.App.UI;
using FileAction.Core.Lock;
using Microsoft.Win32;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell.Protection;

internal static class RescuerSetupPolicy
{
    /// <summary>First-time setup and a password reset create a password pair; a new key does not.</summary>
    public static bool NeedsPasswordPair(RescuerSetupMode mode) =>
        mode is RescuerSetupMode.FirstTime or RescuerSetupMode.ResetPassword;

    /// <summary>A key is shown once and must be acknowledged before the dialog can close.</summary>
    public static bool NeedsKeyConfirmation(RescuerSetupMode mode) =>
        mode is RescuerSetupMode.FirstTime or RescuerSetupMode.NewKey;
}

internal enum RescuerSetupMode
{
    /// <summary>No Rescuer exists yet: choose a password, then see the key once.</summary>
    FirstTime,

    /// <summary>Forgot the Rescuer password: choose a new one (the key is unchanged).</summary>
    ResetPassword,

    /// <summary>Replace the Rescuer key; the current password or key proves ownership.</summary>
    NewKey,
}

/// <summary>
/// The Rescuer setup and recovery dialogs. Every path shows the key exactly once, with Copy, Save .txt and
/// Print, and the Finish button stays disabled until "I saved it" is ticked — the key cannot be recovered.
/// </summary>
internal sealed class RescuerSetupWindow : Window
{
    private readonly RescuerSetupMode _mode;
    private readonly SecretField _password;
    private readonly SecretField _confirm;
    private readonly SecretField _current;
    private readonly StrengthMeter _strength;
    private readonly StackPanel _firstPage;
    private readonly StackPanel _keyPage;
    private readonly TextBox _keyBox;
    private readonly Border _finish;
    private readonly Border _next;
    private readonly TextBlock _match;
    private bool _copied;
    private bool _allowClose;
    private bool _committing;
    private string _key = string.Empty;
    private string _keyPassword = string.Empty;
    private bool _keyUsed;
    private readonly string? _knownKey;
    private Func<RescuerSecrets, string?>? _commit;

    /// <param name="knownKey">The key a reset keeps, when no vault key is available.</param>
    /// <param name="verifiedPassword">
    /// A password Protection settings already proved. Supplying it for <see cref="RescuerSetupMode.NewKey"/>
    /// skips the proof page and goes straight to the freshly generated key.
    /// </param>
    /// <param name="commit">
    /// Optional transactional save used by Protection settings: it runs before the window may close and
    /// returns null on success or a user-facing warning. The first-lock adoption and key rotation use this
    /// to persist containers first; when it fails the dialog stays open and nothing is reported as saved.
    /// </param>
    public RescuerSetupWindow(RescuerSetupMode mode, string? knownKey = null, string? verifiedPassword = null,
        Func<RescuerSecrets, string?>? commit = null)
    {
        _mode = mode;
        _knownKey = knownKey;
        _commit = commit;
        var title = mode switch
        {
            RescuerSetupMode.FirstTime => T("tool.lock.rescuer.title"),
            RescuerSetupMode.ResetPassword => T("tool.lock.rescuer.resetTitle"),
            _ => T("tool.lock.rescuer.newKeyTitle"),
        };
        var body = LockUi.Configure(this, title, 460, mode == RescuerSetupMode.FirstTime ? 560 : 470);

        _firstPage = new StackPanel();
        _keyPage = new StackPanel { Visibility = Visibility.Collapsed };

        _current = new SecretField(T("tool.lock.rescuer.currentSecret"));
        if (mode == RescuerSetupMode.NewKey && verifiedPassword is null)
        {
            var currentCard = new StackPanel();
            currentCard.Children.Add(new TextBlock
            {
                Text = T("tool.lock.rescuer.newKeyIntro"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = Theme.LockMuted,
                Margin = new Thickness(0, 0, 0, 10),
            });
            currentCard.Children.Add(new TextBlock
            {
                Text = T("tool.lock.rescuer.currentSecret"),
                FontSize = 12.5,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 0, 0, 5),
            });
            currentCard.Children.Add(_current);
            _firstPage.Children.Add(LockUi.Card(currentCard));
        }
        else
        {
            _current.Visibility = Visibility.Collapsed;
        }

        _password = new SecretField(T("tool.lock.rescuer.password"));
        _confirm = new SecretField(T("tool.lock.rescuer.confirm"));
        _strength = new StrengthMeter();
        _password.Changed += () =>
        {
            _strength.Update(_password.Text);
            UpdatePair();
        };
        _confirm.Changed += UpdatePair;
        _current.Changed += UpdatePair;

        var passwordCard = new StackPanel();
        passwordCard.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.intro"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 0, 0, 10),
        });
        passwordCard.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.password"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 5),
        });
        passwordCard.Children.Add(_password);
        passwordCard.Children.Add(_strength);
        passwordCard.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.confirm"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 10, 0, 5),
        });
        passwordCard.Children.Add(_confirm);
        _match = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.LockMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Text = T("tool.lock.gateHint"),
        };
        passwordCard.Children.Add(_match);
        passwordCard.Visibility = mode == RescuerSetupMode.NewKey ? Visibility.Collapsed : Visibility.Visible;
        _firstPage.Children.Add(LockUi.Card(passwordCard));

        var buttons = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var cancel = Controls.Button(T("tool.lock.cancel"), () =>
        {
            _allowClose = true;
            Close();
        });
        _next = Controls.Button(T("tool.lock.rescuer.next"), OnNext, primary: true);
        _next.IsEnabled = false;
        DockPanel.SetDock(cancel, Dock.Left);
        DockPanel.SetDock(_next, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_next);
        _firstPage.Children.Add(buttons);
        body.Children.Add(_firstPage);

        // The one-time key page.
        _keyPage.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.keyIntro"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 0, 0, 10),
        });
        _keyBox = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            FontSize = 19,
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(12, 12, 12, 12),
            Background = Theme.Card,
            Foreground = Theme.LockBadgeText,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
        };
        _keyPage.Children.Add(LockUi.Card(_keyBox));

        var keyActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        keyActions.Children.Add(Controls.Button(T("tool.lock.rescuer.copy"), CopyKey, glyph: "\uE8C8"));
        keyActions.Children.Add(new Border { Width = 8 });
        keyActions.Children.Add(Controls.Button(T("tool.lock.rescuer.save"), SaveKey, glyph: "\uE78C"));
        keyActions.Children.Add(new Border { Width = 8 });
        keyActions.Children.Add(Controls.Button(T("tool.lock.rescuer.print"), PrintKey, glyph: "\uE749"));
        _keyPage.Children.Add(keyActions);

        var keyButtons = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        _finish = Controls.Button(T("tool.lock.rescuer.finish"), Finish, primary: true);
        var keyCancel = Controls.Button(T("tool.lock.cancel"), () =>
        {
            _allowClose = true;
            Close();
        });
        DockPanel.SetDock(keyCancel, Dock.Left);
        DockPanel.SetDock(_finish, Dock.Right);
        keyButtons.Children.Add(keyCancel);
        keyButtons.Children.Add(_finish);
        _keyPage.Children.Add(keyButtons);
        body.Children.Add(_keyPage);

        if (mode == RescuerSetupMode.ResetPassword)
            _keyPage.Visibility = Visibility.Collapsed;
        if (mode == RescuerSetupMode.NewKey && verifiedPassword is not null)
        {
            // Protection settings already proved the current password. Generate the one-time key now and
            // land the user directly on the only page that matters: the key itself.
            _key = RescuerKey.Generate();
            _keyPassword = verifiedPassword;
            _keyUsed = false;
            ShowKeyPage();
        }
        UpdatePair();
        Closing += (_, e) =>
        {
            _password.HideReveal();
            _confirm.HideReveal();
            _current.HideReveal();
            if (_allowClose || _keyPage.Visibility != Visibility.Visible)
                return;
            // Copying is an intentional hand-off, so closing afterwards saves the pair like Done does.
            // Before Copy/Done the key is one click from being lost; make the user choose explicitly.
            if (_copied)
            {
                var pair = new RescuerSecrets(_key, _keyPassword, _keyUsed);
                var problem = Commit(pair);
                if (problem is not null)
                {
                    Dialogs.Show(this, problem, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    e.Cancel = true;
                    return;
                }
                Result = pair;
                _allowClose = true;
                return;
            }
            var discard = Dialogs.Show(this, T("tool.lock.rescuer.discardBody"),
                T("tool.lock.rescuer.discardTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (discard != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _allowClose = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_keyPage.Visibility == Visibility.Visible)
                    Finish();
                else if (_next.IsEnabled)
                    OnNext();
            }
        };
        Loaded += (_, _) =>
        {
            if (_keyPage.Visibility == Visibility.Visible)
                _keyBox.Focus();
            else if (mode != RescuerSetupMode.NewKey)
                _password.FocusInput();
            else
                _current.FocusInput();
        };
    }

    private void UpdatePair()
    {
        if (_mode == RescuerSetupMode.NewKey)
        {
            _next.IsEnabled = !string.IsNullOrWhiteSpace(_current.Text);
            return;
        }
        var status = LockSecrets.CheckPair(_password.Text, _confirm.Text);
        _match.Text = status switch
        {
            LockSecrets.PairStatus.Mismatch => T("tool.lock.mismatch"),
            LockSecrets.PairStatus.Match => T("tool.lock.match"),
            _ => T("tool.lock.gateHint"),
        };
        _match.Foreground = status switch
        {
            LockSecrets.PairStatus.Mismatch => Theme.BadgeErrorText,
            LockSecrets.PairStatus.Match => Theme.BadgeSuccessText,
            _ => Theme.LockMuted,
        };
        _next.IsEnabled = status == LockSecrets.PairStatus.Match;
    }

    public RescuerSecrets? Result { get; private set; }

    /// <summary>Transactional save installed by the caller before <see cref="Window.ShowDialog"/>.</summary>
    public Func<RescuerSecrets, string?>? CommitHandler
    {
        get => _commit;
        set => _commit = value;
    }

    private void OnNext()
    {
        if (_mode == RescuerSetupMode.NewKey)
        {
            var stored = LockRuntime.Vault.Load();
            if (stored is null)
            {
                Dialogs.Show(this, T("tool.lock.rescuer.missing"), Title, MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            var typed = _current.Text.Trim();
            if (!RescuerPolicy.TryMatch(stored, typed, out _))
            {
                Dialogs.Show(this, T("tool.lock.error.wrongSecret"), Title, MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            _key = RescuerKey.Generate();
            _keyPassword = stored.Password ?? string.Empty;
            _keyUsed = false;
            ShowKeyPage();
            return;
        }

        var password = _password.Text;
        if (!LockSecrets.IsAcceptablePassword(password))
        {
            Dialogs.Show(this, T("tool.lock.error.short"), Title, MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (!string.Equals(password, _confirm.Text, StringComparison.Ordinal))
        {
            Dialogs.Show(this, T("tool.lock.error.mismatch"), Title, MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string key;
        if (_mode == RescuerSetupMode.ResetPassword)
        {
            // The key may come from a just-opened container (_knownKey) when the vault has no password
            // yet, so a successful unlock can reset the Rescuer password without a second prompt.
            var stored = LockRuntime.Vault.Load();
            key = LockSecrets.ChooseResetKey(_knownKey, stored) ?? string.Empty;
            if (string.IsNullOrEmpty(key))
            {
                Dialogs.Show(this, T("tool.lock.rescuer.missing"), Title, MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            // A password change keeps the same key, so a key that already opened a file stays "used".
            _keyUsed = stored?.KeyUsed == true;
        }
        else
        {
            key = RescuerKey.Generate();
            _keyUsed = false;
        }

        _key = key;
        _keyPassword = password;
        if (_mode == RescuerSetupMode.ResetPassword)
        {
            // No key to show: save immediately. A failed transactional save keeps this dialog open with
            // its fields intact; nothing is marked configured and no reset dialog has changed the vault.
            var pair = new RescuerSecrets(key, password, _keyUsed);
            var problem = Commit(pair);
            if (problem is not null)
            {
                Dialogs.Show(this, problem, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = pair;
            DialogResult = true;
            Close();
            return;
        }
        ShowKeyPage();
    }

    private void ShowKeyPage()
    {
        _keyBox.Text = RescuerKey.Format(_key);
        _firstPage.Visibility = Visibility.Collapsed;
        _keyPage.Visibility = Visibility.Visible;
        Title = T("tool.lock.rescuer.keyTitle");
        _password.Clear();
        _confirm.Clear();
        _copied = false;
        _keyBox.Focus();
    }

    /// <summary>
    /// Runs the transactional save, or the normal vault save. Returning a message means the window must
    /// stay open: nothing was persisted and no caller may treat the Result as active.
    /// </summary>
    private string? Commit(RescuerSecrets pair)
    {
        if (_committing)
            return T("tool.lock.settings.saveFailed");
        _committing = true;
        try
        {
            if (_commit is not null)
                return _commit(pair);
            return LockRuntime.SaveRescuer(pair) ? null : T("tool.lock.settings.saveFailed");
        }
        finally
        {
            _committing = false;
        }
    }

    private void Finish()
    {
        var pair = new RescuerSecrets(_key, _keyPassword, _keyUsed);
        var problem = Commit(pair);
        if (problem is not null)
        {
            Dialogs.Show(this, problem, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = pair;
        _allowClose = true;
        DialogResult = true;
        Close();
    }

    private void CopyKey()
    {
        ClipboardGuard.Copy(RescuerKey.Format(_key));
        _copied = true;
    }

    private void SaveKey()
    {
        var dialog = new SaveFileDialog
        {
            Title = T("tool.lock.rescuer.saveTitle"),
            FileName = "DropWheel-rescuer-key.txt",
            Filter = "Text (*.txt)|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        var text = T("tool.lock.rescuer.fileHeader") + Environment.NewLine + Environment.NewLine +
                   RescuerKey.Format(_key) + Environment.NewLine + Environment.NewLine +
                   T("tool.lock.rescuer.fileWarning") + Environment.NewLine;
        File.WriteAllText(dialog.FileName, text, System.Text.Encoding.UTF8);
        _copied = true;
        Dialogs.Show(this, T("tool.lock.rescuer.savedFile", Bidi.Value(dialog.FileName)), Title,
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PrintKey()
    {
        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;
            var document = new FlowDocument
            {
                PagePadding = new Thickness(60),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
            };
            document.Blocks.Add(new Paragraph(new Run(T("tool.lock.rescuer.keyTitle"))) { FontSize = 20 });
            document.Blocks.Add(new Paragraph(new Run(RescuerKey.Format(_key))) { FontSize = 26 });
            document.Blocks.Add(new Paragraph(new Run(T("tool.lock.rescuer.fileWarning"))));
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, Title);
            _copied = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Printing.PrintQueueException)
        {
            Dialogs.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}


/// <summary>
/// Re-authentication for a destructive Rescuer change: one current Rescuer password <em>or</em> key, proved
/// against a real container before the caller may open a reset dialog. The dialog cannot be completed by a
/// format-valid typo, and a cancel returns no secret and changes nothing.
/// </summary>
internal sealed class RescuerProofWindow : Window
{
    private readonly SecretField _secret;
    private readonly TextBlock _status;
    private readonly Border _ok;
    private readonly IReadOnlyList<string> _verifiers;
    private bool _checking;

    public RescuerProofWindow(IReadOnlyList<string> verifiers)
    {
        _verifiers = verifiers ?? throw new ArgumentNullException(nameof(verifiers));
        var body = LockUi.Configure(this, T("tool.lock.settings.updateFiles"), 450, 320);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.settings.currentSecretHint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 0, 0, 10),
        });
        _secret = new SecretField(T("tool.lock.settings.currentSecret"));
        body.Children.Add(_secret);
        _status = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.BadgeErrorText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        body.Children.Add(_status);
        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Controls.Button(T("tool.lock.cancel"), Close);
        _ok = Controls.Button(T("tool.lock.rescuer.next"), Continue, primary: true);
        DockPanel.SetDock(cancel, Dock.Left);
        DockPanel.SetDock(_ok, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);
        body.Children.Add(buttons);
        _secret.Changed += () => _status.Text = string.Empty;
        Closing += (_, _) => _secret.HideReveal();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Continue();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => _secret.FocusInput();
    }

    /// <summary>The verified secret; meaningful only after <see cref="Window.DialogResult"/> is true.</summary>
    public string Secret { get; private set; } = string.Empty;

    /// <summary>The slot kind that proved the secret (Rescuer password or Rescuer key).</summary>
    public LockSecretKind Kind { get; private set; }

    /// <summary>The container that provided the proof, or null when none could be read.</summary>
    public string? ProofContainer { get; private set; }

    private async void Continue()
    {
        if (_checking)
            return;
        var typed = _secret.Text;
        if (string.IsNullOrWhiteSpace(typed))
            return;
        _checking = true;
        _ok.IsEnabled = false;
        _status.Foreground = Theme.LockMuted;
        _status.Text = T("tool.lock.rescuer.sessionVerifying");
        (string Path, LockSecretKind Kind)? proof;
        try
        {
            // The unwrap is PBKDF2 and must not freeze the dialog.
            proof = await Task.Run(() => LockVerification.FindOpeningContainerWithRescuerSecret(_verifiers, typed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LockException
                                       or ArgumentException)
        {
            proof = null;
        }
        finally
        {
            _checking = false;
            _ok.IsEnabled = true;
        }
        if (!IsVisible)
            return;
        if (proof is null)
        {
            _status.Foreground = Theme.BadgeErrorText;
            _status.Text = T("tool.lock.rescuer.sessionVerifyFailed",
                Bidi.Value(Path.GetFileName(_verifiers[0])));
            return;
        }
        Secret = typed;
        Kind = proof.Value.Kind;
        ProofContainer = proof.Value.Path;
        DialogResult = true;
        Close();
    }
}

/// <summary>
/// The manual fallback shown when a Rescuer marker exists but the DPAPI vault cannot be read (a new Windows
/// profile, a failed protection, or an upgrade). It asks for both doors once and proves them against a real
/// container; the successful pair is then saved through the same vault the normal path uses.
/// </summary>
internal sealed class RescuerSessionWindow : Window
{
    private readonly SecretField _password;
    private readonly SecretField _key;
    private readonly TextBlock _error;
    private readonly Border _ok;
    private readonly IReadOnlyList<string> _verifiers;
    private readonly CheckBox? _noFilesAck;
    private bool _checking;

    /// <param name="verifiers">
    /// Existing containers whose Rescuer slots must open with the typed values. Empty when no protected
    /// file is known yet; the post-write slot check still fails closed in that case.
    /// </param>
    public RescuerSessionWindow(IReadOnlyList<string>? verifiers = null)
    {
        _verifiers = verifiers ?? Array.Empty<string>();
        var body = LockUi.Configure(this, T("tool.lock.rescuer.sessionTitle"), 450,
            _verifiers.Count == 0 ? 460 : 390);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.sessionIntro"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 0, 0, 10),
        });
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.password"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 5),
        });
        _password = new SecretField(T("tool.lock.rescuer.password"));
        body.Children.Add(_password);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.sessionKey"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 12, 0, 5),
        });
        _key = new SecretField(T("tool.lock.rescuer.sessionKey"));
        body.Children.Add(_key);
        _error = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.BadgeErrorText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        body.Children.Add(_error);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.sessionNote"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 10, 0, 0),
        });
        if (_verifiers.Count == 0)
        {
            // There is no real container to prove the pair against. Never save an unverified pair without
            // this explicit acknowledgement that this is a new installation.
            _noFilesAck = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = T("tool.lock.rescuer.noFilesAck"),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = Theme.Text,
                    MaxWidth = 380,
                },
                IsChecked = false,
                Margin = new Thickness(0, 10, 0, 0),
            };
            body.Children.Add(_noFilesAck);
        }

        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Controls.Button(T("tool.lock.cancel"), Close);
        _ok = Controls.Button(T("tool.lock.rescuer.next"), Continue, primary: true);
        DockPanel.SetDock(cancel, Dock.Left);
        DockPanel.SetDock(_ok, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);
        body.Children.Add(buttons);
        Closing += (_, _) =>
        {
            _password.HideReveal();
            _key.HideReveal();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Continue();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => _password.FocusInput();
    }

    public string Password => _password.Text;

    public string RecoveryKey { get; private set; } = string.Empty;

    private async void Continue()
    {
        if (_checking)
            return;
        _error.Foreground = Theme.BadgeErrorText;
        if (!LockSecrets.IsAcceptablePassword(_password.Text))
        {
            _error.Text = T("tool.lock.error.short");
            return;
        }
        if (!RescuerKey.TryParse(_key.Text, out var canonical))
        {
            _error.Text = T("tool.lock.rescuer.sessionInvalid");
            return;
        }
        if (_verifiers.Count == 0 && _noFilesAck?.IsChecked != true)
        {
            _error.Text = T("tool.lock.rescuer.noFilesAck");
            return;
        }

        if (_verifiers.Count > 0)
        {
            var password = _password.Text;
            _checking = true;
            _ok.IsEnabled = false;
            _error.Foreground = Theme.LockMuted;
            _error.Text = T("tool.lock.rescuer.sessionVerifying");
            string? opened;
            try
            {
                // The unwrap is PBKDF2 and must not freeze the dialog.
                opened = await Task.Run(() => LockVerification.FindOpeningContainer(_verifiers, password,
                    canonical));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LockException
                                           or ArgumentException)
            {
                opened = null;
            }
            finally
            {
                _checking = false;
                _ok.IsEnabled = true;
            }
            if (!IsVisible)
                return;
            if (opened is null)
            {
                _error.Foreground = Theme.BadgeErrorText;
                _error.Text = T("tool.lock.rescuer.sessionVerifyFailed",
                    Bidi.Value(Path.GetFileName(_verifiers[0])));
                return;
            }
            _error.Text = string.Empty;
        }

        RecoveryKey = canonical;
        DialogResult = true;
        Close();
    }
}
/// <summary>Asks for the 24-character recovery key alone (key-only Rescuer password reset).</summary>
internal sealed class RescuerKeyWindow : Window
{
    private readonly SecretField _key;
    private readonly TextBlock _error;

    public RescuerKeyWindow()
    {
        var body = LockUi.Configure(this, T("tool.lock.rescuer.keyOnlyTitle"), 430, 270);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.keyOnlyBody"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 0, 0, 10),
        });
        _key = new SecretField(T("tool.lock.rescuer.sessionKey"));
        body.Children.Add(_key);
        _error = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.BadgeErrorText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        body.Children.Add(_error);
        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Controls.Button(T("tool.lock.cancel"), Close);
        var ok = Controls.Button(T("tool.lock.rescuer.next"), Continue, primary: true);
        DockPanel.SetDock(cancel, Dock.Left);
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);
        Closing += (_, _) => _key.HideReveal();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Continue();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => _key.FocusInput();
    }

    public string RecoveryKey { get; private set; } = string.Empty;

    private void Continue()
    {
        if (!RescuerKey.TryParse(_key.Text, out var canonical))
        {
            _error.Text = T("tool.lock.rescuer.sessionInvalid");
            return;
        }
        RecoveryKey = canonical;
        DialogResult = true;
        Close();
    }
}
/// <summary>Asks for the existing Rescuer password when the vault has the key but not the password.</summary>
internal sealed class RescuerPasswordWindow : Window
{
    private readonly SecretField _password;

    public RescuerPasswordWindow(string key)
    {
        var body = LockUi.Configure(this, T("tool.lock.rescuer.passwordTitle"), 420, 250);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.passwordIntro"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 0, 0, 10),
        });
        _password = new SecretField(T("tool.lock.rescuer.password"));
        body.Children.Add(_password);
        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Controls.Button(T("tool.lock.cancel"), Close);
        var ok = Controls.Button(T("tool.lock.rescuer.next"), () =>
        {
            if (!LockSecrets.IsAcceptablePassword(_password.Text))
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
            else if (e.Key == Key.Enter && LockSecrets.IsAcceptablePassword(_password.Text))
            {
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        };
        Closing += (_, _) => _password.HideReveal();
        Loaded += (_, _) => _password.FocusInput();
    }

    public string Password => _password.Text;
}

/// <summary>The only clipboard use in the feature: the one-time key, cleared after 60 seconds.</summary>
internal static class ClipboardGuard
{
    private static DispatcherTimer? _timer;
    private static string? _text;

    public static void Copy(string text)
    {
        _text = text;
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return;
        }
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) =>
        {
            _timer?.Stop();
            try
            {
                if (_text is not null && Clipboard.ContainsText() && Clipboard.GetText() == _text)
                    Clipboard.Clear();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
            _text = null;
        };
        _timer.Start();
    }
}
