using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FileAction.App.UI;
using FileAction.Core;
using FileAction.Core.Lock;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell.Protection;

internal enum UnlockedStatus
{
    Synced,
    Syncing,
    Error,
}

/// <summary>
/// The small always-on-top panel for an unlocked item: "Unlocked: name", a note that closing the program
/// locks it again, the synced/syncing/error state with retry, and a "Lock now" button.
/// </summary>
internal sealed class UnlockedPanel : Window
{
    private readonly TextBlock _status;
    private readonly TextBlock _badge;
    private readonly Border _retry;
    private readonly DispatcherTimer _hide;

    public UnlockedPanel(string name)
    {
        Theme.Refresh();
        Title = T("tool.lock.panel.title", Bidi.Value(name));
        Width = 330;
        Height = 136;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FlowDirection = LanguageManager.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;

        _badge = new TextBlock
        {
            Text = "\uE72E",
            FontFamily = Theme.Icons,
            FontSize = 16,
            Foreground = Theme.LockBadgeText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var title = new DockPanel();
        DockPanel.SetDock(_badge, Dock.Left);
        title.Children.Add(_badge);
        title.Children.Add(new TextBlock
        {
            Text = T("tool.lock.panel.unlocked", Bidi.Value(name)),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var note = new TextBlock
        {
            Text = T("tool.lock.panel.note"),
            FontSize = 11.5,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        _status = new TextBlock
        {
            Text = T("tool.lock.panel.synced"),
            FontSize = 11.5,
            Foreground = Theme.BadgeSuccessText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _retry = Controls.Button(T("tool.lock.panel.retry"), () => Retry?.Invoke());
        _retry.Visibility = Visibility.Collapsed;

        var statusRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(_retry, Dock.Right);
        statusRow.Children.Add(_retry);
        statusRow.Children.Add(_status);

        var body = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
        body.Children.Add(title);
        body.Children.Add(note);
        body.Children.Add(statusRow);

        Content = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8),
            Effect = Theme.Shadow(18),
            Child = body,
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }
        };
        // A short confirmation only: the session keeps locking in the background, the panel does not
        // need to sit on top of every window for the whole editing session.
        _hide = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Normal, (_, _) =>
        {
            _hide.Stop();
            Hide();
        }, Dispatcher);
        Loaded += (_, _) =>
        {
            PlaceBottomRight();
            _hide.Start();
        };
    }

    public event Action? Retry;

    public void SetStatus(UnlockedStatus status)
    {
        _status.Text = T(status switch
        {
            UnlockedStatus.Synced => "tool.lock.panel.synced",
            UnlockedStatus.Syncing => "tool.lock.panel.syncing",
            _ => "tool.lock.panel.error",
        });
        _status.Foreground = status switch
        {
            UnlockedStatus.Synced => Theme.BadgeSuccessText,
            UnlockedStatus.Syncing => Theme.LockMuted,
            _ => Theme.BadgeErrorText,
        };
        _retry.Visibility = status == UnlockedStatus.Error ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetError(string detail)
    {
        _status.Text = detail;
        _status.Foreground = Theme.BadgeErrorText;
        _retry.Visibility = Visibility.Visible;
    }

    public void ClosePanel()
    {
        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void PlaceBottomRight()
    {
        try
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - 24;
            Top = work.Bottom - ActualHeight - 24;
        }
        catch (InvalidOperationException)
        {
        }
    }
}

/// <summary>
/// Startup recovery: explains whether the container still exists and asks for the file password the edited
/// plaintext will be protected with. "Not now" keeps everything for the next start.
/// </summary>
internal sealed class LockRecoveryWindow : Window
{
    private readonly SecretField _password;

    public LockRecoveryWindow(LockSessionRecord record, LockRecoveryAction action)
    {
        var body = LockUi.Configure(this, T("tool.lock.recovery.title"), 430, 330);
        body.Children.Add(new TextBlock
        {
            Text = T(action == LockRecoveryAction.PromptSalvage
                ? "tool.lock.recovery.salvage"
                : "tool.lock.recovery.body", Bidi.Value(record.OriginalName)),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 0, 0, 10),
        });
        body.Children.Add(new TextBlock
        {
            Text = Bidi.Value(record.ContainerPath),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Theme.LockMuted,
            Margin = new Thickness(0, 0, 0, 12),
        });
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.filePassword"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 5),
        });
        _password = new SecretField(T("tool.lock.filePassword"));
        body.Children.Add(_password);

        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Controls.Button(T("tool.lock.recovery.later"), Close);
        var recover = Controls.Button(T("tool.lock.recovery.recover"), () =>
        {
            if (!LockSecrets.IsAcceptablePassword(_password.Text))
            {
                Dialogs.Show(this, T("tool.lock.error.short"), Title, MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
            Close();
        }, primary: true);
        DockPanel.SetDock(cancel, Dock.Left);
        DockPanel.SetDock(recover, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(recover);
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

    public string FilePassword => _password.Text;
}
