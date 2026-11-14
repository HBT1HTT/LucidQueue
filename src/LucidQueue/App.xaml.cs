using System.Windows;
using LucidQueue.Models;
using LucidQueue.Services;
using LucidQueue.ViewModels;
using LucidQueue.Views;

namespace LucidQueue;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--selftest"))
        {
            var exitCode = SelfTest.Run(e.Args);
            Shutdown(exitCode);
            return;
        }

        var captureIndex = Array.IndexOf(e.Args, "--capture");
        var capturePath = captureIndex >= 0 && captureIndex + 1 < e.Args.Length
            ? Path.ChangeExtension(e.Args[captureIndex + 1], ".png")
            : null;
        var demo = e.Args.Contains("--demo") || capturePath is not null;

        var dataIndex = Array.IndexOf(e.Args, "--data");
        var dataPath = dataIndex >= 0 && dataIndex + 1 < e.Args.Length ? e.Args[dataIndex + 1] : null;

        var store = new DataStore(dataPath ?? AppPaths.DataFile);
        string? warning = null;
        var data = demo ? DemoData.Create() : store.Load(out warning);
        var engine = new QueueEngine(data);
        var viewModel = new MainViewModel(engine, store, persist: !demo);

        // 注销或关机时先落盘，不阻止系统继续。
        SessionEnding += (_, _) => viewModel.SaveNow();

        if (demo)
        {
            viewModel.ShowReminder(DemoData.CreateReminder());
        }

        var window = new MainWindow(viewModel, enableTray: !demo);
        MainWindow = window;
        window.Show();

        if (warning is not null)
        {
            viewModel.ShowToast(warning);
        }

        if (capturePath is not null)
        {
            RunCapture(window, viewModel, capturePath);
        }
    }

    private void RunCapture(MainWindow window, MainViewModel viewModel, string path)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(900);
                window.SaveSnapshot(path);
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.ChangeExtension(path, ".error.txt"), ex.ToString());
                }
                catch
                {
                    // 截图失败时不再抛出新异常。
                }
            }
            finally
            {
                viewModel.SaveNow();
                window.Close();
                Shutdown();
            }
        });
    }
}
