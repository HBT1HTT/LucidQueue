using LucidQueue.Models;

namespace LucidQueue.Services;

/// <summary>仅用于界面预览与自动化截图，不会写入用户数据文件。</summary>
public static class DemoData
{
    public static AppData Create()
    {
        var now = DateTime.Now;

        var trim = Make(
            "系统盘 TRIM 优化",
            TaskEffect.Reboot,
            now.AddHours(-30),
            now.AddHours(-28),
            applied: true,
            notes: "优化已提交，等待下一次重启后确认。");

        var powerPlan = Make(
            "电源计划切到“卓越性能”",
            TaskEffect.Reboot,
            now.AddHours(-29),
            now.AddHours(-27),
            applied: true);

        var desktop = Make(
            "整理桌面与下载目录",
            TaskEffect.None,
            now.AddHours(-8),
            now.AddHours(-3));

        var driver = Make(
            "更新 NVIDIA 显卡驱动 581.xx",
            TaskEffect.Reboot,
            now.AddHours(-6),
            now.AddMinutes(-55),
            notes: "Studio 驱动。安装后不重启会继续使用旧内核模块。",
            parameters: new Dictionary<string, string>
            {
                ["version"] = "581.xx",
                ["channel"] = "Studio"
            });

        var pagefile = Make(
            "页面文件固定到 D:\\ 32 GB",
            TaskEffect.Reboot,
            now.AddHours(-5),
            now.AddMinutes(-44),
            notes: "固定上下限，避免系统反复调整大小。",
            parameters: new Dictionary<string, string>
            {
                ["drive"] = "D:\\",
                ["initial"] = "32768 MB",
                ["maximum"] = "32768 MB"
            });

        var hyperV = Make(
            "启用 Hyper-V 与 WSL2",
            TaskEffect.Reboot,
            now.AddHours(-4.5),
            now.AddMinutes(-33),
            notes: "功能启用后必须重启，WSL2 才能启动。");

        var homework = Make(
            "写作业（第三章习题）",
            TaskEffect.None,
            now.AddHours(-3),
            null,
            notes: "重启前先确认这一项，别又开机才发现作业没写。");

        var backup = Make(
            "导出注册表与驱动备份",
            TaskEffect.None,
            now.AddHours(-2.5),
            null,
            parameters: new Dictionary<string, string>
            {
                ["target"] = "D:\\Backup\\registry"
            });

        var prefetch = Make(
            "调整注册表 Prefetch 参数",
            TaskEffect.Shutdown,
            now.AddHours(-2),
            null,
            notes: "涉及内核行为，安排在下次开机时生效。",
            parameters: new Dictionary<string, string>
            {
                ["key"] = "EnablePrefetcher",
                ["value"] = "3"
            });
        prefetch.DependsOn.Add(backup.Id);

        var verify = Make(
            "更新后验证 WSL 与虚拟化",
            TaskEffect.None,
            now.AddHours(-1.8),
            null,
            notes: "重启后先跑 wsl --status，再启动项目。");
        verify.DependsOn.Add(hyperV.Id);

        var data = new AppData
        {
            Tasks = new List<TaskItem>
            {
                trim, powerPlan, desktop, driver, pagefile, hyperV,
                homework, backup, prefetch, verify
            },
            History = new List<CycleRecord>
            {
                new()
                {
                    Kind = TaskEffect.Reboot,
                    AppliedAt = now.AddHours(-18),
                    TaskCount = 2,
                    Note = "上一轮合并重启"
                }
            }
        };

        return data;
    }

    public static CycleReminder CreateReminder()
    {
        var normal = new[] { "写作业（第三章习题）", "导出注册表与驱动备份", "更新后验证 WSL 与虚拟化" };
        return new CycleReminder
        {
            Kind = TaskEffect.Reboot,
            AppliedCount = 2,
            NormalRemaining = normal.Length,
            NormalTitles = normal.ToList(),
            AppliedAt = DateTime.Now.AddMinutes(-12)
        };
    }

    private static TaskItem Make(
        string title,
        TaskEffect effect,
        DateTime createdAt,
        DateTime? completedAt,
        bool applied = false,
        string notes = "",
        Dictionary<string, string>? parameters = null)
    {
        return new TaskItem
        {
            Title = title,
            Notes = notes,
            Effect = effect,
            IsCompleted = completedAt is not null,
            EffectApplied = applied,
            CreatedAt = createdAt,
            UpdatedAt = completedAt ?? createdAt,
            CompletedAt = completedAt,
            AppliedAt = applied ? completedAt?.AddMinutes(18) : null,
            Parameters = parameters ?? new Dictionary<string, string>()
        };
    }
}
