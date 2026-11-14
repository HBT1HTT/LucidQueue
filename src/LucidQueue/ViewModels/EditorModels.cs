using LucidQueue.Common;
using LucidQueue.Models;

namespace LucidQueue.ViewModels;

public sealed class DependencyOption : ObservableObject
{
    private bool _isSelected;

    public DependencyOption(TaskItem task, bool isSelected)
    {
        Task = task;
        _isSelected = isSelected;
    }

    public TaskItem Task { get; }

    public string Title => Task.Title;

    public string Meta => Task.IsCompleted ? $"{Task.EffectLabel} · 已完成" : Task.EffectLabel;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class ParameterRow : ObservableObject
{
    private string _key = string.Empty;
    private string _value = string.Empty;

    public ParameterRow(string key = "", string value = "")
    {
        _key = key;
        _value = value;
    }

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}
