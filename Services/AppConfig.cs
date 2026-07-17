using System.Text.Json;

namespace LlamaCppLauncher.Services;

/// <summary>Generic launcher settings for any llama.cpp build.</summary>
public sealed class AppConfig
{
    public string LlamaBin { get; set; } = "";
    public string ModelsDir { get; set; } = "";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public int ContextSize { get; set; } = 4096;
    public int GpuLayers { get; set; } = 99;
    public bool EnableTools { get; set; } = false;
    public bool EnableJinja { get; set; } = true;
    public bool EnableVision { get; set; } = true;
    public string ExtraArgs { get; set; } = "";
    public string? LastModelPath { get; set; }
    public string? LastMmprojPath { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string ConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LlamaCppLauncher",
            "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                if (cfg is not null) return cfg;
            }
        }
        catch { /* ignore */ }

        // Sensible first-run defaults if Bansai project exists nearby
        var bansai = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "AI", "Bansai Llama.cpp");
        // Desktop may be OneDrive
        var bansaiOd = @"C:\Users\geron\OneDrive\Desktop\AI\Bansai Llama.cpp";
        foreach (var root in new[] { bansaiOd, bansai })
        {
            var bin = Path.Combine(root, "llama.cpp", "build", "bin");
            var models = Path.Combine(root, "models");
            if (Directory.Exists(bin) && File.Exists(Path.Combine(bin, "llama-server.exe")))
            {
                return new AppConfig
                {
                    LlamaBin = bin,
                    ModelsDir = Directory.Exists(models) ? models : bin,
                };
            }
        }

        return new AppConfig();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
