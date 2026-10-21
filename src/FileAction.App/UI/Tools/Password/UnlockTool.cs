using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Password;

/// <summary>
/// "Unlock": removes protection from one encrypted ZIP or password-protected PDF using the correct
/// password. It makes one attempt and never tries to crack or recover a password.
/// </summary>
internal sealed class UnlockTool : IToolWindowSpec
{
    public static readonly UnlockTool Instance = new();

    internal const string Glyph = "\uE785";

    private static readonly ToolSpecLayout UnlockLayout = new(
        ChromeWidth: 32,
        ChromeHeight: 100,
        PreviewMinWidth: 300,
        PreviewMinHeight: 240,
        PreviewMaxWidth: 620,
        PreviewMaxHeight: 520,
        DefaultPreviewWidth: 448,
        DefaultPreviewHeight: 320);

    public string Id => "tools.unlock";

    public string TitleKey => "tool.unlock.title";

    public ToolSpecLayout Layout => UnlockLayout;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 &&
        !items[0].IsDirectory &&
        items[0].Format is "zip" or "pdf";

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new UnlockView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class UnlockToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(UnlockTool.Instance);
}

/// <summary>"Unlock" on the Tools wheel: opens the password window instead of starting a job.</summary>
internal sealed class UnlockOperation : Operation, IInteractiveTool
{
    public UnlockOperation()
        : base("tools.unlock", WheelKind.Tools, T("tool.unlock.label"), UnlockTool.Glyph, T("tool.unlock.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => UnlockTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(UnlockTool.Instance, items, targets).Show();
}

/// <summary>The unlock window content: one password field, one progress row and a clear error.</summary>
internal sealed class UnlockView
{
    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly PasswordField _password = new("tool.unlock.show", "tool.unlock.hide");
    private readonly BusyRow _busy;
    private readonly TextBlock _error = PasswordViewParts.Error();
    private readonly PasswordEntryState _form = new();

    private CancellationTokenSource? _cts;
    private ScrollViewer? _anchor;
    private Border? _primary;
    private bool _busyNow;

    public UnlockView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _busy = new BusyRow(T("tool.unlock.progress.cancel"), Cancel);
        _password.Changed += () =>
        {
            _form.Password = _password.Value;
            UpdatePrimaryState();
            HideError();
        };
    }

    public ToolContent Build()
    {
        var content = new StackPanel();
        content.Children.Add(PasswordViewParts.Note("tool.unlock.intro"));
        content.Children.Add(new TextBlock
        {
            Text = T("tool.unlock.source.file", Bidi.Value(_item.Name)),
            FontSize = 12.5,
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        content.Children.Add(PasswordViewParts.Caption("tool.unlock.password"));
        content.Children.Add(_password);
        content.Children.Add(PasswordViewParts.Note("tool.unlock.note"));

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
            PasswordViewParts.ConfigurePrimary(_primary, T("tool.unlock.label"), false);
            if (Window.GetWindow(scroll) is { } window)
                window.KeyDown += OnWindowKeyDown;
            _password.FocusInput();
            UpdatePrimaryState();
        };
        scroll.Unloaded += (_, _) =>
        {
            Cancel();
            ClearSecrets();
        };

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
            T("tool.unlock.label"),
            !_busyNow && _form.CanSubmit);

    private void SchedulePrimaryUpdate() => _anchor?.Dispatcher.BeginInvoke(
        new Action(UpdatePrimaryState),
        DispatcherPriority.Background);

    private void Reset()
    {
        Cancel();
        _form.Reset();
        _password.Clear();
        _busy.Hide();
        HideError();
        UpdatePrimaryState();
    }

    private void Cancel() => _cts?.Cancel();

    private async Task<string?> ApplyAsync()
    {
        HideError();

        _form.Password = _password.Value;
        var password = _form.Password;
        var problem = PasswordRules.ValidateForUnlock(password);
        if (problem != PasswordProblem.None)
        {
            var key = problem == PasswordProblem.Empty
                ? "tool.unlock.error.empty"
                : "tool.unlock.error.tooLong";
            var arguments = problem == PasswordProblem.TooLong
                ? new object?[] { PasswordRules.MaximumLength }
                : [];
            ShowError(T(key, arguments));
            SchedulePrimaryUpdate();
            throw new PasswordProtectionException(PasswordProtectionFailure.InvalidPassword, key, null, arguments);
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        try
        {
            var directory = OutputDirectory();
            var baseName = $"{Stem()} {T("tool.unlock.suffix")}".Trim();
            var format = _item.Format;
            if (format is not ("zip" or "pdf"))
            {
                var unsupported = T("tool.unlock.error.unsupported");
                ShowError(unsupported);
                throw new PasswordProtectionException(
                    PasswordProtectionFailure.UnsupportedInput, "tool.unlock.error.unsupported");
            }

            if (format == "pdf")
            {
                return await OutputPaths.ProduceAsync(directory, baseName, "pdf", temp =>
                    Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        PdfPasswordProtection.Unprotect(_item.Path, temp, password);
                    }, token));
            }

            var progress = new Progress<double>(value => _busy.Report(value));
            return await OutputPaths.ProduceAsync(directory, baseName, "zip", temp =>
                Task.Run(() => ZipPasswordUnlocker.Unlock(_item.Path, temp, password, progress, token), token));
        }
        catch (OperationCanceledException)
        {
            ShowError(T("tool.unlock.cancelled"));
            return null;
        }
        catch (PasswordProtectionException ex)
        {
            ShowError(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            var message = T("tool.unlock.error.generic");
            ShowError(message);
            throw new PasswordProtectionException(PasswordProtectionFailure.Generic, "tool.unlock.error.generic", ex);
        }
        finally
        {
            SetBusy(false);
            SchedulePrimaryUpdate();
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void SetBusy(bool busy)
    {
        _busyNow = busy;
        _password.SetEnabled(!busy);
        if (busy)
            _busy.Show(indeterminate: _item.Format == "pdf", T("tool.unlock.progress.working"));
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
        var stem = _item.BaseName;
        return string.IsNullOrWhiteSpace(stem) ? "Unlocked" : stem;
    }

    /// <summary>Next to the source, or the folder this drop resolved (the same rules as Crop).</summary>
    private string OutputDirectory()
    {
        var detected = OutputTarget.Resolve(_item.Path, _targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        if (!detected.UsesDetectedFolder && _targets?.Remembered is { } remembered)
        {
            var fallback = OutputTarget.Resolve(_item.Path, remembered, Directory.Exists, OutputTarget.CanWrite);
            if (fallback.UsesDetectedFolder)
                return fallback.Directory!;
        }
        return detected.Directory
            ?? OutputTarget.SourceFolder(_item.Path)
            ?? Environment.CurrentDirectory;
    }

    public void ClearSecrets()
    {
        _form.Reset();
        _password.Clear();
        UpdatePrimaryState();
    }
}
