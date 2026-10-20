using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.App.Shell.Protection;
using FileAction.App.UI;
using FileAction.App.UI.Tools;
using FileAction.Core;
using FileAction.Core.Lock;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Lock;

/// <summary>
/// "Lock" on the Tools wheel: the highest-risk tool, so it opens a window whose only job is to collect the
/// file password, confirm the Rescuer is set up, and then hand the item to the verified protect pipeline.
/// A protected item opens the unlock view instead. The tool is labelled Beta until the owner signs it off.
/// </summary>
internal sealed class LockTool : IToolWindowSpec
{
    public static readonly LockTool Instance = new();

    /// <summary>The protect window is a fixed 480×460 card; there is no preview to size against.</summary>
    public static readonly ToolSpecLayout LockLayout = new(
        ChromeWidth: 36,
        ChromeHeight: 200,
        PreviewMinWidth: 320,
        PreviewMinHeight: 220,
        PreviewMaxWidth: 640,
        PreviewMaxHeight: 640,
        DefaultPreviewWidth: 444,
        DefaultPreviewHeight: 260);

    public string Id => "tools.lock";

    public string TitleKey => "tool.lock.title";

    public string PrimaryLabelKey => "tool.lock.protect";

    public ToolSpecLayout Layout => LockLayout;

    public bool Applies(IReadOnlyList<FileItem> items)
    {
        if (items.Count == 0)
            return false;
        var existing = items.Where(i => File.Exists(i.Path) || Directory.Exists(i.Path)).ToList();
        if (existing.Count != items.Count)
            return false;
        return existing.All(i => !LockFormat.IsProtectedName(i.Path)) || existing.Count == 1;
    }

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        var protectedItem = items.FirstOrDefault(i => LockFormat.IsProtectedName(i.Path));
        return protectedItem is not null && items.Count == 1
            ? new LockUnlockView(protectedItem, targets).Build()
            : new LockProtectView(items, targets).Build();
    }
}

/// <summary>Registers the tool from its own file, like every other tool.</summary>
internal static class LockToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(LockTool.Instance);
}

/// <summary>"Lock" on the Tools wheel; opens the floating tool instead of running a job.</summary>
internal sealed class LockOperation : Operation, IInteractiveTool
{
    public LockOperation()
        : base("tools.lock", WheelKind.Tools, T("tool.lock.label"), "\uE72E", T("tool.lock.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => LockTool.Instance.Applies(items);

    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        // Open this tool by identity: several tools accept the same files, and a first-match lookup
        // could open another tool's window from the Lock slice.
        new ToolWindow(LockTool.Instance, items, targets).Show();
    }
}

/// <summary>Remembers whether the plain warning was already accepted in this session.</summary>
internal static class LockSessionConsent
{
    public static bool Granted { get; set; }
}

/// <summary>Output folder rules reused from the other tools: detected drop folder, remembered folder, source.</summary>
internal static class LockOutput
{
    public static string DirectoryFor(FileItem item, DropTargets? targets)
    {
        var detected = OutputTarget.Resolve(item.Path, targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        if (!detected.UsesDetectedFolder && targets?.Remembered is { } remembered)
        {
            var fallback = OutputTarget.Resolve(item.Path, remembered, Directory.Exists, OutputTarget.CanWrite);
            if (fallback.UsesDetectedFolder)
                return fallback.Directory!;
        }
        return detected.Directory ?? OutputTarget.SourceFolder(item.Path) ?? Environment.CurrentDirectory;
    }
}

/// <summary>The protect view: password + confirmation + strength meter, Rescuer status and the warning.</summary>
internal sealed class ActionGateGrid : Grid, IToolActionGate, IToolActionLabel
{
    public string PrimaryLabelKey => "tool.lock.protect";

    private bool _canApply;

    public bool CanApply => _canApply;

    public event Action? CanApplyChanged;

