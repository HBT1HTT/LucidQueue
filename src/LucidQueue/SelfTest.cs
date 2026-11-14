using System.Text;
using LucidQueue.Models;
using LucidQueue.Services;

namespace LucidQueue;

public static class SelfTest
{
    public static int Run(string[] args)
    {
        var passed = 0;
        var failed = 0;
        var lines = new List<string>();

        void Assert(string name, bool condition, string? detail = null)
        {
            if (condition)
            {
                passed++;
                lines.Add($"[PASS] {name}");
            }
            else
            {
                failed++;
                lines.Add($"[FAIL] {name}{(detail is null ? string.Empty : " — " + detail)}");
            }
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "LucidQueue-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            // 1. 添加、完成、参数与持久化往返
            var store = new DataStore(Path.Combine(tempRoot, "queue.json"));
            var engine = new QueueEngine(new AppData());
            var backup = engine.TryAddTask(
                "导出注册表备份",
                TaskEffect.None,
                "导出 HKLM",
                new Dictionary<string, string> { ["target"] = "D:\\Backup\\registry" },
                null,
                out var error);
            var driver = engine.TryAddTask(
                "更新显卡驱动",
                TaskEffect.Reboot,
                "Studio 驱动",
                new Dictionary<string, string> { ["channel"] = "Studio" },
                backup is null ? null : new[] { backup.Id },
                out error);

            Assert("创建任务", backup is not null && driver is not null, error);
            Assert("标签与参数写入", driver!.Effect == TaskEffect.Reboot && driver.Parameters["channel"] == "Studio");
            Assert("前置任务挡住后续任务", !engine.TrySetCompleted(driver, true, out var blockedError) && blockedError!.Contains("前置"));

            Assert("完成前置任务", engine.TrySetCompleted(backup!, true, out error), error);
            Assert("前置完成后可勾选", engine.TrySetCompleted(driver, true, out error), error);
            Assert("重启任务进入待生效", driver.IsPendingEffect && engine.PendingCount(TaskEffect.Reboot) == 1);

            store.Save(engine.Data);
            var reloaded = new DataStore(store.FilePath).Load(out var warning);
            var reloadedEngine = new QueueEngine(reloaded);
            var reloadedDriver = reloadedEngine.Data.Tasks.FirstOrDefault(t => t.Title == "更新显卡驱动");
            Assert("JSON 持久化往返", reloadedDriver is not null
                                     && reloadedDriver.Effect == TaskEffect.Reboot
                                     && reloadedDriver.IsCompleted
                                     && reloadedDriver.DependsOn.Count == 1
                                     && warning is null);

            // 2. 依赖环检测
            var cycleEngine = new QueueEngine(new AppData());
            var first = cycleEngine.TryAddTask("A", TaskEffect.None, null, null, null, out _)!;
            var second = cycleEngine.TryAddTask("B", TaskEffect.None, null, null, null, out _)!;
            cycleEngine.TryUpdateTask(first, "A", TaskEffect.None, "", null, new[] { second.Id }, out _);
            var cycleRejected = !cycleEngine.TryUpdateTask(
                second, "B", TaskEffect.None, "", null, new[] { first.Id }, out var cycleError);
            Assert("拒绝依赖环", cycleRejected && cycleError!.Contains("循环"));

            // 3. 已完成任务的依赖保护
            var protectEngine = new QueueEngine(new AppData());
            var a = protectEngine.TryAddTask("A", TaskEffect.None, null, null, null, out _)!;
            var b = protectEngine.TryAddTask("B", TaskEffect.None, null, null, new[] { a.Id }, out _)!;
            protectEngine.TrySetCompleted(a, true, out _);
            protectEngine.TrySetCompleted(b, true, out _);
            var protectedOk = !protectEngine.TrySetCompleted(a, false, out var protectError)
                              && protectError!.Contains("依赖");
            Assert("已完成的下游任务保护前置", protectedOk);

            var pendingTask = protectEngine.TryAddTask("C", TaskEffect.None, null, null, null, out _)!;
            var completedDepRejected = !protectEngine.TryUpdateTask(
                a, "A", TaskEffect.None, "", null, new[] { pendingTask.Id }, out var completedDepError)
                                        && completedDepError!.Contains("未完成");
            Assert("已完成任务不能依赖未完成任务", completedDepRejected);

            // 4. 批处理归组
            var batchEngine = new QueueEngine(new AppData());
            var r1 = batchEngine.TryAddTask("改页面文件", TaskEffect.Reboot, null, null, null, out _)!;
            var r2 = batchEngine.TryAddTask("启用 Hyper-V", TaskEffect.Reboot, null, null, null, out _)!;
            var s1 = batchEngine.TryAddTask("刷新网络栈", TaskEffect.Sleep, null, null, null, out _)!;
            batchEngine.TryAddTask("写作业", TaskEffect.None, null, null, null, out _);
            batchEngine.TrySetCompleted(r1, true, out _);
            batchEngine.TrySetCompleted(r2, true, out _);
            batchEngine.TrySetCompleted(s1, true, out _);
            Assert("按类型合并批次", batchEngine.PendingCount(TaskEffect.Reboot) == 2
                                     && batchEngine.PendingCount(TaskEffect.Sleep) == 1
                                     && batchEngine.PendingTotal == 3);

            var record = batchEngine.ApplyEffect(TaskEffect.Reboot, "selftest");
            Assert("应用重启批次", record.TaskCount == 2
                                   && batchEngine.PendingCount(TaskEffect.Reboot) == 0
                                   && batchEngine.PendingCount(TaskEffect.Sleep) == 1
                                   && batchEngine.Data.History.Count == 1);

            // 5. 手动确认后提醒常规任务（应用本身不执行任何电源操作）
            var cycleStore = new DataStore(Path.Combine(tempRoot, "cycle.json"));
            var startupEngine = new QueueEngine(new AppData());
            var restartTask = startupEngine.TryAddTask("固定页面文件", TaskEffect.Reboot, null, null, null, out _)!;
            startupEngine.TryAddTask("写作业", TaskEffect.None, null, null, null, out _);
            startupEngine.TrySetCompleted(restartTask, true, out _);
            cycleStore.Save(startupEngine.Data);

            var bootData = new DataStore(cycleStore.FilePath).Load(out _);
            var bootEngine = new QueueEngine(bootData);
            var applied = bootEngine.ApplyEffect(TaskEffect.Reboot, "手动确认生效");
            var normalAfter = bootEngine.NormalRemaining();
            Assert("手动确认批次生效", applied.TaskCount == 1
                                        && bootEngine.PendingCount(TaskEffect.Reboot) == 0);
            Assert("确认后提醒常规任务", normalAfter.Count == 1 && normalAfter[0].Title == "写作业");

            // 6. 依赖拓扑排序
            var orderEngine = new QueueEngine(new AppData());
            var later = orderEngine.TryAddTask("后置任务", TaskEffect.None, null, null, null, out _)!;
            var earlier = orderEngine.TryAddTask("前置任务", TaskEffect.None, null, null, null, out _)!;
            orderEngine.TryUpdateTask(later, "后置任务", TaskEffect.None, "", null, new[] { earlier.Id }, out _);
            var order = orderEngine.BuildDisplayOrder(orderEngine.Data.Tasks);
            Assert("依赖优先排序", order.FindIndex(t => t.Id == earlier.Id) < order.FindIndex(t => t.Id == later.Id));

            // 7. 常规任务计数与删除联动
            var miscEngine = new QueueEngine(new AppData());
            var normal = miscEngine.TryAddTask("常规", TaskEffect.None, null, null, null, out _)!;
            var shutdown = miscEngine.TryAddTask("关机", TaskEffect.Shutdown, null, null, null, out _)!;
            var dependent = miscEngine.TryAddTask("依赖关机", TaskEffect.None, null, null, new[] { shutdown.Id }, out _)!;
            Assert("常规任务统计只算常规", miscEngine.NormalRemaining().Count == 2);
            miscEngine.TryDeleteTask(shutdown);
            Assert("删除任务清理依赖", dependent.DependsOn.Count == 0 && !miscEngine.Data.Tasks.Contains(shutdown));

            // 8. 修改生效类型会让已完成任务重新进入批次
            var effectEngine = new QueueEngine(new AppData());
            var normalTask = effectEngine.TryAddTask("普通任务", TaskEffect.None, null, null, null, out _)!;
            effectEngine.TrySetCompleted(normalTask, true, out _);
            effectEngine.TryUpdateTask(normalTask, "普通任务改为重启", TaskEffect.Reboot, "", null, null, out _);
            Assert("修改标签后重新待生效", normalTask.IsPendingEffect);

            // 9. 已完成分区、清除与恢复
            var doneEngine = new QueueEngine(new AppData());
            var doneNormal = doneEngine.TryAddTask("写作业", TaskEffect.None, null, null, null, out _)!;
            var doneReboot = doneEngine.TryAddTask("改页面文件", TaskEffect.Reboot, null, null, null, out _)!;
            var doneDependent = doneEngine.TryAddTask(
                "重启后验证", TaskEffect.None, null, null, new[] { doneNormal.Id }, out _)!;
            doneEngine.TrySetCompleted(doneNormal, true, out _);
            doneEngine.TrySetCompleted(doneReboot, true, out _);

            Assert("完成的任务离开队列", doneEngine.ActiveCount == 1
                                         && doneEngine.GetNextUp(10).All(t => !t.IsCompleted));
            Assert("待生效任务单独保留", doneEngine.PendingCount(TaskEffect.Reboot) == 1
                                          && doneEngine.CompletedCount == 2
                                          && doneEngine.ClearableCompletedCount == 1);

            Assert("恢复已完成任务", doneEngine.TrySetCompleted(doneReboot, false, out var restoreError)
                                     && !doneReboot.IsCompleted
                                     && restoreError is null);
            doneEngine.TrySetCompleted(doneReboot, true, out _);

            var removedCount = doneEngine.ClearCompleted();
            Assert("清除已完成并保留待生效", removedCount == 1
                                             && !doneEngine.Data.Tasks.Contains(doneNormal)
                                             && doneEngine.Data.Tasks.Contains(doneReboot)
                                             && doneDependent.DependsOn.Count == 0);

            // 10. 数据目录与文案
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert("数据目录位于 LocalAppData", AppPaths.DataDirectory.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase));
            Assert("批次按钮带数量", TaskEffect.Reboot.BatchActionText(3) == "应用并重启（3 项）"
                                     && TaskEffect.Shutdown.BatchActionText(2) == "应用并关机（2 项）");

            lines.Add(string.Empty);
            lines.Add($"RESULT: {passed} passed, {failed} failed");
        }
        catch (Exception ex)
        {
            failed++;
            lines.Add($"[FAIL] 未捕获异常 — {ex}");
            lines.Add(string.Empty);
            lines.Add($"RESULT: {passed} passed, {failed} failed");
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
                // 临时目录清理失败不影响结果。
            }
        }

        var logPath = ResolveLogPath(args);
        try
        {
            File.WriteAllText(logPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        }
        catch
        {
            // 写日志失败时只能依赖返回值。
        }

        return failed == 0 ? 0 : 1;
    }

    private static string ResolveLogPath(string[] args)
    {
        var index = Array.IndexOf(args, "--selftest");
        if (index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return Path.GetFullPath(args[index + 1]);
        }

        return Path.Combine(Path.GetTempPath(), "LucidQueue-selftest.log");
    }
}
