using System.Runtime.InteropServices;
using LucidQueue.Models;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using DrawingImaging = System.Drawing.Imaging;
using Forms = System.Windows.Forms;

namespace LucidQueue.Services;

/// <summary>
/// 托盘图标常驻。待生效提示使用自己绘制的圆点叠加，不依赖系统徽标 API，
/// 也不修改开始菜单、任务栏或通知中心。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _pendingItem;
    private readonly Drawing.Icon _baseIcon;
    private Drawing.Icon _currentIcon;
    private bool _currentIsGenerated;
    private bool _disposed;
    private bool _hasPendingState;
    private int _lastCount;
    private TaskEffect? _lastDominant;

    public TrayService()
    {
        _baseIcon = LoadIcon();
        _currentIcon = _baseIcon;

        _pendingItem = new Forms.ToolStripMenuItem("暂无待生效任务") { Enabled = false };

        var openItem = new Forms.ToolStripMenuItem("打开 Lucid Queue");
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(_pendingItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _currentIcon,
            Text = "Lucid Queue",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public void SetPending(int count, TaskEffect? dominant)
    {
        // 状态没变就不重画图标：每次重绘都要走一遍 GDI+ 与 HICON 转换。
        if (_hasPendingState && _lastCount == count && _lastDominant == dominant)
        {
            return;
        }

        _hasPendingState = true;
        _lastCount = count;
        _lastDominant = dominant;

        var label = dominant?.ShortLabel();
        _pendingItem.Text = count > 0 ? $"{count} 项任务待生效（{label}）" : "暂无待生效任务";
        _notifyIcon.Text = count > 0 ? $"Lucid Queue · {count} 项待生效" : "Lucid Queue";

        if (count <= 0)
        {
            SetIcon(_baseIcon, generated: false);
            return;
        }

        var color = dominant switch
        {
            TaskEffect.Shutdown => Drawing.Color.FromArgb(0xF5, 0x8C, 0x8C),
            TaskEffect.Sleep => Drawing.Color.FromArgb(0xBF, 0xAE, 0xFF),
            _ => Drawing.Color.FromArgb(0xF2, 0xC8, 0x79)
        };

        SetIcon(BuildBadgedIcon(_baseIcon, color), generated: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        if (_currentIsGenerated)
        {
            _currentIcon.Dispose();
        }

        _baseIcon.Dispose();
    }

    private void SetIcon(Drawing.Icon icon, bool generated)
    {
        var previous = _currentIcon;
        var previousGenerated = _currentIsGenerated;

        _currentIcon = icon;
        _currentIsGenerated = generated;
        _notifyIcon.Icon = icon;

        if (previousGenerated && !ReferenceEquals(previous, icon))
        {
            previous.Dispose();
        }
    }

    private static Drawing.Icon BuildBadgedIcon(Drawing.Icon baseIcon, Drawing.Color color)
    {
        using var canvas = new Drawing.Bitmap(32, 32, DrawingImaging.PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(canvas))
        {
            graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.Clear(Drawing.Color.Transparent);

            using (var sized = new Drawing.Icon(baseIcon, 32, 32))
            {
                graphics.DrawIcon(sized, new Drawing.Rectangle(0, 0, 32, 32));
            }

            using var shadow = new Drawing.SolidBrush(Drawing.Color.FromArgb(90, 0, 0, 0));
            graphics.FillEllipse(shadow, 20.5f, 21.5f, 10.5f, 10.5f);

            var dotRect = new Drawing.RectangleF(19f, 19f, 12f, 12f);
            using (var fill = new Drawing.SolidBrush(color))
            {
                graphics.FillEllipse(fill, dotRect);
            }

            using var outline = new Drawing.Pen(Drawing.Color.FromArgb(240, 12, 15, 20), 2f);
            graphics.DrawEllipse(outline, dotRect);
        }

        var handle = canvas.GetHicon();
        try
        {
            using var temporary = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(processPath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // 回退到系统图标。
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
