using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using LucidQueue.Common;
using LucidQueue.Models;
using LucidQueue.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace LucidQueue.ViewModels;

public enum TaskFilter
{
    Queue,
    Pending
}

public sealed class FilterOption : ObservableObject
{
    private int _count;

    public FilterOption(TaskFilter value, string name)
    {
        Value = value;
        Name = name;
    }

    public TaskFilter Value { get; }

    public string Name { get; }

    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    public string Display => Count > 0 ? $"{Name} {Count}" : Name;
}

public sealed class PendingGroup
{
    public TaskEffect Effect { get; init; }

    public int Count { get; init; }

    public string Preview { get; init; } = string.Empty;

    public string Title => Effect.BatchTitle();

    public string Prompt => Effect.BatchPrompt(Count);

    public string ActionText => Effect.BatchActionText(Count);
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly QueueEngine _engine;
    private readonly DataStore _store;
    private readonly bool _persist;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _toastTimer;

    private string _newTaskTitle = string.Empty;
    private TaskEffect _newTaskEffect = TaskEffect.None;
    private FilterOption? _selectedFilter;
    private string _toastText = string.Empty;
    private bool _toastVisible;
    private bool _countdownVisible;
    private bool _canCancelCountdown;
    private bool _canConfirmManual;
    private string _manualConfirmText = "我已重启";
    private string _countdownText = string.Empty;
    private int _countdownSeconds;
    private TaskEffect _countdownEffect;
    private bool _reminderVisible;
    private string _reminderTitle = string.Empty;
    private string _reminderMeta = string.Empty;
    private string _reminderBody = string.Empty;
    private string _lastAppliedText = "尚无生效记录";
    private int _pendingTotal;
    private TaskEffect? _pendingDominantEffect;
    private MediaBrush _pendingIndicatorBrush = MediaBrushes.Transparent;
    private string _pendingBadgeText = "0";
    private bool _pendingBaselineReady;
    private bool _disposed;

    public MainViewModel(QueueEngine engine, DataStore store, bool persist)
    {
        _engine = engine;
        _store = store;
        _persist = persist;

        Filters = new ObservableCollection<FilterOption>
        {
            new(TaskFilter.Queue, "队列"),
            new(TaskFilter.Pending, "待生效")
        };
        _selectedFilter = Filters[0];

        AddTaskCommand = new RelayCommand(AddTask);
        DismissReminderCommand = new RelayCommand(() => ReminderVisible = false);
        ShowNormalTasksCommand = new RelayCommand(ShowNormalTasks);
        CancelCountdownCommand = new RelayCommand(CancelCountdown);
        ConfirmManualCommand = new RelayCommand(ConfirmManual);

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += CountdownTick;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastVisible = false;
        };