    public void SetCanApply(bool value)
    {
        if (value == _canApply)
            return;
        _canApply = value;
        CanApplyChanged?.Invoke();
    }
}

internal sealed class LockProtectView
{
    private readonly IReadOnlyList<FileItem> _items;
    private readonly DropTargets? _targets;
    private readonly SecretField _password;
    private readonly SecretField _confirm;
    private readonly StrengthMeter _strength = new();
    private readonly TextBlock _rescuerStatus;
    private readonly TextBlock _progress;
    private readonly TextBlock _match;
    private readonly ActionGateGrid _root = new();
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LockProtectView(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        _items = items;
        _targets = targets;
        LockRuntime.Initialize();

        var content = new StackPanel();
        content.Children.Add(new System.Windows.Controls.Image
        {
            Source = LockIcons.PreviewForPath(items[0].Path, 48),
            Width = 48,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
        });
        content.Children.Add(new DockPanel
        {
            Margin = new Thickness(0, 0, 0, 10),
            Children =
            {
                LockUi.Pill(T("tool.lock.beta"), Theme.LockBadge, Theme.TextOnAccent),
                new TextBlock
                {
                    Text = items.Count == 1
                        ? Bidi.Value(items[0].Name)
                        : Items(items.Count),
                    FontSize = 13,
                    Foreground = Theme.Text,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(8, 1, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        });

        var passwordCard = new StackPanel();
        passwordCard.Children.Add(new TextBlock
        {
            Text = T("tool.lock.filePassword"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 5),
        });
        _password = new SecretField(T("tool.lock.filePassword"));
        passwordCard.Children.Add(_password);
        passwordCard.Children.Add(_strength);
        passwordCard.Children.Add(new TextBlock
        {
            Text = T("tool.lock.confirm"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 10, 0, 5),
        });
        _confirm = new SecretField(T("tool.lock.confirm"));
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
        _password.Changed += () =>
        {
            _strength.Update(_password.Text);
            UpdatePair();
        };
        _confirm.Changed += UpdatePair;
        content.Children.Add(LockUi.Card(passwordCard));

        var rescuerCard = new DockPanel();
        var rescuerText = new StackPanel();
        rescuerText.Children.Add(new TextBlock
        {
            Text = T("tool.lock.rescuer.row"),
            FontSize = 12.5,
            Foreground = Theme.Text,
        });
        _rescuerStatus = new TextBlock
        {
            Text = T("tool.lock.rescuer.checking"),
            FontSize = 11.5,
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        rescuerText.Children.Add(_rescuerStatus);
        var setup = Controls.Button(T("tool.lock.rescuer.setup"), () =>
        {
            LockRuntime.RequireRescuer(Window.GetWindow(_root));
            RefreshRescuer();
        });
        DockPanel.SetDock(setup, Dock.Right);
        rescuerCard.Children.Add(setup);
        rescuerCard.Children.Add(rescuerText);
        content.Children.Add(LockUi.Card(rescuerCard));

        content.Children.Add(new DockPanel
        {
            Margin = new Thickness(2, 4, 2, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "\uE72E",
                    FontFamily = Theme.Icons,
                    FontSize = 13,
                    Foreground = Theme.LockBadge,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 7, 0),
                },
                new TextBlock
                {
                    Text = T("tool.lock.status"),
                    FontSize = 11.5,
                    Foreground = Theme.Muted,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        });

        _progress = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.Muted,
            Margin = new Thickness(2, 6, 2, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = T("tool.lock.ready"),
        };
        content.Children.Add(_progress);

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _root.Children.Add(scroll);
        _root.Loaded += (_, _) =>
        {
            RefreshRescuer();
            UpdatePair();
            _password.FocusInput();
            _ready.TrySetResult(null);
        };
        _root.Unloaded += (_, _) =>
        {
            _password.HideReveal();
            _confirm.HideReveal();
        };
    }

    public ToolContent Build() => new(_root, Reset, ApplyAsync, _ready.Task, () => Task.CompletedTask);

    private void UpdatePair()
    {
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
        _root.SetCanApply(status == LockSecrets.PairStatus.Match);
    }

    private void RefreshRescuer()
    {
        var stored = LockRuntime.Vault.Load();
        _rescuerStatus.Text = !LockRuntime.Settings.RescuerConfigured
            ? T("tool.lock.rescuer.missing")
            : stored is null || string.IsNullOrEmpty(stored.Password)
                ? T("tool.lock.rescuer.needsPassword")
                : T("tool.lock.rescuer.ready");
        _rescuerStatus.Foreground = stored is null || string.IsNullOrEmpty(stored.Password)
            ? Theme.LockBadge
            : Theme.Success;
    }

    private void Reset()
    {
        _password.Clear();
        _confirm.Clear();
        _strength.Update(string.Empty);
        UpdatePair();
        _progress.Text = T("tool.lock.ready");
        RefreshRescuer();
    }

    private async Task<string?> ApplyAsync()
    {
        var owner = Window.GetWindow(_root);
        if (!LockSecrets.IsAcceptablePassword(_password.Text))
            throw new InvalidOperationException(T("tool.lock.error.short"));
        if (!string.Equals(_password.Text, _confirm.Text, StringComparison.Ordinal))
            throw new InvalidOperationException(T("tool.lock.error.mismatch"));

        var warningNeeded = _items.Count > 1 || _items.Any(i => i.IsDirectory) || !LockSessionConsent.Granted;
        if (warningNeeded && !LockWarningDialog.Ask(owner, _items))
            return null;
        LockSessionConsent.Granted = true;

        var rescuer = LockRuntime.RequireRescuer(owner);
        if (rescuer is null || string.IsNullOrEmpty(rescuer.Password))
            return null;

        var credentials = new LockCredentials
        {
            FilePassword = _password.Text,
            RescuerPassword = rescuer.Password,
            RescuerKey = rescuer.Key,
        };

        var failures = new List<string>();
        var outputs = new List<string>();
        var notes = new List<string>();
        var context = SynchronizationContext.Current;
        var progress = new Progress<LockProgress>(value =>
        {
            if (context is null)
                return;
            context.Post(_ => _progress.Text = value.Stage switch
            {
                LockStage.Encrypting => T("tool.lock.progress.encrypting", value.Done / 1024),
                LockStage.Verifying => T("tool.lock.progress.verifying"),
                LockStage.Finalizing => T("tool.lock.progress.finalizing"),
                _ => T("tool.lock.progress.preparing"),
            }, null);
        });

        foreach (var item in _items)
        {
            try
            {
                var directory = LockOutput.DirectoryFor(item, _targets);
                var icon = item.IsDirectory ? LockRuntime.Icons.FolderIcon : string.Empty;
                var outcome = await Task.Run(() => LockRuntime.Service.Protect(
                    item.Path, directory, credentials, string.IsNullOrEmpty(icon) ? null : icon, progress));
                outputs.Add(outcome.OutputPath);
                if (!outcome.SourceRemoved && outcome.LeftoverSource is not null)
                    notes.Add(T("tool.protect.warn.leftover", Bidi.Value(outcome.LeftoverSource)));
            }
            catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                           or LockPolicyException or ArgumentException)
            {
                failures.Add(T("tool.lock.error.item", Bidi.Value(item.Name), LockRuntime.Describe(ex)));
            }
        }

        LockRuntime.EnsureAssociation();

        if (failures.Count > 0)
        {
            var text = string.Join(Environment.NewLine, failures);
            if (outputs.Count > 0)
                text += Environment.NewLine + T("tool.lock.error.partial", outputs.Count);
            throw new IOException(text);
        }

        if (notes.Count > 0)
            Dialogs.Show(owner, string.Join(Environment.NewLine, notes), T("tool.lock.title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        return outputs.FirstOrDefault();
    }
}

/// <summary>The mandatory warning shown before the first encryption in a session (always for folders/multiple).</summary>
internal sealed class LockWarningDialog : Window
{
    private readonly CheckBox _ack;
    private readonly Border _encrypt;

    private LockWarningDialog(IReadOnlyList<FileItem> items)
    {
        var body = LockUi.Configure(this, T("tool.protect.warn.title"), 520, 540);
        body.Children.Add(new TextBlock
        {
            Text = items.Count == 1 ? Bidi.Value(items[0].Name) : Items(items.Count),
            FontSize = 12,
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 0, 0, 8),
        });
        body.Children.Add(new TextBlock
        {
            Text = T("tool.protect.warn.body"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 20,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 12),
        });
        _ack = new CheckBox
        {
            Content = new TextBlock
            {
                Text = T("tool.protect.warn.checkbox"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = Theme.Text,
                MaxWidth = 430,
            },
            IsChecked = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Thickness(0, 0, 0, 14),
        };
        body.Children.Add(_ack);

        var buttons = new DockPanel();
        var cancel = Controls.Button(T("tool.protect.warn.cancel"), Close);
        _encrypt = Controls.Button(T("tool.protect.warn.protect"), () =>
        {
            DialogResult = true;
            Close();
        }, primary: true);
        _encrypt.IsEnabled = false;
        _ack.Checked += (_, _) => _encrypt.IsEnabled = true;
        _ack.Unchecked += (_, _) => _encrypt.IsEnabled = false;
        DockPanel.SetDock(cancel, Dock.Left);
        DockPanel.SetDock(_encrypt, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_encrypt);
        body.Children.Add(buttons);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _encrypt.IsEnabled)
            {
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => _ack.Focus();
    }

    public static bool Ask(Window? owner, IReadOnlyList<FileItem> items)
    {
        var dialog = new LockWarningDialog(items) { Owner = owner };
        return dialog.ShowDialog() == true;
    }
}

/// <summary>
/// The unlock view for one protected item: any of the three secrets, the recovery ladder behind "I forgot",
/// and a separate, explicit "Remove protection" action with its own confirmation.
/// </summary>
internal sealed class ActionLabelGrid : Grid, IToolActionLabel
{
    public ActionLabelGrid(string primaryLabelKey)
    {
        PrimaryLabelKey = primaryLabelKey;
    }

    public string PrimaryLabelKey { get; }
}

internal sealed class LockUnlockView
{
    private readonly FileItem _item;
    private readonly ActionGateGrid _root = new();
    private readonly SecretField _secret;
    private readonly TextBlock _error;
    private readonly TextBlock _ladder;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _failures;

    public LockUnlockView(FileItem item, DropTargets? targets)
    {
        _item = item;
        LockRuntime.Initialize();
        var content = new StackPanel();
        content.Children.Add(new System.Windows.Controls.Image
        {
            Source = LockIcons.PreviewForPath(item.Path, 48),
            Width = 48,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
        });
        content.Children.Add(new TextBlock
        {
            Text = Bidi.Value(LockPaths.DisplayName(item.Path)),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4),
        });
        content.Children.Add(new TextBlock
        {
            Text = T(LockFormat.IsProtectedFolderName(item.Path)
                ? "tool.lock.unlock.folderHint"
                : "tool.lock.unlock.fileHint"),
            FontSize = 12,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var card = new StackPanel();
        card.Children.Add(new TextBlock
        {
            Text = T("tool.lock.unlock.secret"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 5),
        });
        _secret = new SecretField(T("tool.lock.unlock.secret"));
        card.Children.Add(_secret);
        _error = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.Error,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        _secret.Changed += () => _error.Text = string.Empty;
        card.Children.Add(_error);

        var forgot = new TextBlock
        {
            Text = T("tool.lock.unlock.forgot"),
            FontSize = 12,
            Foreground = Theme.Accent,
            Margin = new Thickness(0, 8, 0, 0),
            Cursor = Cursors.Hand,
        };
        _ladder = new TextBlock
        {
            Text = T("tool.lock.unlock.ladder"),
            FontSize = 11.5,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        forgot.MouseLeftButtonUp += (_, _) =>
            _ladder.Visibility = _ladder.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        card.Children.Add(forgot);
        card.Children.Add(_ladder);
        content.Children.Add(LockUi.Card(card));

        var change = Controls.Button(T("tool.lock.changePassword"), () =>
        {
            var window = new ChangeFilePasswordWindow(_item) { Owner = Window.GetWindow(_root) };
            window.ShowDialog();
        });
        change.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(change);
        var remove = Controls.Button(T("tool.lock.unlock.remove"), RemoveProtection);
        content.Children.Add(remove);
        content.Children.Add(new TextBlock
        {
            Text = T("tool.lock.unlock.removeHint"),
            FontSize = 11.5,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 8, 2, 0),
        });

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _root.Children.Add(scroll);
        _root.Loaded += (_, _) =>
        {
            _secret.FocusInput();
            _ready.TrySetResult(null);
        };
    }

    public ToolContent Build() => new(_root, Reset, OpenAsync, _ready.Task, () => Task.CompletedTask);

    private void Reset()
    {
        _secret.Clear();
        _error.Text = string.Empty;
        _ladder.Visibility = Visibility.Collapsed;
    }

    private async Task<string?> OpenAsync()
    {
        var owner = Window.GetWindow(_root);
        if (string.IsNullOrEmpty(_secret.Text))
            throw new InvalidOperationException(T("tool.lock.unlock.empty"));
        try
        {
            var kind = await LockRuntime.Sessions.OpenAsync(_item.Path, _secret.Text);
            OfferRescuerReset(owner, kind);
            return null;
        }
        catch (LockWrongSecretException)
        {
            _failures++;
            var delay = LockSecrets.DelayAfter(_failures);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);
            throw new InvalidOperationException(LockRuntime.Describe(new LockWrongSecretException()));
        }
        catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                       or LockPolicyException)
        {
            throw new InvalidOperationException(LockRuntime.Describe(ex));
        }
    }

    private void OfferRescuerReset(Window? owner, LockSecretKind kind)
    {
        if (kind != LockSecretKind.RescuerKey)
            return;
        if (Dialogs.Show(owner, T("tool.lock.unlock.resetOffer"), T("tool.lock.rescuer.resetTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var secret = _secret.Text;
        var knownKey = RescuerKey.TryParse(secret, out var parsed) ? parsed : null;
        var window = new RescuerSetupWindow(RescuerSetupMode.ResetPassword, knownKey) { Owner = owner };
        if (window.ShowDialog() != true || window.Result is null)
            return;
        _ = Task.Run(() =>
        {
            try
            {
                LockRuntime.Service.SetRescuerPassword(_item.Path, secret, window.Result.Password!);
            }
            catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException)
            {
                // The offer is optional; the file already opened.
            }
        });
    }

    private void RemoveProtection()
    {
        var owner = Window.GetWindow(_root);
        if (Dialogs.Show(owner, T("tool.lock.removeConfirm.body", Bidi.Value(LockPaths.DisplayName(_item.Path))),
                T("tool.lock.removeConfirm.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
            return;
        try
        {
            var outcome = LockRuntime.Service.Unprotect(_item.Path, _secret.Text,
                LockRuntime.Settings.RecycleContainerAfterUnlock);
            var text = T("tool.lock.removeDone", Bidi.Value(outcome.RestoredPath));
            if (!outcome.ContainerRemoved)
                text += Environment.NewLine + T("tool.lock.removeLeftover", Bidi.Value(outcome.ContainerError ?? ""));
            Dialogs.Show(owner, text, T("tool.lock.title"), MessageBoxButton.OK, MessageBoxImage.Information);
            Reset();
        }
        catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                       or LockPolicyException or ArgumentException)
        {
            _error.Text = LockRuntime.Describe(ex);
        }
    }
}
/// <summary>
/// "Change file password": the current file password or the Rescuer password proves ownership (the
/// Rescuer key alone is refused), and the new password is asked twice with its own eyes and live match
/// status. The rewrite is verified by a full test-open before it replaces the header.
/// </summary>
internal sealed class ChangeFilePasswordWindow : Window
{
    private readonly FileItem _item;
    private readonly SecretField _current;
    private readonly SecretField _password;
    private readonly SecretField _confirm;
    private readonly TextBlock _status;
    private readonly Border _apply;

    public ChangeFilePasswordWindow(FileItem item)
    {
        _item = item;
        var body = LockUi.Configure(this, T("tool.lock.changePasswordTitle"), 460, 470);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.changePasswordHint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 0, 0, 10),
        });
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.currentFilePassword"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 5),
        });
        _current = new SecretField(T("tool.lock.currentFilePassword"));
        body.Children.Add(_current);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.filePassword"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 12, 0, 5),
        });
        _password = new SecretField(T("tool.lock.filePassword"));
        body.Children.Add(_password);
        var strength = new StrengthMeter();
        body.Children.Add(strength);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.confirm"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 10, 0, 5),
        });
        _confirm = new SecretField(T("tool.lock.confirm"));
        body.Children.Add(_confirm);
        _status = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Text = T("tool.lock.gateHint"),
        };
        body.Children.Add(_status);

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var cancel = Controls.Button(T("tool.lock.cancel"), Close);
        _apply = Controls.Button(T("tool.lock.changePassword"), Apply, primary: true);
        _apply.IsEnabled = false;
        DockPanel.SetDock(cancel, Dock.Left);
        DockPanel.SetDock(_apply, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_apply);
        body.Children.Add(buttons);

