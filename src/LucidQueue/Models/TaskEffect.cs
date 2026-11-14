namespace LucidQueue.Models;

public enum TaskEffect
{
    None = 0,
    Reboot = 1,
    Shutdown = 2,
    Sleep = 3
}

public static class TaskEffectExtensions
{
    public static string Label(this TaskEffect effect) => effect switch
    {
        TaskEffect.Reboot => "需要重启",
        TaskEffect.Shutdown => "需要关机",
        TaskEffect.Sleep => "需要睡眠",
        _ => "常规"
    };

    public static string ShortLabel(this TaskEffect effect) => effect switch
    {
        TaskEffect.Reboot => "重启",
        TaskEffect.Shutdown => "关机",
        TaskEffect.Sleep => "睡眠",
        _ => "常规"
    };

    public static string PendingText(this TaskEffect effect) => effect switch
    {
        TaskEffect.Reboot => "等待重启生效",
        TaskEffect.Shutdown => "等待关机生效",
        TaskEffect.Sleep => "等待睡眠生效",
        _ => "已生效"
    };

    public static string ActionText(this TaskEffect effect) => effect switch
    {
        TaskEffect.Reboot => "应用并重启",
        TaskEffect.Shutdown => "应用并关机",
        TaskEffect.Sleep => "应用并睡眠",
        _ => "标记已生效"
    };

    public static string BatchActionText(this TaskEffect effect, int count) => effect switch
    {
        TaskEffect.Reboot => $"应用并重启（{count} 项）",
        TaskEffect.Shutdown => $"应用并关机（{count} 项）",
        TaskEffect.Sleep => $"应用并睡眠（{count} 项）",
        _ => $"应用（{count} 项）"
    };

    public static string Description(this TaskEffect effect) => effect switch
    {
        TaskEffect.Reboot => "改动写入后，重启一次才真正生效。",
        TaskEffect.Shutdown => "改动在下次开机时生效，适合驱动与固件。",
        TaskEffect.Sleep => "唤醒后生效，适合网络与热插拔类改动。",
        _ => "不改变系统状态，随时可以完成。"
    };

    public static string BatchTitle(this TaskEffect effect) => effect switch
    {
        TaskEffect.Reboot => "重启批次",
        TaskEffect.Shutdown => "关机批次",
        TaskEffect.Sleep => "睡眠批次",
        _ => "常规批次"
    };

    public static string BatchPrompt(this TaskEffect effect, int count) => effect switch
    {
        TaskEffect.Reboot => $"已有 {count} 项任务等待重启生效，是否现在一键重启？",
        TaskEffect.Shutdown => $"已有 {count} 项任务等待关机生效，是否现在关机？",
        TaskEffect.Sleep => $"已有 {count} 项任务等待睡眠生效，是否现在睡眠？",
        _ => $"已有 {count} 项任务等待生效。"
    };
}
