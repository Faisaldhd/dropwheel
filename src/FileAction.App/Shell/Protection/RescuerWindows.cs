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
    private readonly CheckBox _saved;
    private readonly Border _finish;
    private readonly Border _next;
    private readonly TextBlock _match;
    private readonly TextBox _typedLast4;
    private bool _confirmed;
    private bool _allowClose;
    private string _key = string.Empty;
    private string _keyPassword = string.Empty;
    private readonly string? _knownKey;

    public RescuerSetupWindow(RescuerSetupMode mode, string? knownKey = null)
    {
        _mode = mode;
        _knownKey = knownKey;
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
        if (mode == RescuerSetupMode.NewKey)
        {
            var currentCard = new StackPanel();
            currentCard.Children.Add(new TextBlock
            {
                Text = T("tool.lock.rescuer.newKeyIntro"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = Theme.Muted,
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
            Foreground = Theme.Muted,
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
            Foreground = Theme.Muted,
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
            Foreground = Theme.Muted,
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
            Foreground = Theme.ProtectBadge,
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

        _saved = new CheckBox
        {
            Content = new TextBlock
            {
                Text = T("tool.lock.rescuer.iSaved"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = Theme.Text,
                MaxWidth = 380,
            },
            IsChecked = false,
            Margin = new Thickness(0, 2, 0, 14),
            FlowDirection = FlowDirection.LeftToRight,
        };
        _keyPage.Children.Add(_saved);

        _keyPage.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.typeLast4"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 5),
        });
        _typedLast4 = new TextBox
        {
            MaxLength = 8,
            Padding = new Thickness(10, 7, 10, 7),
            Background = Theme.Card,
            Foreground = Theme.Text,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 120,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _typedLast4.TextChanged += (_, _) => UpdateConfirmation();
        _keyPage.Children.Add(_typedLast4);

        var keyButtons = new DockPanel();
        _finish = Controls.Button(T("tool.lock.rescuer.finish"), Finish, primary: true);
        _finish.IsEnabled = false;
        _saved.Checked += (_, _) => UpdateConfirmation();
        _saved.Unchecked += (_, _) => UpdateConfirmation();
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
        UpdatePair();
        Closing += (_, e) =>
        {
            _password.HideReveal();
            _confirm.HideReveal();
            _current.HideReveal();
            if (_keyPage.Visibility != Visibility.Visible || _confirmed || _allowClose)
            {
                _allowClose = true;
                return;
            }
            if (_confirmed && Result is null && _key.Length > 0)
            {
                // The user confirmed the key, so closing keeps it instead of silently losing it.
                Result = new RescuerSecrets(_key, _keyPassword);
                LockRuntime.Vault.Save(Result);
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
                {
                    if (_finish.IsEnabled)
                        Finish();
                }
                else if (_next.IsEnabled)
                {
                    OnNext();
                }
            }
        };
        Loaded += (_, _) =>
        {
            if (mode != RescuerSetupMode.NewKey)
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
            LockSecrets.PairStatus.Mismatch => Theme.Error,
            LockSecrets.PairStatus.Match => Theme.Success,
            _ => Theme.Muted,
        };
        _next.IsEnabled = status == LockSecrets.PairStatus.Match;
    }

    private void UpdateConfirmation()
    {
        _confirmed = LockSecrets.IsRecoveryKeyConfirmed(_saved.IsChecked.GetValueOrDefault(),
            _typedLast4.Text, _key);
        _finish.IsEnabled = _confirmed;
    }

    public RescuerSecrets? Result { get; private set; }

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
            var matchesPassword = string.Equals(typed, stored.Password, StringComparison.Ordinal);
            var matchesKey = RescuerKey.TryParse(typed, out var typedKey) &&
                             string.Equals(typedKey, stored.Key, StringComparison.Ordinal);
            if (!matchesPassword && !matchesKey)
            {
                Dialogs.Show(this, T("tool.lock.error.wrongSecret"), Title, MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            _key = RescuerKey.Generate();
            _keyPassword = stored.Password ?? string.Empty;
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
            var stored = LockRuntime.Vault.Load();
            if (stored is null)
            {
                Dialogs.Show(this, T("tool.lock.rescuer.missing"), Title, MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            key = stored.Key;
        }
        else
        {
            key = RescuerKey.Generate();
        }

        _key = key;
        _keyPassword = password;
        if (_mode == RescuerSetupMode.ResetPassword)
        {
            // No key to show: save immediately.
            Result = new RescuerSecrets(key, password);
            LockRuntime.Vault.Save(Result);
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
        _typedLast4.Clear();
        UpdateConfirmation();
        _typedLast4.Focus();
    }

    private void Finish()
    {
        _confirmed = LockSecrets.IsRecoveryKeyConfirmed(_saved.IsChecked.GetValueOrDefault(),
            _typedLast4.Text, _key);
        if (!_confirmed)
        {
            Dialogs.Show(this, T("tool.lock.rescuer.confirmRequired"), Title, MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        _allowClose = true;
        Result = new RescuerSecrets(_key, _keyPassword);
        LockRuntime.Vault.Save(Result);
        DialogResult = true;
        Close();
    }

    private void CopyKey() => ClipboardGuard.Copy(RescuerKey.Format(_key));

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
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Printing.PrintQueueException)
        {
            Dialogs.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}


/// <summary>
/// Asks for the Rescuer password and the recovery key once per session. Neither is stored: the password
/// wraps new file keys for this run, and the recovery key is needed to create the third slot. On the next
/// start the user is asked again, so losing the settings file or the device reveals nothing.
/// </summary>
internal sealed class RescuerSessionWindow : Window
{
    private readonly SecretField _password;
    private readonly SecretField _key;
    private readonly TextBlock _error;

    public RescuerSessionWindow()
    {
        var body = LockUi.Configure(this, T("tool.lock.rescuer.sessionTitle"), 450, 390);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.sessionIntro"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Theme.Muted,
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
            Foreground = Theme.Error,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        body.Children.Add(_error);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.sessionNote"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 10, 0, 0),
        });

        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Controls.Button(T("tool.lock.cancel"), Close);
        var ok = Controls.Button(T("tool.lock.rescuer.next"), Continue, primary: true);
        DockPanel.SetDock(cancel, Dock.Left);
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
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

    private void Continue()
    {
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
            Foreground = Theme.Muted,
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
