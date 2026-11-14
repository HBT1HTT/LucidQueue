using System.Text.Json.Serialization;
using LucidQueue.Common;

namespace LucidQueue.Models;

public sealed class TaskItem : ObservableObject
{
    private string _title = string.Empty;
    private string _notes = string.Empty;
    private TaskEffect _effect;
    private bool _isCompleted;
    private bool _effectApplied;
    private bool _isBlocked;
    private string _blockedByText = string.Empty;
    private string _dependencySummary = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (SetProperty(ref _notes, value))
            {
                OnPropertyChanged(nameof(HasNotes));
            }
        }
    }

    public TaskEffect Effect
    {
        get => _effect;
        set
        {
            if (SetProperty(ref _effect, value))
            {
                OnPropertyChanged(nameof(EffectLabel));
                OnPropertyChanged(nameof(IsPendingEffect));
                OnPropertyChanged(nameof(PendingLabel));
            }
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (SetProperty(ref _isCompleted, value))
            {
                OnPropertyChanged(nameof(IsPendingEffect));
                OnPropertyChanged(nameof(PendingLabel));
            }
        }
    }

    public bool EffectApplied
    {
        get => _effectApplied;
        set
        {
            if (SetProperty(ref _effectApplied, value))
            {
                OnPropertyChanged(nameof(IsPendingEffect));
                OnPropertyChanged(nameof(PendingLabel));
            }
        }
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public DateTime? CompletedAt { get; set; }

    public DateTime? AppliedAt { get; set; }

    /// <summary>当前任务必须在其全部完成之后才能打勾。</summary>
    public List<Guid> DependsOn { get; set; } = new();

    public Dictionary<string, string> Parameters { get; set; } = new();

    [JsonIgnore]
    public bool IsPendingEffect => Effect != TaskEffect.None && IsCompleted && !EffectApplied;

    [JsonIgnore]
    public string EffectLabel => Effect.Label();

    [JsonIgnore]
    public string PendingLabel => Effect.PendingText();

    [JsonIgnore]
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    /// <summary>完成时间的显示文本；旧数据缺少 CompletedAt 时回退到更新时间。</summary>
    [JsonIgnore]
    public string CompletedWhenText => $"{(CompletedAt ?? UpdatedAt):MM-dd HH:mm} · 完成";

    [JsonIgnore]
    public bool HasParameters => ParameterCount > 0;

    [JsonIgnore]
    public int ParameterCount => Parameters?.Count ?? 0;

    [JsonIgnore]
    public string ParameterSummary => ParameterCount == 0 ? string.Empty : $"参数 · {ParameterCount}";

    [JsonIgnore]
    public bool IsBlocked
    {
        get => _isBlocked;
        internal set
        {
            if (SetProperty(ref _isBlocked, value))
            {
                OnPropertyChanged(nameof(IsReady));
            }
        }
    }

    [JsonIgnore]
    public bool IsReady => !IsCompleted && !IsBlocked;

    [JsonIgnore]
    public string BlockedByText
    {
        get => _blockedByText;
        internal set
        {
            if (SetProperty(ref _blockedByText, value))
            {
                OnPropertyChanged(nameof(HasBlockedText));
            }
        }
    }

    [JsonIgnore]
    public bool HasBlockedText => !string.IsNullOrWhiteSpace(BlockedByText);

    [JsonIgnore]
    public string DependencySummary
    {
        get => _dependencySummary;
        internal set
        {
            if (SetProperty(ref _dependencySummary, value))
            {
                OnPropertyChanged(nameof(HasDependencySummary));
            }
        }
    }

    [JsonIgnore]
    public bool HasDependencySummary => !string.IsNullOrWhiteSpace(DependencySummary);

    public void NotifyParametersChanged()
    {
        OnPropertyChanged(nameof(ParameterCount));
        OnPropertyChanged(nameof(ParameterSummary));
        OnPropertyChanged(nameof(HasParameters));
    }
}
