using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using LucidQueue.Common;
using LucidQueue.Models;
using LucidQueue.Services;
using LucidQueue.ViewModels;

namespace LucidQueue.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly bool _enableTray;
    private TrayService? _tray;
    private bool _exitRequested;

    public MainWindow(MainViewModel viewModel, bool enableTray = true)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _enableTray = enableTray;
        DataContext = viewModel;
        WindowCorners.Apply(this);
        InitTray();
    }

    public void SaveSnapshot(string path)
    {
        Root.UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(Root);
        var width = Math.Max(1, (int)Math.Ceiling(Root.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(Root.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(Root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void InitTray()
    {
        if (!_enableTray)
        {
            return;
        }

        try
        {
            _tray = new TrayService();
            _tray.OpenRequested += (_, _) => Dispatcher.Invoke(RestoreFromTray);
            _tray.ExitRequested += (_, _) => Dispatcher.Invoke(ExitApplication);
            _tray.SetPending(_viewModel.PendingTotal, _viewModel.PendingDominantEffect);
            _viewModel.PendingChanged += (_, _) =>
                _tray?.SetPending(_viewModel.PendingTotal, _viewModel.PendingDominantEffect);
        }
        catch
        {
            _tray = null;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitRequested || _tray is null)
        {
            _viewModel.SaveNow();
            _viewModel.Dispose();
            _tray?.Dispose();
            _tray = null;
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove 在窗口状态切换时可能抛异常。
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Completed_Click(object sender, RoutedEventArgs e)
    {
        var window = new CompletedWindow(_viewModel) { Owner = this };
        window.ShowDialog();
    }

    private void TaskCheck_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not TaskItem task)
        {
            return;
        }

        // 勾选状态已经和数据一致时（例如由模型通知更新），不再重复提交。
        if (box.IsChecked == task.IsCompleted)
        {
            return;
        }

        _viewModel.ToggleCompletion(task, out _);
        box.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        box.IsChecked = task.IsCompleted;
    }

    private void TaskMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not TaskItem task)
        {
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = element,
            Placement = PlacementMode.Bottom
        };

        var edit = new MenuItem { Header = "编辑任务" };
        edit.Click += (_, _) => OpenEditor(task);

        var delete = new MenuItem { Header = "删除任务" };
        delete.Click += (_, _) => DeleteTask(task);

        menu.Items.Add(edit);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    private void OpenEditor(TaskItem? task)
    {
        var editor = new TaskEditorWindow(_viewModel.Engine, task) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            _viewModel.ShowToast(task is null ? "任务已创建。" : "任务已更新。");
        }
    }

    private void DeleteTask(TaskItem task)
    {
        var confirm = new ConfirmWindow(
            "删除任务",
            $"确定删除「{task.Title}」吗？\n依赖它的任务会失去这条前置关系。",
            "删除",
            "取消",
            danger: true)
        {
            Owner = this
        };

        if (confirm.ShowDialog() == true)
        {
            _viewModel.DeleteTask(task);
        }
    }

    private void PowerAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not PendingGroup group)
        {
            return;
        }

        var action = group.ActionText;
        var message = group.Effect switch
        {
            TaskEffect.Reboot =>
                $"即将进入 10 秒确认倒计时。倒计时结束后会提示你手动重启 Windows。\n\nLucid Queue 不会执行重启，也不会修改任何系统设置。\n如果还有常规任务没做完，建议先处理。",
            TaskEffect.Shutdown =>
                $"即将进入 10 秒确认倒计时。倒计时结束后会提示你手动关机。\n\nLucid Queue 不会执行关机，也不会修改任何系统设置。",
            _ => $"即将进入 10 秒确认倒计时。倒计时结束后会提示你手动睡眠。\n\nLucid Queue 不会执行睡眠，也不会修改任何系统设置。部分设备可能受电源策略影响。"
        };

        var confirm = new ConfirmWindow(action, message, "开始倒计时", "取消",
            danger: group.Effect != TaskEffect.Sleep)
        {
            Owner = this
        };

        if (confirm.ShowDialog() == true)
        {
            _viewModel.RequestPower(group.Effect);
        }
    }

    private void MarkApplied_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not PendingGroup group)
        {
            return;
        }

        var confirm = new ConfirmWindow(
            "标记已生效",
            $"将把已完成的 {group.Count} 项{group.Effect.ShortLabel()}任务标记为生效。\n\n不会执行任何重启、关机或睡眠操作，请确认你已经手动完成了对应的系统操作。",
            "确认生效",
            "取消",
            danger: false)
        {
            Owner = this
        };

        if (confirm.ShowDialog() == true)
        {
            _viewModel.MarkApplied(group.Effect);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _viewModel.SaveNow();
        _tray?.Dispose();
        _tray = null;
        System.Windows.Application.Current.Shutdown();
    }
}
