namespace LucidQueue.Services;

public static class AppPaths
{
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LucidQueue");

    public static string DataFile => Path.Combine(DataDirectory, "queue.json");
}
