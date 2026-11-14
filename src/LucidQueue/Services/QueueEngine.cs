using LucidQueue.Models;

namespace LucidQueue.Services;

/// <summary>
/// 纯内存的队列逻辑：标签、依赖、批处理与生效确认。
/// 不读取任何系统状态，所有完成与生效都由用户或应用内的记录确认。
/// </summary>
public sealed class QueueEngine
{
    private static readonly TaskEffect[] BatchOrder =
    {
        TaskEffect.Reboot, TaskEffect.Shutdown, TaskEffect.Sleep
    };

    public QueueEngine(AppData data)
    {
        Data = data ?? new AppData();
        Sanitize();
        RefreshDerived();
    }

    public AppData Data { get; }

    public event EventHandler? Changed;

    public static IReadOnlyList<TaskEffect> BatchEffects => BatchOrder;

    public TaskItem? FindTask(Guid id) => Data.Tasks.FirstOrDefault(t => t.Id == id);

    public IReadOnlyList<TaskItem> PendingFor(TaskEffect effect) => Data.Tasks
        .Where(t => t.Effect == effect && t.IsCompleted && !t.EffectApplied)
        .OrderBy(t => t.CompletedAt ?? t.UpdatedAt)
        .ToList();

    public int PendingCount(TaskEffect effect) => Data.Tasks.Count(t => t.Effect == effect && t.IsCompleted && !t.EffectApplied);

    public int PendingTotal => Data.Tasks.Count(t => t.IsPendingEffect);

    public int ActiveCount => Data.Tasks.Count(t => !t.IsCompleted);

    public int CompletedCount => Data.Tasks.Count(t => t.IsCompleted);

    public IReadOnlyList<TaskItem> NormalRemaining() => Data.Tasks
        .Where(t => !t.IsCompleted && t.Effect == TaskEffect.None)
        .OrderBy(t => t.CreatedAt)
        .ToList();

    public IReadOnlyList<TaskItem> GetNextUp(int max) => BuildDisplayOrder(Data.Tasks.Where(t => !t.IsCompleted))
        .Where(t => t.IsReady)
        .Take(max)
        .ToList();

    /// <summary>已完成的记录，最新的在前。待生效的任务也在这里，直到确认生效。</summary>
    public IReadOnlyList<TaskItem> CompletedTasks => Data.Tasks
        .Where(t => t.IsCompleted)
        .OrderByDescending(t => t.IsPendingEffect)
        .ThenByDescending(t => t.CompletedAt ?? t.UpdatedAt)
        .ToList();

    /// <summary>可以被清除的已完成记录数量：已生效的那些。待生效任务必须保留，否则会丢掉批次信息。</summary>
    public int ClearableCompletedCount => Data.Tasks.Count(t => t.IsCompleted && t.EffectApplied);

    /// <summary>清除已生效的完成记录，并同步清理对它们的依赖引用。</summary>
    public int ClearCompleted()
    {
        var removable = Data.Tasks.Where(t => t.IsCompleted && t.EffectApplied).ToList();
        if (removable.Count == 0)
        {
            return 0;
        }

        var ids = removable.Select(t => t.Id).ToHashSet();
        foreach (var task in removable)
        {
            Data.Tasks.Remove(task);
        }

        foreach (var other in Data.Tasks)
        {
            other.DependsOn.RemoveAll(id => ids.Contains(id));
        }

        RefreshDerived();
        RaiseChanged();
        return removable.Count;
    }

