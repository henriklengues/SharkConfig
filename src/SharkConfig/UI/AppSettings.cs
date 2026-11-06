using System.Text.Json;

namespace SharkConfig.UI;

/// <summary>Preferências da interface, salvas em %LOCALAPPDATA%\SharkConfig\settings.json.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "dark";      // "dark" | "light"
    /// <summary>Desliga as animações do app (independe da opção do Windows, que costuma estar desligada).</summary>
    public bool ReduceMotion { get; set; }

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharkConfig", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Bridge.Json) ?? new());
        }
        catch { /* arquivo corrompido: usa o padrão */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Bridge.Json));
        }
        catch { /* preferências são opcionais */ }
    }

    public static AppSettings Normalize(AppSettings s)
    {
        s.Theme = s.Theme == "light" ? "light" : "dark";
        return s;
    }
}
