namespace RisDataEditor;

public static class AppPaths
{
    public static string LocateDataDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "app.js")))
            {
                var dataDir = Path.Combine(dir.FullName, "data");
                Directory.CreateDirectory(dataDir);
                return dataDir;
            }

            dir = dir.Parent;
        }

        var fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data"));
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