    public TaskItem? TryAddTask(
        string title,
        TaskEffect effect,
        string? notes,
        IReadOnlyDictionary<string, string>? parameters,
        IEnumerable<Guid>? dependsOn,
        out string? error)
    {
        error = null;
        var cleanTitle = title?.Trim() ?? string.Empty;
        if (cleanTitle.Length == 0)
        {
            error = "任务标题不能为空。";
            return null;
        }

        var task = new TaskItem
        {
            Title = cleanTitle,
            Notes = notes?.Trim() ?? string.Empty,
            Effect = NormalizeEffect(effect),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        task.DependsOn = NormalizeDependencies(dependsOn, task.Id);
        task.Parameters = NormalizeParameters(parameters);

        Data.Tasks.Add(task);
        RefreshDerived();
        RaiseChanged();
        return task;
    }

    public bool TryUpdateTask(
        TaskItem task,
        string title,
        TaskEffect effect,
        string? notes,
        IReadOnlyDictionary<string, string>? parameters,
        IEnumerable<Guid>? dependsOn,
        out string? error)
    {
        error = null;
        var cleanTitle = title?.Trim() ?? string.Empty;
        if (cleanTitle.Length == 0)
        {
            error = "任务标题不能为空。";
            return false;
        }

        var map = Data.Tasks.ToDictionary(t => t.Id);
        var cleanDeps = NormalizeDependencies(dependsOn, task.Id);
        if (WouldCreateCycle(task.Id, cleanDeps, map))
        {
            error = "依赖关系会形成循环，请调整后再保存。";
            return false;
        }

        // 已完成的任务不能再依赖未完成的任务，否则队列顺序会自相矛盾。
        if (task.IsCompleted)
        {
            var unmet = cleanDeps
                .Select(id => map.GetValueOrDefault(id))
                .FirstOrDefault(t => t is not null && !t.IsCompleted);
            if (unmet is not null)
            {
                error = $"已完成的任务不能依赖未完成的任务「{unmet.Title}」。";
                return false;
            }
        }

        var effectChanged = task.Effect != NormalizeEffect(effect);
        task.Title = cleanTitle;
        task.Notes = notes?.Trim() ?? string.Empty;
        task.Effect = NormalizeEffect(effect);
        task.DependsOn = cleanDeps;
        task.Parameters = NormalizeParameters(parameters);
        task.NotifyParametersChanged();
        task.UpdatedAt = DateTime.Now;

        if (effectChanged)
        {
            if (task.IsCompleted)
            {
                task.EffectApplied = task.Effect == TaskEffect.None;
                task.AppliedAt = task.Effect == TaskEffect.None ? DateTime.Now : null;
            }
            else
            {
                task.EffectApplied = false;
                task.AppliedAt = null;
            }
        }

        RefreshDerived();
        RaiseChanged();
        return true;
    }

    public bool TrySetCompleted(TaskItem task, bool completed, out string? error)
    {
        error = null;
        if (completed)
        {
            if (task.IsCompleted)
            {
                return true;
            }

            if (task.IsBlocked)
            {
                error = $"还有前置任务未完成：{task.BlockedByText.Replace("等待：", string.Empty)}";
                return false;
            }

            task.IsCompleted = true;
            task.CompletedAt = DateTime.Now;
            task.UpdatedAt = DateTime.Now;
            if (task.Effect == TaskEffect.None)
            {
                task.EffectApplied = true;
                task.AppliedAt = DateTime.Now;
            }
            else
            {
                task.EffectApplied = false;
                task.AppliedAt = null;
            }
        }
        else
        {
            if (!task.IsCompleted)
            {
                return true;
            }

            var completedDependents = Data.Tasks
                .Where(t => t.Id != task.Id && t.IsCompleted && t.DependsOn.Contains(task.Id))
                .ToList();
            if (completedDependents.Count > 0)
            {
                error = $"「{completedDependents[0].Title}」已完成并依赖此任务，请先取消它的完成状态。";
                return false;
            }

            task.IsCompleted = false;
            task.CompletedAt = null;
            task.EffectApplied = false;
            task.AppliedAt = null;
            task.UpdatedAt = DateTime.Now;
        }

        RefreshDerived();
        RaiseChanged();
        return true;
    }

    public bool TryDeleteTask(TaskItem task)
    {
        if (!Data.Tasks.Remove(task))
        {
            return false;
        }

        foreach (var other in Data.Tasks)
        {
            other.DependsOn.RemoveAll(id => id == task.Id);
        }

        RefreshDerived();
        RaiseChanged();
        return true;
    }

    /// <summary>把某一类待生效任务标记为已生效，并写入历史。</summary>
    public CycleRecord ApplyEffect(TaskEffect effect, string note = "")
    {
        var now = DateTime.Now;
        var pending = PendingFor(effect);
        foreach (var task in pending)
        {
            task.EffectApplied = true;
            task.AppliedAt = now;
            task.UpdatedAt = now;
        }

        var record = new CycleRecord
        {
            Kind = effect,
            AppliedAt = now,
            TaskCount = pending.Count,
            Note = note
        };

        if (pending.Count > 0)
        {
            Data.History.Insert(0, record);
            TrimHistory();
        }

        RefreshDerived();
        RaiseChanged();
        return record;
    }

    public List<TaskItem> BuildDisplayOrder(IEnumerable<TaskItem> source)
    {
        var tasks = source.ToList();
        if (tasks.Count == 0)
        {
            return tasks;
        }

        var ordered = TopologicalOrder(tasks);
        var active = ordered.Where(t => !t.IsCompleted && !t.IsBlocked).ToList();
        var blocked = ordered.Where(t => !t.IsCompleted && t.IsBlocked).ToList();
        var done = ordered
            .Where(t => t.IsCompleted)
            .OrderByDescending(t => t.IsPendingEffect)
            .ThenByDescending(t => t.CompletedAt ?? t.UpdatedAt)
            .ToList();

        var result = new List<TaskItem>(tasks.Count);
        result.AddRange(active);
        result.AddRange(blocked);
        result.AddRange(done);
        return result;
    }

    private List<TaskItem> TopologicalOrder(IReadOnlyCollection<TaskItem> tasks)
    {
        var map = tasks.ToDictionary(t => t.Id);
        var indegree = new Dictionary<Guid, int>();
        var dependents = new Dictionary<Guid, List<TaskItem>>();

        foreach (var task in tasks)
        {
            indegree[task.Id] = 0;
            dependents[task.Id] = new List<TaskItem>();
        }

        foreach (var task in tasks)
        {
            foreach (var dep in task.DependsOn.Distinct())
            {
                if (!map.ContainsKey(dep))
                {
                    continue;
                }

                indegree[task.Id]++;
                dependents[dep].Add(task);
            }
        }

        var ready = tasks.Where(t => indegree[t.Id] == 0).ToList();
        ready.Sort(CompareByCreated);

        var result = new List<TaskItem>(tasks.Count);
        while (ready.Count > 0)
        {
            var current = ready[0];
            ready.RemoveAt(0);
            result.Add(current);

            foreach (var dependent in dependents[current.Id])
            {
                indegree[dependent.Id]--;
                if (indegree[dependent.Id] == 0)
                {
                    ready.Add(dependent);
                }
            }

            ready.Sort(CompareByCreated);
        }

        if (result.Count < tasks.Count)
        {
            // 理论上不会出现；若数据里有环，剩余任务按创建时间兜底。
            var seen = new HashSet<Guid>(result.Select(t => t.Id));
            result.AddRange(tasks.Where(t => !seen.Contains(t.Id)).OrderBy(t => t.CreatedAt));
        }

        return result;
    }

    private static int CompareByCreated(TaskItem a, TaskItem b)
    {
        var cmp = a.CreatedAt.CompareTo(b.CreatedAt);
        return cmp != 0 ? cmp : a.Id.CompareTo(b.Id);
    }

    private void RefreshDerived()
    {
        var map = Data.Tasks.ToDictionary(t => t.Id);
        foreach (var task in Data.Tasks)
        {
            var blockers = task.DependsOn
                .Select(id => map.GetValueOrDefault(id))
                .Where(t => t is not null && !t.IsCompleted)
                .ToList();

            task.IsBlocked = blockers.Count > 0;
            task.BlockedByText = blockers.Count == 0
                ? string.Empty
                : "等待：" + string.Join("、", blockers.Select(t => t!.Title));

            var dependencyNames = task.DependsOn
                .Select(id => map.GetValueOrDefault(id))
                .Where(t => t is not null)
                .Select(t => t!.Title)
                .ToList();

            task.DependencySummary = dependencyNames.Count == 0
                ? string.Empty
                : "前置 · " + string.Join("、", dependencyNames.Take(2)) + (dependencyNames.Count > 2 ? " 等" : string.Empty);
        }
    }

    private bool WouldCreateCycle(Guid taskId, IEnumerable<Guid> proposed, IReadOnlyDictionary<Guid, TaskItem> map)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>(proposed);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == taskId)
            {
                return true;
            }

