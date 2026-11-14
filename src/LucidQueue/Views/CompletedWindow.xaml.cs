using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using LucidQueue.Common;
using LucidQueue.Models;
using LucidQueue.ViewModels;

namespace LucidQueue.Views;

public partial class CompletedWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ObservableCollection<TaskItem> _items = new();

    public CompletedWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        WindowCorners.Apply(this);
        CompletedList.ItemsSource = _items;
        Reload();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    private void Reload()
    {
        _items.Clear();
        foreach (var task in _viewModel.CompletedTasks)
        {
            _items.Add(task);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // 忽略窗口状态切换期间的异常。
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TaskItem task)
        {
            if (_viewModel.RestoreCompleted(task, out var error))
            {
                FooterStatus.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowFooterStatus(error ?? "无法恢复这个任务。");
            }

            Reload();
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var clearable = _viewModel.ClearableCompletedCount;
        if (clearable == 0)
        {
            ShowFooterStatus("没有已生效的完成记录可以清除。");
            return;
        }

        var confirm = new ConfirmWindow(
            "清除已完成",
            $"将删除 {clearable} 条已生效的完成记录。\n\n仍在等待重启生效的任务会保留；依赖这些记录的任务会失去前置关系。此操作不可撤销。",
            "清除",
            "取消",
            danger: true)
        {
            Owner = this
        };

        if (confirm.ShowDialog() == true)
        {
            var removed = _viewModel.ClearCompleted();
            ShowFooterStatus(removed > 0 ? $"已清除 {removed} 条记录。" : "没有可清除的记录。");
            Reload();
        }
    }

    private void ShowFooterStatus(string message)
    {
        FooterStatus.Text = message;
        FooterStatus.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
