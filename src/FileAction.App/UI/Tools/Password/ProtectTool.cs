using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Password;

/// <summary>
/// "Protect": one ZIP with AES-256 for a file selection or a folder, or an AES-256 PDF when exactly one
/// PDF is selected. The original files are only read.
/// </summary>
internal sealed class ProtectTool : IToolWindowSpec
{
    public static readonly ProtectTool Instance = new();

    /// <summary>Segoe Fluent "Lock" glyph; kept here so no shared glyph table has to be edited.</summary>
    internal const string Glyph = "\uE72E";

    private static readonly ToolSpecLayout ProtectLayout = new(
        ChromeWidth: 32,
        ChromeHeight: 100,
        PreviewMinWidth: 300,
        PreviewMinHeight: 260,
        PreviewMaxWidth: 620,
        PreviewMaxHeight: 520,
        DefaultPreviewWidth: 448,
        DefaultPreviewHeight: 320);

    public string Id => "tools.protect";

    public string TitleKey => "tool.protect.title";

    public ToolSpecLayout Layout => ProtectLayout;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count > 0 && (items.Count == 1 || items.All(item => !item.IsDirectory));

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ProtectView(items, targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class ProtectToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(ProtectTool.Instance);
}

/// <summary>"Protect" on the Tools wheel: opens the password window instead of starting a job.</summary>
internal sealed class ProtectOperation : Operation, IInteractiveTool
{
    public ProtectOperation()
        : base("tools.protect", WheelKind.Tools, T("tool.protect.label"), ProtectTool.Glyph, T("tool.protect.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => ProtectTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(ProtectTool.Instance, items, targets).Show();
}

internal enum ProtectFormat
{
    Zip,
    Pdf,
}

/// <summary>The protect window content: password + confirmation, strength, output format and permissions.</summary>
internal sealed class ProtectView
{
    private readonly IReadOnlyList<FileItem> _items;
    private readonly DropTargets? _targets;
    private readonly bool _canChoosePdf;
    private readonly ProtectFormat _defaultFormat;
    private readonly PasswordField _password = new();
    private readonly PasswordField _confirm = new();
    private readonly StrengthMeter _strength = new();
    private readonly ContentControl _formatHost = new();
    private readonly StackPanel _pdfOptions = new();
    private readonly TextBlock _zipNote;
    private readonly SwitchRow _blockPrint;
    private readonly SwitchRow _blockCopy;
    private readonly BusyRow _busy;
    private readonly TextBlock _error = PasswordViewParts.Error();
    private readonly PasswordFormState _form = new();
    private readonly TextBlock _matchStatus = new()
    {
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 5, 0, 0),
    };

    private ProtectFormat _format;
    private CancellationTokenSource? _cts;
    private ScrollViewer? _anchor;
    private Border? _primary;
    private bool _busyNow;

    public ProtectView(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        _items = items;
        _targets = targets;
        _canChoosePdf = items.Count == 1 && !items[0].IsDirectory && items[0].Format == "pdf";
        _defaultFormat = PasswordProtectionDefaults.DefaultFormat(items.Count, _canChoosePdf) == ProtectionOutputFormat.Pdf
            ? ProtectFormat.Pdf
            : ProtectFormat.Zip;
        _format = _defaultFormat;

        _zipNote = PasswordViewParts.Note("tool.protect.note.names");
        _blockPrint = new SwitchRow(T("tool.protect.blockPrint"), _ => { });
        _blockCopy = new SwitchRow(T("tool.protect.blockCopy"), _ => { });
        _busy = new BusyRow(T("tool.protect.progress.cancel"), Cancel);

        _password.Changed += () =>
        {
            _form.Password = _password.Value;
            _strength.Set(_form.Password);
            UpdateMatchStatus();
            UpdatePrimaryState();
            HideError();
        };
        _confirm.Changed += () =>
        {
            _form.Confirmation = _confirm.Value;
            UpdateMatchStatus();
            UpdatePrimaryState();
            HideError();
        };

        _pdfOptions.Children.Add(PasswordViewParts.Caption("tool.protect.permissions"));
        _pdfOptions.Children.Add(_blockPrint);
        _pdfOptions.Children.Add(_blockCopy);
        _pdfOptions.Children.Add(PasswordViewParts.Note("tool.protect.note.pdf"));
    }

    public ToolContent Build()
    {
        var content = new StackPanel { Margin = new Thickness(1, 0, 5, 0) };
        content.Children.Add(PasswordViewParts.Note("tool.protect.intro"));
        content.Children.Add(Summary());
        content.Children.Add(new TextBlock
        {
            Text = T("tool.protect.status.aes"),
            FontSize = 11.5,
            Foreground = Theme.Accent,
            Margin = new Thickness(0, 5, 0, 0),
        });

        if (_canChoosePdf)
        {
            content.Children.Add(PasswordViewParts.Caption("tool.protect.format"));
            _formatHost.Content = FormatChoices();
            content.Children.Add(_formatHost);
        }

        content.Children.Add(PasswordViewParts.Caption("tool.protect.password"));
        content.Children.Add(_password);
        content.Children.Add(PasswordViewParts.Caption("tool.protect.confirm"));
        content.Children.Add(_confirm);
        content.Children.Add(_matchStatus);
        content.Children.Add(PasswordViewParts.Caption("tool.protect.strength"));
        content.Children.Add(_strength);
        content.Children.Add(PasswordViewParts.Note("tool.protect.note.password"));
        content.Children.Add(_zipNote);
        content.Children.Add(_pdfOptions);

        RefreshOptions();

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };

        _anchor = scroll;
        scroll.Loaded += (_, _) =>
        {
            _primary = PasswordViewParts.FindPrimaryButton(scroll);
            PasswordViewParts.ConfigurePrimary(_primary, T("tool.protect.label"), false);
            if (Window.GetWindow(scroll) is { } window)
                window.KeyDown += OnWindowKeyDown;
            _password.FocusInput();
            UpdateMatchStatus();
            UpdatePrimaryState();
        };
        scroll.Unloaded += (_, _) =>
        {
            Cancel();
            ClearSecrets();
        };

        // The Cancel button and the error stay visible while the form scrolls.
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scroll, 0);
        Grid.SetRow(_busy, 1);
        Grid.SetRow(_error, 2);
        root.Children.Add(scroll);
        root.Children.Add(_busy);
        root.Children.Add(_error);