        _password.Changed += () =>
        {
            strength.Update(_password.Text);
            UpdateStatus();
        };
        _confirm.Changed += UpdateStatus;
        _current.Changed += UpdateStatus;
        UpdateStatus();
        Closing += (_, _) =>
        {
            _current.HideReveal();
            _password.HideReveal();
            _confirm.HideReveal();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _apply.IsEnabled)
            {
                Apply();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => _current.FocusInput();
    }

    private void UpdateStatus()
    {
        var status = LockSecrets.CheckPair(_password.Text, _confirm.Text);
        _status.Text = status switch
        {
            LockSecrets.PairStatus.Mismatch => T("tool.lock.mismatch"),
            LockSecrets.PairStatus.Match => T("tool.lock.match"),
            _ => T("tool.lock.gateHint"),
        };
        _status.Foreground = status switch
        {
            LockSecrets.PairStatus.Mismatch => Theme.Error,
            LockSecrets.PairStatus.Match => Theme.Success,
            _ => Theme.Muted,
        };
        _apply.IsEnabled = string.IsNullOrEmpty(_current.Text) ? false : status == LockSecrets.PairStatus.Match;
    }

    private void Apply()
    {
        if (LockRuntime.Journal.FindByContainer(_item.Path) is not null)
        {
            _status.Text = T("tool.lock.error.openSession");
            _status.Foreground = Theme.Error;
            return;
        }
        try
        {
            LockRuntime.Service.SetFilePassword(_item.Path, _current.Text, _password.Text);
            Dialogs.Show(this, T("tool.lock.changePasswordDone"), T("tool.lock.changePasswordTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (LockPolicyException)
        {
            _status.Text = T("tool.lock.changePasswordKeyRefused");
            _status.Foreground = Theme.Error;
        }
        catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                       or ArgumentException)
        {
            _status.Text = LockRuntime.Describe(ex);
            _status.Foreground = Theme.Error;
        }
    }
}