            if (!visited.Add(current) || !map.TryGetValue(current, out var task))
            {
                continue;
            }

            foreach (var dep in task.DependsOn)
            {
                stack.Push(dep);
            }
        }

        return false;
    }

    private List<Guid> NormalizeDependencies(IEnumerable<Guid>? dependsOn, Guid selfId)
    {
        if (dependsOn is null)
        {
            return new List<Guid>();
        }

        var known = Data.Tasks.Select(t => t.Id).ToHashSet();
        return dependsOn
            .Where(id => id != selfId && known.Contains(id))
            .Distinct()
            .ToList();
    }

    private static Dictionary<string, string> NormalizeParameters(IReadOnlyDictionary<string, string>? parameters)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parameters is null)
        {
            return result;
        }

        foreach (var pair in parameters)
        {
            var key = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            result[key] = pair.Value?.Trim() ?? string.Empty;
        }

        return result;
    }

    private static TaskEffect NormalizeEffect(TaskEffect effect)
        => Enum.IsDefined(typeof(TaskEffect), effect) ? effect : TaskEffect.None;

    private void Sanitize()
    {
        Data.Tasks ??= new List<TaskItem>();
        Data.History ??= new List<CycleRecord>();
        Data.Settings ??= new AppSettings();

        Data.Tasks = Data.Tasks.Where(t => t is not null).ToList();
        Data.History = Data.History.Where(r => r is not null).ToList();

        var seen = new HashSet<Guid>();
        foreach (var task in Data.Tasks)
        {
            if (task.Id == Guid.Empty || !seen.Add(task.Id))
            {
                task.Id = Guid.NewGuid();
                seen.Add(task.Id);
            }

            task.Title ??= string.Empty;
            task.Notes ??= string.Empty;
            task.Effect = NormalizeEffect(task.Effect);
            task.DependsOn ??= new List<Guid>();
            task.Parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var known = Data.Tasks.Select(t => t.Id).ToHashSet();
        foreach (var task in Data.Tasks)
        {
            task.DependsOn = task.DependsOn
                .Where(id => id != task.Id && known.Contains(id))
                .Distinct()
                .ToList();
        }

        TrimHistory();
    }

    private void TrimHistory()
    {
        if (Data.History.Count > 200)
        {
            Data.History.RemoveRange(200, Data.History.Count - 200);
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
