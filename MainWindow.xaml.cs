using System.Diagnostics;
using LlamaCppLauncher.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LlamaCppLauncher;

public sealed partial class MainWindow : Window
{
    private readonly AppConfig _cfg;
    private readonly LlamaServerService _server = new();
    private readonly DispatcherQueue _dq;
    private List<LocalModel> _models = [];
    private List<LocalModel> _mmprojs = [];
    private CancellationTokenSource? _healthCts;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 780));

        _dq = DispatcherQueue.GetForCurrentThread();
        _cfg = AppConfig.Load();
        LoadUiFromConfig();

        _server.LogReceived += line => AppendLog(line);
        _server.ProcessExited += () => _dq.TryEnqueue(() =>
        {
            SetRunningUi(false);
            SetStatus("idle", "Parado", "Servidor encerrado.");
        });

        RefreshLists();
        DetectServerLabel();

        Closed += (_, _) =>
        {
            _healthCts?.Cancel();
            _server.Dispose();
            SaveConfigFromUi();
            _cfg.Save();
        };
    }

    private void LoadUiFromConfig()
    {
        LlamaBinBox.Text = _cfg.LlamaBin;
        ModelsDirBox.Text = _cfg.ModelsDir;
        HostBox.Text = _cfg.Host;
        PortBox.Value = _cfg.Port;
        CtxBox.Value = _cfg.ContextSize;
        NglBox.Value = _cfg.GpuLayers;
        ToolsToggle.IsOn = _cfg.EnableTools;
        JinjaToggle.IsOn = _cfg.EnableJinja;
        VisionToggle.IsOn = _cfg.EnableVision;
        ExtraArgsBox.Text = _cfg.ExtraArgs;
    }

    private void SaveConfigFromUi()
    {
        _cfg.LlamaBin = LlamaBinBox.Text.Trim();
        _cfg.ModelsDir = ModelsDirBox.Text.Trim();
        _cfg.Host = string.IsNullOrWhiteSpace(HostBox.Text) ? "127.0.0.1" : HostBox.Text.Trim();
        _cfg.Port = (int)(double.IsNaN(PortBox.Value) ? 8080 : PortBox.Value);
        _cfg.ContextSize = (int)(double.IsNaN(CtxBox.Value) ? 4096 : CtxBox.Value);
        _cfg.GpuLayers = (int)(double.IsNaN(NglBox.Value) ? 99 : NglBox.Value);
        _cfg.EnableTools = ToolsToggle.IsOn;
        _cfg.EnableJinja = JinjaToggle.IsOn;
        _cfg.EnableVision = VisionToggle.IsOn;
        _cfg.ExtraArgs = ExtraArgsBox.Text?.Trim() ?? "";
        if (LocalModelCombo.SelectedItem is LocalModel m) _cfg.LastModelPath = m.Path;
        if (MmprojCombo.SelectedItem is LocalModel mp) _cfg.LastMmprojPath = mp.Path;
        else _cfg.LastMmprojPath = null;
    }

    private void RefreshLists()
    {
        SaveConfigFromUi();
        _models = ModelScanner.ScanGguf(_cfg.ModelsDir);
        _mmprojs = ModelScanner.ScanMmproj(_cfg.ModelsDir);

        LocalModelCombo.ItemsSource = _models;
        LocalModelCombo.DisplayMemberPath = nameof(LocalModel.Display);

        MmprojCombo.ItemsSource = _mmprojs;
        MmprojCombo.DisplayMemberPath = nameof(LocalModel.Display);

        if (_models.Count > 0)
        {
            LocalModel? sel = null;
            if (!string.IsNullOrEmpty(_cfg.LastModelPath))
                sel = _models.FirstOrDefault(m => string.Equals(m.Path, _cfg.LastModelPath, StringComparison.OrdinalIgnoreCase));
            LocalModelCombo.SelectedItem = sel ?? _models[0];
        }

        if (_mmprojs.Count > 0 && !string.IsNullOrEmpty(_cfg.LastMmprojPath))
        {
            var mps = _mmprojs.FirstOrDefault(m => string.Equals(m.Path, _cfg.LastMmprojPath, StringComparison.OrdinalIgnoreCase));
            if (mps is not null) MmprojCombo.SelectedItem = mps;
        }

        DetectServerLabel();
    }

    private void DetectServerLabel()
    {
        var exe = LlamaServerService.FindServerExe(LlamaBinBox.Text.Trim());
        ServerDetectLabel.Text = exe is null
            ? "llama-server.exe não encontrado nesta pasta"
            : $"encontrado · {exe}";
        ServerDetectLabel.Foreground = new SolidColorBrush(exe is null
            ? ColorHelper.FromArgb(0xFF, 0xB0, 0x7A, 0x5C)
            : ColorHelper.FromArgb(0xFF, 0x7A, 0x8F, 0x6E));
    }

    private void AppendLog(string line)
    {
        _dq.TryEnqueue(() =>
        {
            LogBox.Text += line + Environment.NewLine;
            if (LogBox.Text.Length > 200_000)
                LogBox.Text = LogBox.Text[^150_000..];
        });
    }

    private void SetStatus(string kind, string title, string message)
    {
        StatusTitle.Text = title;
        StatusMessage.Text = message;
        StatusDot.Background = new SolidColorBrush(kind switch
        {
            "ok" => ColorHelper.FromArgb(0xFF, 0x7A, 0x8F, 0x6E),
            "warn" => ColorHelper.FromArgb(0xFF, 0xC4, 0xA5, 0x74),
            "error" => ColorHelper.FromArgb(0xFF, 0xB0, 0x7A, 0x5C),
            "busy" => ColorHelper.FromArgb(0xFF, 0x8F, 0xA3, 0x7A),
            _ => ColorHelper.FromArgb(0xFF, 0x66, 0x6A, 0x60),
        });
    }

    private void SetRunningUi(bool running)
    {
        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        LocalModelCombo.IsEnabled = !running;
        MmprojCombo.IsEnabled = !running;
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> PickFileAsync(params string[] exts)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        foreach (var e in exts) picker.FileTypeFilter.Add(e);
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async void BrowseLlamaBin_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is null) return;
        LlamaBinBox.Text = path;
        DetectServerLabel();
    }

    private void DetectServer_Click(object sender, RoutedEventArgs e) => DetectServerLabel();

    private async void BrowseModelsDir_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is null) return;
        ModelsDirBox.Text = path;
        RefreshLists();
    }

    private async void BrowseGguf_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(".gguf");
        if (path is null) return;
        ModelsDirBox.Text = Path.GetDirectoryName(path) ?? ModelsDirBox.Text;
        RefreshLists();
        SelectModelByPath(path);
    }

    private async void ImportModel_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(".gguf");
        if (path is null) return;

        SaveConfigFromUi();
        try
        {
            Directory.CreateDirectory(_cfg.ModelsDir);
            var dest = Path.Combine(_cfg.ModelsDir, Path.GetFileName(path));

            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                AppendLog($"Já está em models: {Path.GetFileName(path)}");
                RefreshLists();
                SelectModelByPath(dest);
                SetStatus("ok", "Modelo", Path.GetFileName(path));
                return;
            }

            if (File.Exists(dest))
            {
                var overwrite = await ShowConfirm(
                    $"Já existe {Path.GetFileName(dest)} na pasta de models.\nSubstituir?");
                if (!overwrite) return;
            }

            DownloadProgress.Visibility = Visibility.Visible;
            DownloadStatus.Text = $"Importando {Path.GetFileName(path)}…";
            AppendLog($"Import → {_cfg.ModelsDir}: {Path.GetFileName(path)}");

            await Task.Run(() => File.Copy(path, dest, overwrite: true));

            DownloadStatus.Text = "Importado.";
            AppendLog("Import OK: " + dest);
            RefreshLists();
            SelectModelByPath(dest);
            SetStatus("ok", "Modelo adicionado", Path.GetFileName(dest));
        }
        catch (Exception ex)
        {
            DownloadStatus.Text = "Falha no import.";
            AppendLog("ERRO import: " + ex.Message);
            await ShowInfo(ex.Message);
        }
        finally
        {
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenModelsFolder_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        try
        {
            Directory.CreateDirectory(_cfg.ModelsDir);
            Process.Start(new ProcessStartInfo { FileName = _cfg.ModelsDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog("ERRO: " + ex.Message);
        }
    }

    private void SelectModelByPath(string path)
    {
        var match = _models.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
        if (match is not null) LocalModelCombo.SelectedItem = match;
    }

    private async void DownloadCustom_Click(object sender, RoutedEventArgs e)
    {
        var repo = HfRepoBox.Text?.Trim() ?? "";
        var file = HfFileBox.Text?.Trim() ?? "";
        var mmproj = HfMmprojBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(file))
        {
            await ShowInfo("Preencha repo (org/name) e file.gguf.");
            return;
        }

        SaveConfigFromUi();
        Directory.CreateDirectory(_cfg.ModelsDir);
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadStatus.Text = $"Baixando {file}…";
        AppendLog($"HF download: {repo} / {file}");

        try
        {
            await Task.Run(() =>
            {
                var modelsDir = _cfg.ModelsDir;
                var py = FindPython();
                if (py is null)
                    throw new InvalidOperationException("Python não encontrado (necessário para HuggingFace).");

                var script =
                    "from huggingface_hub import hf_hub_download; " +
                    $"p=hf_hub_download(repo_id='{repo}', filename='{file}', local_dir=r'{modelsDir}'); print(p)";
                RunProcess(py, $"-c \"{script}\"");

                if (!string.IsNullOrWhiteSpace(mmproj))
                {
                    var script2 =
                        "from huggingface_hub import hf_hub_download; " +
                        $"p=hf_hub_download(repo_id='{repo}', filename='{mmproj}', local_dir=r'{modelsDir}'); print(p)";
                    RunProcess(py, $"-c \"{script2}\"");
                }
            });

            DownloadStatus.Text = "Download concluído.";
            AppendLog("Download OK.");
            RefreshLists();
            SelectModelByPath(Path.Combine(_cfg.ModelsDir, file));
            SetStatus("ok", "Modelo adicionado", file);
        }
        catch (Exception ex)
        {
            DownloadStatus.Text = "Erro no download.";
            AppendLog("ERRO: " + ex.Message);
            await ShowInfo("Falha no download:\n" + ex.Message);
        }
        finally
        {
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private static string? FindPython()
    {
        foreach (var name in new[] { "python", "py" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = name,
                    Arguments = name == "py"
                        ? "-3 -c \"import sys; print(sys.executable)\""
                        : "-c \"import sys; print(sys.executable)\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                if (p.ExitCode == 0 && File.Exists(output)) return output;
            }
            catch { /* next */ }
        }
        return null;
    }

    private void RunProcess(string fileName, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar processo.");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (!string.IsNullOrWhiteSpace(stdout)) AppendLog(stdout.Trim());
        if (p.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? $"exit {p.ExitCode}" : stderr.Trim());
    }

    private async void BrowseMmproj_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync(".gguf");
        if (path is null) return;
        RefreshLists();
        var match = _mmprojs.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            match = new LocalModel
            {
                Path = path,
                Name = Path.GetFileName(path),
                SizeBytes = new FileInfo(path).Length,
            };
            _mmprojs.Insert(0, match);
            MmprojCombo.ItemsSource = null;
            MmprojCombo.ItemsSource = _mmprojs;
            MmprojCombo.DisplayMemberPath = nameof(LocalModel.Display);
        }
        MmprojCombo.SelectedItem = match;
    }

    private void RefreshModels_Click(object sender, RoutedEventArgs e) => RefreshLists();

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        _cfg.Save();
        AppendLog("Config salva em %LocalAppData%\\LlamaCppLauncher\\config.json");
        SetStatus("ok", "Config", "Preferências salvas.");
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        _cfg.Save();

        if (LocalModelCombo.SelectedItem is not LocalModel model)
        {
            await ShowInfo("Selecione um modelo GGUF.");
            return;
        }

        string? mmproj = null;
        if (VisionToggle.IsOn && MmprojCombo.SelectedItem is LocalModel mp)
            mmproj = mp.Path;
        else if (VisionToggle.IsOn)
            mmproj = ModelScanner.GuessMmproj(model.Path, _cfg.ModelsDir);

        try
        {
            _server.Start(_cfg, model.Path, mmproj);
            SetRunningUi(true);
            SetStatus("busy", "Iniciando…",
                $"Modelo: {model.Name}  PID {_server.ProcessId}");

            _healthCts?.Cancel();
            _healthCts = new CancellationTokenSource();
            var ct = _healthCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var ok = await _server.WaitUntilHealthyAsync(_cfg, ct);
                    _dq.TryEnqueue(() =>
                    {
                        if (ok)
                        {
                            SetStatus("ok", "Rodando", _server.WebUiUrl(_cfg));
                            OpenBrowser(_server.WebUiUrl(_cfg));
                        }
                        else if (_server.IsRunning)
                        {
                            SetStatus("warn", "Aguardando",
                                "Ainda carregando — use Open UI quando pronto.");
                        }
                    });
                }
                catch (OperationCanceledException) { }
            }, ct);
        }
        catch (Exception ex)
        {
            AppendLog("ERRO: " + ex.Message);
            SetStatus("error", "Erro", ex.Message);
            SetRunningUi(false);
            await ShowInfo(ex.Message);
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _healthCts?.Cancel();
        _server.Stop();
        SetRunningUi(false);
        SetStatus("idle", "Parado", "Servidor parado.");
    }

    private void OpenWebUi_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        OpenBrowser(_server.WebUiUrl(_cfg));
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    private async Task ShowInfo(string message)
    {
        var dlg = new ContentDialog
        {
            Title = "llama.cpp Launcher",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
        };
        await dlg.ShowAsync();
    }

    private async Task<bool> ShowConfirm(string message)
    {
        var dlg = new ContentDialog
        {
            Title = "llama.cpp Launcher",
            Content = message,
            PrimaryButtonText = "Sim",
            CloseButtonText = "Não",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
