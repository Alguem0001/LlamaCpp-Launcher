# llama.cpp Launcher (genérico)

App **WinUI 3** para **qualquer** build do llama.cpp e **qualquer** modelo GGUF.

## Diferença do BonsaiWinUI

| | BonsaiWinUI | LlamaCpp Launcher |
|--|-------------|-------------------|
| Foco | PrismML / Bonsai | Genérico |
| Catálogo HF | Modelos Bonsai | Não (só arquivos locais) |
| Caminhos | Pré-preenchidos Bansai | Você escolhe qualquer pasta |
| mmproj | Auto se existir | Combo + auto-guess |

## Build

```powershell
cd "C:\Users\geron\OneDrive\Desktop\AI\LlamaCpp Launcher"
dotnet build -c Release -p:Platform=x64
```

EXE:

```
bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\LlamaCppLauncher.exe
```

## Uso

1. **Pasta do llama-server** → `...\build\bin` (ou onde estiver o exe)
2. **Pasta de modelos** → pasta com `.gguf`
3. Escolha modelo (+ mmproj opcional)
4. Ajuste porta / ctx / ngl / tools
5. **Iniciar llama-server + WebUI**

Config: `%LocalAppData%\LlamaCppLauncher\config.json`
