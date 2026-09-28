using System.Windows;
using WinForms = System.Windows.Forms;

namespace FlowBoard.Services;

/// <summary>System tray icon with a small menu and balloon (toast) notifications.</summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _focusItem;
    private Guid? _pendingCardId;

    public event EventHandler? OpenRequested;
    public event EventHandler? QuickAddRequested;
    public event EventHandler? FocusToggleRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<Guid>? CardRequested;

    public TrayService()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open FlowBoard", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Quick add card…", null, (_, _) => QuickAddRequested?.Invoke(this, EventArgs.Empty));
        _focusItem = new WinForms.ToolStripMenuItem("Start focus session", null, (_, _) => FocusToggleRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_focusItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new WinForms.NotifyIcon
        {
            Text = "FlowBoard",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_pendingCardId is { } id) CardRequested?.Invoke(this, id);
            else OpenRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public void SetFocusMenuText(string text) => _focusItem.Text = text;

    public void Notify(string title, string message, Guid? cardId = null)
    {
        _pendingCardId = cardId;
        _icon.ShowBalloonTip(6000, title, message, WinForms.ToolTipIcon.Info);
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info != null)
            {
                using var s = info.Stream;
                return new System.Drawing.Icon(s, 32, 32);
            }
        }
        catch
        {
            // Fall through to the default icon.
        }

        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