        return new ToolContent(
            root,
            Reset,
            ApplyAsync,
            Task.FromResult<MediaSize?>(null),
            () => Task.CompletedTask);
    }

    private TextBlock Summary()
    {
        var text = _items.Count switch
        {
            1 when _items[0].IsDirectory => T("tool.protect.source.folder", Bidi.Value(_items[0].Name)),
            1 => T("tool.protect.source.file", Bidi.Value(_items[0].Name)),
            _ => T("tool.protect.source.many", Bidi.Number(_items.Count)),
        };
        return new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
    }

    private FrameworkElement FormatChoices() =>
        Controls.Segmented(
            [
                (T("tool.protect.format.zip"), ProtectFormat.Zip),
                (T("tool.protect.format.pdf"), ProtectFormat.Pdf),
            ],
            _format,
            value =>
            {
                _format = value;
                RefreshOptions();
            });

    private void RefreshOptions()
    {
        var pdf = _format == ProtectFormat.Pdf;
        _pdfOptions.Visibility = pdf ? Visibility.Visible : Visibility.Collapsed;
        _zipNote.Visibility = pdf ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        PasswordViewParts.ClickPrimary(_primary);
    }

    private void UpdatePrimaryState() =>
        PasswordViewParts.ConfigurePrimary(
            _primary,
            T("tool.protect.label"),
            !_busyNow && _form.CanSubmit);

    private void UpdateMatchStatus()
    {
        var status = _form.MatchStatus;
        _matchStatus.Visibility = status == PasswordMatchStatus.Empty ? Visibility.Collapsed : Visibility.Visible;
        switch (status)
        {
            case PasswordMatchStatus.Match:
                _matchStatus.Text = T("tool.protect.match.yes");
                _matchStatus.Foreground = Theme.Success;
                break;
            case PasswordMatchStatus.Mismatch:
                _matchStatus.Text = T("tool.protect.match.no");
                _matchStatus.Foreground = Theme.Error;
                break;
            case PasswordMatchStatus.TooShort:
                _matchStatus.Text = T("tool.protect.hint.minLength", PasswordRules.MinimumLength);
                _matchStatus.Foreground = Theme.Error;
                break;
            default:
                _matchStatus.Text = string.Empty;
                break;
        }
    }

    /// <summary>Re-applies the gate after the shared window's own finally re-enables its button.</summary>
    private void SchedulePrimaryUpdate() => _anchor?.Dispatcher.BeginInvoke(
        new Action(UpdatePrimaryState),
        DispatcherPriority.Background);

    private void Reset()
    {
        Cancel();
        _form.Reset();
        _password.Clear();
        _confirm.Clear();
        _strength.Set(null);
        _blockPrint.Set(false);
        _blockCopy.Set(false);
        _busy.Hide();
        HideError();
        UpdateMatchStatus();
        UpdatePrimaryState();
        _format = _defaultFormat;
        if (_canChoosePdf)
            _formatHost.Content = FormatChoices();
        RefreshOptions();
    }

    private void Cancel() => _cts?.Cancel();

