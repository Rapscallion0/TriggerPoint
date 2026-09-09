using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using TriggerPoint.Core.Contracts;

namespace TriggerPoint.UI.Services;

public enum TrayIconStatus
{
    Armed,
    Conflict,
    Snoozed
}

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly IShortcutListener _shortcutListener;
    private readonly Action _openSettingsAction;
    private readonly Action? _openAppSettingsAction;
    private readonly Action _openPaletteAction;
    private readonly Action _reloadConfigAction;
    private readonly Action _exitAction;

    private readonly ToolStripMenuItem _snoozeMenuItem;
    private Icon? _currentGeneratedIcon;

    public TrayIconService(
        IShortcutListener shortcutListener,
        Action openSettingsAction,
        Action openPaletteAction,
        Action reloadConfigAction,
        Action exitAction,
        Action? openAppSettingsAction = null)
    {
        _shortcutListener = shortcutListener;
        _openSettingsAction = openSettingsAction;
        _openPaletteAction = openPaletteAction;
        _reloadConfigAction = reloadConfigAction;
        _exitAction = exitAction;
        _openAppSettingsAction = openAppSettingsAction;

        _notifyIcon = new NotifyIcon
        {
            Text = "TriggerPoint — Precision Shortcuts & Instant Menus",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("Action Settings...", null, (s, e) => _openSettingsAction())
        {
            Font = new Font(contextMenu.Font, FontStyle.Bold)
        };
        contextMenu.Items.Add(settingsItem);

        if (_openAppSettingsAction != null)
        {
            var appSettingsItem = new ToolStripMenuItem("Application Settings...", null, (s, e) => _openAppSettingsAction());
            contextMenu.Items.Add(appSettingsItem);
        }

        var paletteItem = new ToolStripMenuItem("Command Palette (Alt+Space)", null, (s, e) => _openPaletteAction());
        contextMenu.Items.Add(paletteItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem? snoozeItem = null;
        snoozeItem = new ToolStripMenuItem("Snooze Global Hotkeys", null, (s, e) =>
        {
            _shortcutListener.IsSnoozed = !_shortcutListener.IsSnoozed;
            if (snoozeItem != null)
            {
                snoozeItem.Checked = _shortcutListener.IsSnoozed;
            }
            UpdateTrayIcon();
        })
        {
            Checked = _shortcutListener.IsSnoozed
        };
        _snoozeMenuItem = snoozeItem;
        contextMenu.Items.Add(_snoozeMenuItem);

        var reloadItem = new ToolStripMenuItem("Reload Configuration", null, (s, e) => _reloadConfigAction());
        contextMenu.Items.Add(reloadItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit TriggerPoint", null, (s, e) => _exitAction());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => _openSettingsAction();

        _shortcutListener.ConflictsUpdated += (s, e) => UpdateTrayIcon();

        UpdateTrayIcon();
    }

    public void UpdateTrayIcon()
    {
        var status = TrayIconStatus.Armed;
        if (_shortcutListener.IsSnoozed)
        {
            status = TrayIconStatus.Snoozed;
        }
        else if (_shortcutListener.CurrentConflicts.Count > 0)
        {
            status = TrayIconStatus.Conflict;
        }

        // Generate dynamic 16x16 icon based on status
        var oldIcon = _currentGeneratedIcon;
        _currentGeneratedIcon = RenderReticleIcon(status);
        _notifyIcon.Icon = _currentGeneratedIcon;
        oldIcon?.Dispose();

        _snoozeMenuItem.Checked = _shortcutListener.IsSnoozed;
    }

    public static Icon RenderReticleIcon(TrayIconStatus status, bool isDarkTaskbar = true)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float cx = 8f;
            float cy = 8f;
            float r = 5f;

            // Reticle line color: White on dark taskbar, Dark Charcoal on light taskbar
            var lineColor = isDarkTaskbar 
                ? Color.FromArgb(245, 245, 245) 
                : Color.FromArgb(28, 28, 30);

            using var pen = new Pen(lineColor, 1.0f);

            // 4 arcs with gaps for compass ticks
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 25, 40);
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 115, 40);
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 205, 40);
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 295, 40);

            // 4 compass ticks
            g.DrawLine(pen, cx, 1f, cx, 3f);
            g.DrawLine(pen, cx, 13f, cx, 15f);
            g.DrawLine(pen, 1f, cy, 3f, cy);
            g.DrawLine(pen, 13f, cy, 15f, cy);

            // Status Center Dot
            // Teal = Armed, Amber = Conflict, Gray = Snoozed
            var dotColor = status switch
            {
                TrayIconStatus.Armed => Color.FromArgb(0, 240, 255),    // Electric Cyan #00F0FF
                TrayIconStatus.Conflict => Color.FromArgb(245, 158, 11), // Amber #F59E0B
                TrayIconStatus.Snoozed => Color.FromArgb(115, 118, 130), // Muted Slate Gray #737682
                _ => Color.FromArgb(0, 240, 255)
            };

            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, cx - 1.5f, cy - 1.5f, 3f, 3f);
        }

        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        return icon;
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentGeneratedIcon?.Dispose();
        GC.SuppressFinalize(this);
    }
}