        _engine.Changed += EngineChanged;
        Refresh();
    }

    public ObservableCollection<TaskItem> VisibleTasks { get; } = new();

    public ObservableCollection<PendingGroup> PendingGroups { get; } = new();

    public ObservableCollection<TaskItem> NextUp { get; } = new();

    public ObservableCollection<FilterOption> Filters { get; }

    public RelayCommand AddTaskCommand { get; }

    public RelayCommand DismissReminderCommand { get; }

    public RelayCommand ShowNormalTasksCommand { get; }

    public RelayCommand CancelCountdownCommand { get; }

    public RelayCommand ConfirmManualCommand { get; }

    public event EventHandler? PendingChanged;

    public QueueEngine Engine => _engine;

    public string SortLabel => "依赖优先";

    public string VersionText => "v0.2.0 · dev";

    public string NewTaskTitle
    {
        get => _newTaskTitle;
        set => SetProperty(ref _newTaskTitle, value);
    }

    /// <summary>创建任务时只暴露一个「需要重启」勾选框，判断权完全在用户手里。</summary>
    public bool NewTaskNeedsReboot
    {
        get => _newTaskEffect != TaskEffect.None;
        set
        {
            NewTaskEffect = value ? TaskEffect.Reboot : TaskEffect.None;
            OnPropertyChanged();
        }
    }

    public TaskEffect NewTaskEffect
    {
        get => _newTaskEffect;
        private set
        {
            if (SetProperty(ref _newTaskEffect, value))
            {
                OnPropertyChanged(nameof(NewTaskNeedsReboot));
            }
        }
    }

    public FilterOption? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                Refresh();
            }
        }
    }

    public int PendingTotal => _pendingTotal;

    public TaskEffect? PendingDominantEffect => _pendingDominantEffect;

    public MediaBrush PendingIndicatorBrush
    {
        get => _pendingIndicatorBrush;
        private set => SetProperty(ref _pendingIndicatorBrush, value);
    }

    public string PendingBadgeText
    {
        get => _pendingBadgeText;
        private set => SetProperty(ref _pendingBadgeText, value);
    }

    public string PendingHeadline => _pendingTotal == 0
        ? "暂无待生效任务"
        : $"已有 {_pendingTotal} 项等待{_pendingDominantEffect?.ShortLabel() ?? "生效"}生效";

    public bool HasPending => PendingGroups.Count > 0;

    public bool HasVisibleTasks => VisibleTasks.Count > 0;

    public bool HasNextUp => NextUp.Count > 0;

    public bool HasNormalWarning { get; private set; }

    public string NormalWarningText { get; private set; } = string.Empty;

    public string ActiveSummary { get; private set; } = "0 项进行中";

    public string PendingSummary { get; private set; } = "0 项待生效";

    public string CompletedSummary { get; private set; } = "已完成 0";

    public IReadOnlyList<TaskItem> CompletedTasks => _engine.CompletedTasks;

    public int ClearableCompletedCount => _engine.ClearableCompletedCount;

    public bool HasCompleted => _engine.CompletedCount > 0;

    public string CompletedFooter => $"共 {_engine.CompletedCount} 条 · 可清除 {_engine.ClearableCompletedCount} 条";

    public string LastAppliedText
    {
        get => _lastAppliedText;
        private set => SetProperty(ref _lastAppliedText, value);
    }

    public string ToastText
    {
        get => _toastText;
        private set => SetProperty(ref _toastText, value);
    }

    public bool ToastVisible
    {
        get => _toastVisible;
        private set => SetProperty(ref _toastVisible, value);
    }

    public bool CountdownVisible
    {
        get => _countdownVisible;
        private set => SetProperty(ref _countdownVisible, value);
    }

    public bool CanCancelCountdown
    {
        get => _canCancelCountdown;
        private set => SetProperty(ref _canCancelCountdown, value);
    }

    public bool CanConfirmManual
    {
        get => _canConfirmManual;
        private set => SetProperty(ref _canConfirmManual, value);
    }

    public string ManualConfirmText
    {
        get => _manualConfirmText;
        private set => SetProperty(ref _manualConfirmText, value);
    }

    public string CountdownText
    {
        get => _countdownText;
        private set => SetProperty(ref _countdownText, value);
    }

    public bool ReminderVisible
    {
        get => _reminderVisible;
        private set => SetProperty(ref _reminderVisible, value);
    }

    public string ReminderTitle
    {
        get => _reminderTitle;
        private set => SetProperty(ref _reminderTitle, value);
    }

    public string ReminderMeta
    {
        get => _reminderMeta;
        private set => SetProperty(ref _reminderMeta, value);
    }

    public string ReminderBody
    {
        get => _reminderBody;
        private set => SetProperty(ref _reminderBody, value);
    }

    public void Refresh()
    {
        var all = _engine.Data.Tasks;
        Filters[0].Count = all.Count(t => !t.IsCompleted);
        Filters[1].Count = _engine.PendingTotal;

        // 已完成的任务不再留在队列里，统一进入「已完成」窗口。
        IEnumerable<TaskItem> source = all.Where(t => !t.IsCompleted);
        if ((SelectedFilter?.Value ?? TaskFilter.Queue) == TaskFilter.Pending)
        {
            source = all.Where(t => t.IsPendingEffect);
        }

        VisibleTasks.Clear();
        foreach (var task in _engine.BuildDisplayOrder(source))
        {
            VisibleTasks.Add(task);
        }

        PendingGroups.Clear();
        foreach (var effect in QueueEngine.BatchEffects)
        {
            var items = _engine.PendingFor(effect);
            if (items.Count == 0)
            {
                continue;
            }

            var preview = string.Join("  ·  ", items.Take(3).Select(t => t.Title));
            if (items.Count > 3)
            {
                preview += "  …";
            }

            PendingGroups.Add(new PendingGroup
            {
                Effect = effect,
                Count = items.Count,
                Preview = preview
            });
        }

        NextUp.Clear();
        foreach (var task in _engine.GetNextUp(4))
        {
            NextUp.Add(task);
        }

        var previousPending = _pendingTotal;
        _pendingTotal = _engine.PendingTotal;
        ActiveSummary = $"{_engine.ActiveCount} 项进行中";
        PendingSummary = $"{_pendingTotal} 项待生效";
        CompletedSummary = $"已完成 {_engine.CompletedCount}";

        var dominant = _pendingTotal == 0
            ? (TaskEffect?)null
            : _engine.PendingCount(TaskEffect.Shutdown) > 0
                ? TaskEffect.Shutdown
                : _engine.PendingCount(TaskEffect.Reboot) > 0
                    ? TaskEffect.Reboot
                    : TaskEffect.Sleep;

        _pendingDominantEffect = dominant;
        PendingIndicatorBrush = dominant switch
        {
            TaskEffect.Shutdown => new SolidColorBrush(MediaColor.FromRgb(0xF5, 0x8C, 0x8C)),
            TaskEffect.Reboot => new SolidColorBrush(MediaColor.FromRgb(0xF2, 0xC8, 0x79)),
            TaskEffect.Sleep => new SolidColorBrush(MediaColor.FromRgb(0xBF, 0xAE, 0xFF)),
            _ => MediaBrushes.Transparent
        };
        PendingBadgeText = _pendingTotal > 99 ? "99+" : _pendingTotal.ToString();

        var normal = _engine.NormalRemaining();
        HasNormalWarning = _pendingTotal > 0 && normal.Count > 0;
        NormalWarningText = HasNormalWarning
            ? $"还有 {normal.Count} 项常规任务未完成，建议先做完再重启。"
            : string.Empty;

        var latest = _engine.Data.History.FirstOrDefault();
        LastAppliedText = latest is null
            ? "尚无生效记录"
            : $"{latest.Kind.ShortLabel()} · {latest.AppliedAt:MM-dd HH:mm} · {latest.TaskCount} 项";

        OnPropertyChanged(nameof(PendingTotal));
        OnPropertyChanged(nameof(PendingDominantEffect));
        OnPropertyChanged(nameof(PendingHeadline));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(HasVisibleTasks));
        OnPropertyChanged(nameof(HasNextUp));
        OnPropertyChanged(nameof(HasNormalWarning));
        OnPropertyChanged(nameof(NormalWarningText));
        OnPropertyChanged(nameof(ActiveSummary));
        OnPropertyChanged(nameof(PendingSummary));
        OnPropertyChanged(nameof(CompletedSummary));
        OnPropertyChanged(nameof(CompletedTasks));
        OnPropertyChanged(nameof(ClearableCompletedCount));
        OnPropertyChanged(nameof(HasCompleted));
        OnPropertyChanged(nameof(CompletedFooter));

        // 首次加载不响，之后每多出一条待生效任务就提醒一次。
        if (_pendingBaselineReady && _pendingTotal > previousPending)
        {
            PlayPromptSound();
        }

        _pendingBaselineReady = true;
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>界面提示音。只发声，不触碰通知中心。</summary>
    public static void PlayPromptSound()
    {
        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch
        {
            // 设备没有音频输出时静默失败。
        }
    }

    public bool ToggleCompletion(TaskItem task, out string? error)
    {
        var target = !task.IsCompleted;
        if (!_engine.TrySetCompleted(task, target, out error))
        {
            ShowToast(error ?? "无法修改这个任务。");
            return false;
        }

        if (target && task.IsPendingEffect)
        {
            ShowToast($"已加入{task.Effect.ShortLabel()}批次（共 {_engine.PendingCount(task.Effect)} 项等待生效）。");
        }
        else if (!target)
        {
            ShowToast("已恢复为未完成。");
        }
        else
        {
            ShowToast("任务已完成，已移入「已完成」。");
        }

        return true;
    }

    public void DeleteTask(TaskItem task)
    {
        if (_engine.TryDeleteTask(task))
        {
            ShowToast("任务已删除。");
        }
    }

    /// <summary>把已完成的任务恢复到队列，依赖它的已完成任务会阻止这次恢复。</summary>
    public bool RestoreCompleted(TaskItem task) => RestoreCompleted(task, out _);

    public bool RestoreCompleted(TaskItem task, out string? error)
    {
        if (!_engine.TrySetCompleted(task, false, out error))
        {
            ShowToast(error ?? "无法恢复这个任务。");
            return false;
        }

        ShowToast($"「{task.Title}」已回到队列。");
        return true;
    }

    /// <summary>清除已生效的完成记录，仍在等待生效的任务会保留。</summary>
    public int ClearCompleted()
    {
        var count = _engine.ClearCompleted();
        ShowToast(count == 0
            ? "没有可以清除的完成记录。"
            : $"已清除 {count} 条完成记录。");
        return count;
    }

    public void ShowReminder(CycleReminder reminder)
    {
        ReminderTitle = reminder.Kind switch
        {
            TaskEffect.Reboot => "重启已完成",
            TaskEffect.Shutdown => "关机已完成",
            TaskEffect.Sleep => "睡眠已完成",
            _ => "批次已完成"
        };

        ReminderMeta = reminder.AppliedCount > 0
            ? $"{reminder.AppliedCount} 项任务已生效"
            : "批次已确认";

        if (reminder.NormalRemaining > 0)
        {
            var names = string.Join("、", reminder.NormalTitles);
            var suffix = reminder.NormalRemaining > reminder.NormalTitles.Count ? " 等" : string.Empty;
            ReminderBody = $"还有常规任务未完成（{reminder.NormalRemaining} 项）：{names}{suffix}";
        }
        else
        {
            ReminderBody = "常规任务已清空，可以继续下一批。";
        }

        ReminderVisible = true;
    }

    /// <summary>
    /// 进入倒计时提示。应用不会执行任何电源操作，倒计时只用于让用户主动确认。
    /// </summary>
    public void RequestPower(TaskEffect effect)
    {
        var count = _engine.PendingCount(effect);
        if (count == 0)
        {
            ShowToast("当前没有待生效任务。");
            return;
        }

        _countdownEffect = effect;
        _countdownSeconds = 10;
        CountdownVisible = true;
        CanCancelCountdown = true;
        CanConfirmManual = false;
        UpdateCountdownText();
        _countdownTimer.Stop();
        _countdownTimer.Start();
    }

    public void MarkApplied(TaskEffect effect)
    {
        var count = _engine.PendingCount(effect);
        if (count == 0)
        {
            return;
        }

        _engine.ApplyEffect(effect, "手动确认生效");
        var normal = _engine.NormalRemaining();
        ShowReminder(new CycleReminder
        {
            Kind = effect,
            AppliedCount = count,
            NormalRemaining = normal.Count,
            NormalTitles = normal.Take(4).Select(t => t.Title).ToList(),
            AppliedAt = DateTime.Now
        });
        ShowToast($"{count} 项任务已标记为生效。");
    }

    public void CancelCountdown()
    {
        _countdownTimer.Stop();
        CountdownVisible = false;
        CanCancelCountdown = false;
        CanConfirmManual = false;
        ShowToast("已收起本次提示，任务仍在待生效队列里。");
    }

    private void ConfirmManual()
    {
        if (!CanConfirmManual)
        {
            return;
        }

        var effect = _countdownEffect;
        _countdownTimer.Stop();
        CountdownVisible = false;
        CanConfirmManual = false;
        MarkApplied(effect);
    }

    public void SaveNow()
    {
        if (!_persist)
        {
            return;
        }

        try
        {
            _store.Save(_engine.Data);
        }
        catch
        {
            // 退出前的最后一次保存失败时不再打扰用户。
        }
    }

    public void ShowToast(string message)
    {
        ToastText = message;
        ToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _countdownTimer.Stop();
        _toastTimer.Stop();
        _engine.Changed -= EngineChanged;
    }

    private void EngineChanged(object? sender, EventArgs e)
    {
        if (_persist)
        {
            try
            {
                _store.Save(_engine.Data);
            }
            catch (Exception ex)
            {
                ShowToast($"保存失败：{ex.Message}");
            }
        }

        Refresh();
    }

    private void AddTask()
    {
        var title = NewTaskTitle?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ShowToast("先写下一个任务。");
            return;
        }

        var effect = NewTaskEffect;
        var task = _engine.TryAddTask(title, effect, string.Empty, null, null, out var error);
        if (task is null)
        {
            ShowToast(error ?? "无法添加任务。");
            return;
        }

        NewTaskTitle = string.Empty;
        NewTaskEffect = TaskEffect.None;
        ShowToast(effect == TaskEffect.None
            ? "已加入队列。"
            : "已加入队列，完成打勾后会并入重启批次。");
    }

    private void ShowNormalTasks()
    {
        SelectedFilter = Filters[0];
        ShowToast("已筛出未完成的任务。");
    }

    private void CountdownTick(object? sender, EventArgs e)
    {
        _countdownSeconds--;
        if (_countdownSeconds <= 0)
        {
            _countdownTimer.Stop();
            CanCancelCountdown = false;
            CanConfirmManual = true;
            ManualConfirmText = _countdownEffect switch
            {
                TaskEffect.Reboot => "我已重启",
                TaskEffect.Shutdown => "我已关机",
                TaskEffect.Sleep => "我已唤醒",
                _ => "我已处理"
            };
            CountdownText = _countdownEffect switch
            {
                TaskEffect.Reboot => "现在可以手动重启 Windows。本软件不会执行重启。",
                TaskEffect.Shutdown => "现在可以手动关机。本软件不会执行关机。",
                TaskEffect.Sleep => "现在可以手动睡眠。本软件不会执行睡眠。",
                _ => "可以手动完成系统操作了。"
            };
            return;
        }

        UpdateCountdownText();
    }

    private void UpdateCountdownText()
    {
        var action = _countdownEffect switch
        {
            TaskEffect.Reboot => "重启",
            TaskEffect.Shutdown => "关机",
            TaskEffect.Sleep => "睡眠",
            _ => "操作"
        };
        CountdownText = $"{_countdownSeconds} 秒后提示你手动{action}（本软件不会自动执行）。";
    }
}
