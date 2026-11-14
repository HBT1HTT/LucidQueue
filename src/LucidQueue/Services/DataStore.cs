using System.Text.Json;
using System.Text.Json.Serialization;
using LucidQueue.Models;

namespace LucidQueue.Services;

public sealed class DataStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public DataStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath) ? AppPaths.DataFile : filePath;
    }

    public string FilePath { get; }

    public AppData Load(out string? warning)
    {
        warning = null;
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(FilePath))
            {
                return new AppData();
            }

            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppData();
            }

            var data = JsonSerializer.Deserialize<AppData>(json, SerializerOptions);
            if (data is null)
            {
                warning = "数据文件内容为空，已使用新的队列。";
                return new AppData();
            }

            return data;
        }
        catch (Exception ex)
        {
            warning = $"读取队列数据失败，已从空队列启动。原文件已备份。（{ex.Message}）";
            TryBackupCorruptFile();
            return new AppData();
        }
    }

    public void Save(AppData data)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(data, SerializerOptions);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(FilePath))
        {
            try
            {
                File.Replace(tempPath, FilePath, null);
                return;
            }
            catch (IOException)
            {
                // 极少数文件系统不支持 Replace，退回到覆盖写入。
            }
        }

        File.Copy(tempPath, FilePath, true);
        File.Delete(tempPath);
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            var backup = Path.Combine(
                Path.GetDirectoryName(FilePath) ?? ".",
                $"queue.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(FilePath, backup, true);
        }
        catch
        {
            // 备份失败不应阻塞启动。
        }
    }
}
