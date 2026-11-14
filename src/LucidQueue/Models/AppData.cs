namespace LucidQueue.Models;

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 1;

    public List<TaskItem> Tasks { get; set; } = new();

    public List<CycleRecord> History { get; set; } = new();

    public AppSettings Settings { get; set; } = new();
}

public sealed class CycleRecord
{
    public TaskEffect Kind { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.Now;

    public int TaskCount { get; set; }

    public string Note { get; set; } = string.Empty;
}

public sealed class AppSettings
{
    public bool MinimizeToTray { get; set; } = true;
}

public sealed class CycleReminder
{
    public TaskEffect Kind { get; set; }

    public int AppliedCount { get; set; }

    public int NormalRemaining { get; set; }

    public List<string> NormalTitles { get; set; } = new();

    public DateTime AppliedAt { get; set; }
}
