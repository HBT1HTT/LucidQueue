using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LucidQueue.Common;
using LucidQueue.Models;
using LucidQueue.Services;
using LucidQueue.ViewModels;

namespace LucidQueue.Views;

public partial class TaskEditorWindow : Window
{
    private readonly QueueEngine _engine;
    private readonly TaskItem? _task;
    private readonly ObservableCollection<DependencyOption> _dependencies = new();
    private readonly ObservableCollection<ParameterRow> _parameters = new();

    public TaskEditorWindow(QueueEngine engine, TaskItem? task)
    {
        InitializeComponent();
        _engine = engine;
        _task = task;
        WindowCorners.Apply(this);

        WindowTitle.Text = task is null ? "新建任务" : "编辑任务";
        SaveButton.Content = task is null ? "创建任务" : "保存修改";

        DependencyList.ItemsSource = _dependencies;
        ParameterList.ItemsSource = _parameters;

        TitleBox.Text = task?.Title ?? string.Empty;
        NotesBox.Text = task?.Notes ?? string.Empty;
        NeedsRebootBox.IsChecked = task is not null && task.Effect != TaskEffect.None;

        foreach (var other in engine.Data.Tasks
                     .Where(t => t.Id != task?.Id)
                     .OrderBy(t => t.Title))
        {
            _dependencies.Add(new DependencyOption(other, task?.DependsOn.Contains(other.Id) == true));
        }

        EmptyDependencyHint.Visibility = _dependencies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (task is not null)
        {
            foreach (var pair in task.Parameters)
            {
                _parameters.Add(new ParameterRow(pair.Key, pair.Value));
            }
        }

        Loaded += (_, _) => TitleBox.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
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

    private void AddParameter_Click(object sender, RoutedEventArgs e)
    {
        _parameters.Add(new ParameterRow());
    }

    private void RemoveParameter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is ParameterRow row)
        {
            _parameters.Remove(row);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            ShowError("标题不能为空。");
            return;
        }

        var effect = NeedsRebootBox.IsChecked == true ? TaskEffect.Reboot : TaskEffect.None;
        var dependencies = _dependencies.Where(d => d.IsSelected).Select(d => d.Task.Id).ToList();

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _parameters)
        {
            var key = row.Key?.Trim() ?? string.Empty;
            var value = row.Value?.Trim() ?? string.Empty;

            if (key.Length == 0)
            {
                if (value.Length > 0)
                {
                    ShowError("参数的 Key 不能为空。");
                    return;
                }

                continue;
            }

            if (parameters.ContainsKey(key))
            {
                ShowError($"参数 Key 重复：{key}");
                return;
            }

            parameters[key] = value;
        }

        string? error;
        if (_task is null)
        {
            var created = _engine.TryAddTask(title, effect, NotesBox.Text, parameters, dependencies, out error);
            if (created is null)
            {
                ShowError(error ?? "无法创建任务。");
                return;
            }
        }
        else
        {
            if (!_engine.TryUpdateTask(_task, title, effect, NotesBox.Text, parameters, dependencies, out error))
            {
                ShowError(error ?? "无法保存任务。");
                return;
            }
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBox.Visibility = Visibility.Visible;
    }
}