    private async Task<string?> ApplyAsync()
    {
        HideError();

        _form.Password = _password.Value;
        _form.Confirmation = _confirm.Value;
        var password = _form.Password;
        var problem = PasswordRules.Validate(password, _confirm.Value, requireConfirmation: true);
        if (problem != PasswordProblem.None)
        {
            var key = PasswordRules.MessageKey(problem) ?? "tool.protect.error.generic";
            var arguments = problem is PasswordProblem.Empty or PasswordProblem.TooShort
                ? new object?[] { PasswordRules.MinimumLength }
                : problem == PasswordProblem.TooLong
                    ? new object?[] { PasswordRules.MaximumLength }
                    : [];
            ShowError(T(key, arguments));
            SchedulePrimaryUpdate();
            throw new PasswordProtectionException(FailureFor(problem), key, null, arguments);
        }

        if (_format == ProtectFormat.Pdf && Encoding.UTF8.GetByteCount(password) > 127)
        {
            var message = T("tool.protect.error.pdfPassword");
            ShowError(message);
            SchedulePrimaryUpdate();
            throw new PasswordProtectionException(PasswordProtectionFailure.TooLong, "tool.protect.error.pdfPassword");
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        try
        {
            var directory = OutputDirectory();
            var baseName = $"{Stem()} {T("tool.protect.suffix")}".Trim();

            if (_format == ProtectFormat.Pdf)
            {
                var permissions = new PdfPermissionPolicy(
                    AllowPrinting: !_blockPrint.IsOn,
                    AllowCopying: !_blockCopy.IsOn);
                return await OutputPaths.ProduceAsync(directory, baseName, "pdf", temp =>
                    Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        PdfPasswordProtection.Protect(_items[0].Path, temp, password, permissions);
                    }, token));
            }

            var plan = await Task.Run(
                () => ProtectedZipPlanBuilder.Collect(_items.Select(item => item.Path).ToList(), token),
                token);
            var progress = new Progress<double>(value => _busy.Report(value));
            return await OutputPaths.ProduceAsync(directory, baseName, "zip", temp =>
                Task.Run(() => AesZipWriter.Create(plan, temp, password, progress, token), token));
        }
        catch (OperationCanceledException)
        {
            ShowError(T("tool.protect.cancelled"));
            return null;
        }
        catch (PasswordProtectionException ex)
        {
            ShowError(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            var message = T("tool.protect.error.generic");
            ShowError(message);
            throw new PasswordProtectionException(PasswordProtectionFailure.Generic, "tool.protect.error.generic", ex);
        }
        finally
        {
            SetBusy(false);
            SchedulePrimaryUpdate();
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static PasswordProtectionFailure FailureFor(PasswordProblem problem) => problem switch
    {
        PasswordProblem.Empty or PasswordProblem.TooShort => PasswordProtectionFailure.InvalidPassword,
        PasswordProblem.Mismatch => PasswordProtectionFailure.PasswordMismatch,
        PasswordProblem.TooLong => PasswordProtectionFailure.TooLong,
        _ => PasswordProtectionFailure.Generic,
    };

    private void SetBusy(bool busy)
    {
        _busyNow = busy;
        _password.SetEnabled(!busy);
        _confirm.SetEnabled(!busy);
        _formatHost.IsEnabled = !busy;
        _pdfOptions.IsEnabled = !busy;
        if (busy)
            _busy.Show(indeterminate: _format == ProtectFormat.Pdf, T("tool.protect.progress.working"));
        else
            _busy.Hide();
        UpdatePrimaryState();
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        _error.Text = string.Empty;
        _error.Visibility = Visibility.Collapsed;
    }

    private string Stem()
    {
        var first = _items[0];
        var stem = first.IsDirectory ? first.Name : first.BaseName;
        return string.IsNullOrWhiteSpace(stem) ? "Protected" : stem;
    }

    /// <summary>Next to the source, or the folder this drop resolved (the same rules as Crop).</summary>
    private string OutputDirectory()
    {
        var detected = OutputTarget.Resolve(_items[0].Path, _targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        if (!detected.UsesDetectedFolder && _targets?.Remembered is { } remembered)
        {
            var fallback = OutputTarget.Resolve(_items[0].Path, remembered, Directory.Exists, OutputTarget.CanWrite);
            if (fallback.UsesDetectedFolder)
                return fallback.Directory!;
        }
        return detected.Directory
            ?? OutputTarget.SourceFolder(_items[0].Path)
            ?? Environment.CurrentDirectory;
    }

    /// <summary>Clears the fields when the window closes; nothing is persisted or logged.</summary>
    public void ClearSecrets()
    {
        _form.Reset();
        _password.Clear();
        _confirm.Clear();
        _strength.Set(null);
        UpdateMatchStatus();
        UpdatePrimaryState();
    }
}
