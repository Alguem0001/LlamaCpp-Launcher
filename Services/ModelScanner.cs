namespace LlamaCppLauncher.Services;

public sealed class LocalModel
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public long SizeBytes { get; init; }
    public string Display => $"{Name}  ({FormatSize(SizeBytes)})";

    public static string FormatSize(long bytes)
    {
        double n = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        foreach (var u in units)
        {
            if (n < 1024) return $"{n:0.#} {u}";
            n /= 1024;
        }
        return $"{n:0.#} PB";
    }
}

public static class ModelScanner
{
    public static List<LocalModel> ScanGguf(string modelsDir, bool includeMmproj = false)
    {
        var list = new List<LocalModel>();
        if (!Directory.Exists(modelsDir)) return list;

        foreach (var file in Directory.EnumerateFiles(modelsDir, "*.gguf", SearchOption.AllDirectories))
        {
            var name = System.IO.Path.GetFileName(file);
            if (!includeMmproj && name.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
                continue;

            list.Add(new LocalModel
            {
                Path = file,
                Name = name,
                SizeBytes = new FileInfo(file).Length,
            });
        }

        return list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<LocalModel> ScanMmproj(string modelsDir)
    {
        if (!Directory.Exists(modelsDir)) return [];
        return Directory.EnumerateFiles(modelsDir, "*mmproj*.gguf", SearchOption.AllDirectories)
            .Select(f => new LocalModel
            {
                Path = f,
                Name = System.IO.Path.GetFileName(f),
                SizeBytes = new FileInfo(f).Length,
            })
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? GuessMmproj(string modelPath, string modelsDir)
    {
        var dir = System.IO.Path.GetDirectoryName(modelPath) ?? modelsDir;
        var stem = System.IO.Path.GetFileNameWithoutExtension(modelPath);

        // Prefer mmproj with similar prefix
        var candidates = new List<string>();
        if (Directory.Exists(dir))
            candidates.AddRange(Directory.EnumerateFiles(dir, "*mmproj*.gguf"));
        if (Directory.Exists(modelsDir) && !string.Equals(dir, modelsDir, StringComparison.OrdinalIgnoreCase))
            candidates.AddRange(Directory.EnumerateFiles(modelsDir, "*mmproj*.gguf"));

        // same folder + similar name first
        var best = candidates
            .OrderByDescending(c => System.IO.Path.GetFileName(c).Contains(stem.Split('-')[0], StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => System.IO.Path.GetFileName(c).Length)
            .FirstOrDefault(File.Exists);

        return best;
    }
}
