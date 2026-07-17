using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace LlamaCppLauncher.Services;

public sealed class LlamaServerService : IDisposable
{
    private Process? _process;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public bool IsRunning => _process is { HasExited: false };
    public int? ProcessId => IsRunning ? _process!.Id : null;

    public event Action<string>? LogReceived;
    public event Action? ProcessExited;

    public static string? FindServerExe(string llamaBin)
    {
        foreach (var name in new[] { "llama-server.exe", "llama-server", "server.exe" })
        {
            var p = Path.Combine(llamaBin, name);
            if (File.Exists(p)) return p;
        }
        // also search one level down (some builds put Release\bin)
        if (Directory.Exists(llamaBin))
        {
            foreach (var p in Directory.EnumerateFiles(llamaBin, "llama-server.exe", SearchOption.AllDirectories).Take(5))
                return p;
        }
        return null;
    }

    public string BuildArgs(AppConfig cfg, string modelPath, string? mmprojPath)
    {
        var sb = new StringBuilder();
        sb.Append("-m \"").Append(modelPath).Append('"');
        sb.Append(" --host ").Append(cfg.Host);
        sb.Append(" --port ").Append(cfg.Port);
        sb.Append(" -c ").Append(cfg.ContextSize);
        sb.Append(" -ngl ").Append(cfg.GpuLayers);
        // Arc / Vulkan defaults — skip if user already set them in ExtraArgs
        var extra = cfg.ExtraArgs ?? "";
        if (!ContainsFlag(extra, "-fa") && !ContainsFlag(extra, "--flash-attn"))
            sb.Append(" -fa on");
        if (!ContainsFlag(extra, "-b") && !ContainsFlag(extra, "--batch-size"))
            sb.Append(" -b 512");
        if (!ContainsFlag(extra, "-ub") && !ContainsFlag(extra, "--ubatch-size"))
            sb.Append(" -ub 256");
        if (cfg.EnableJinja) sb.Append(" --jinja");
        if (cfg.EnableTools) sb.Append(" --tools all");
        if (cfg.EnableVision && !string.IsNullOrEmpty(mmprojPath) && File.Exists(mmprojPath))
            sb.Append(" --mmproj \"").Append(mmprojPath).Append('"');
        // No speculative/draft model — product path is base model only.
        if (!string.IsNullOrWhiteSpace(extra))
            sb.Append(' ').Append(extra.Trim());
        return sb.ToString();
    }

    private static bool ContainsFlag(string args, string flag)
    {
        if (string.IsNullOrWhiteSpace(args)) return false;
        return args.Contains(flag + " ", StringComparison.OrdinalIgnoreCase)
            || args.Contains(flag + "=", StringComparison.OrdinalIgnoreCase)
            || args.EndsWith(flag, StringComparison.OrdinalIgnoreCase)
            || args.Contains(" " + flag + " ", StringComparison.OrdinalIgnoreCase);
    }

    public void Start(AppConfig cfg, string modelPath, string? mmprojPath)
    {
        if (IsRunning)
            throw new InvalidOperationException("O servidor já está em execução.");

        var serverExe = FindServerExe(cfg.LlamaBin)
            ?? throw new FileNotFoundException($"llama-server.exe não encontrado em:\n{cfg.LlamaBin}");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Modelo não encontrado:\n{modelPath}");

        var workDir = Path.GetDirectoryName(serverExe)!;

        var args = BuildArgs(cfg, modelPath, mmprojPath);

        LogReceived?.Invoke($"Exe: {serverExe}");
        try
        {
            var verPsi = new ProcessStartInfo
            {
                FileName = serverExe,
                Arguments = "--version",
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            verPsi.Environment["PATH"] = workDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            using var verProc = Process.Start(verPsi);
            if (verProc is not null)
            {
                var so = verProc.StandardOutput.ReadToEnd();
                var se = verProc.StandardError.ReadToEnd();
                verProc.WaitForExit(5000);
                var line = (so + se).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(line))
                    LogReceived?.Invoke($"llama.cpp: {line.Trim()}");
            }
        }
        catch { /* ignore version probe failures */ }

        LogReceived?.Invoke($"Args: {args}");
        LogReceived?.Invoke($"WebUI embutida → http://{cfg.Host}:{cfg.Port}/");

        var psi = new ProcessStartInfo
        {
            FileName = serverExe,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PATH"] = workDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) LogReceived?.Invoke(e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) LogReceived?.Invoke(e.Data);
        };
        _process.Exited += (_, _) =>
        {
            LogReceived?.Invoke($"Processo encerrado (exit {_process?.ExitCode}).");
            ProcessExited?.Invoke();
        };

        if (!_process.Start())
            throw new InvalidOperationException("Falha ao iniciar llama-server.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        LogReceived?.Invoke($"PID {_process.Id}");
    }

    public void Stop()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                LogReceived?.Invoke("Parando servidor…");
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(8000);
            }
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke($"Erro ao parar: {ex.Message}");
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public async Task<bool> WaitUntilHealthyAsync(AppConfig cfg, CancellationToken ct)
    {
        var url = $"http://{cfg.Host}:{cfg.Port}/health";
        for (var i = 0; i < 180; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsRunning) return false;
            try
            {
                using var resp = await _http.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { /* loading */ }
            await Task.Delay(1000, ct);
        }
        return false;
    }

    public string WebUiUrl(AppConfig cfg) => $"http://{cfg.Host}:{cfg.Port}/";

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
