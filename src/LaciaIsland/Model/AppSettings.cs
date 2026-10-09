using System.Text.Json;
using System.Text.Json.Serialization;
using LaciaIsland.Core;
using LaciaIsland.Services;

namespace LaciaIsland.Model;

public sealed class AppSettings
{
    public IslandMode Mode { get; set; } = IslandMode.Island;
    public double Scale { get; set; } = 1.0;
    /// <summary>Horizontal length of the island (1 = default).</summary>
    public double Length { get; set; } = 1.0;
    public double TopGap { get; set; } = 8;
    public string? Monitor { get; set; }
    public bool HideWhenIdle { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    public bool ExpandOnHover { get; set; }
    public bool RealVisualizer { get; set; } = true;
    public bool ShowMusic { get; set; } = true;
    public bool ShowCalls { get; set; } = true;
    public bool ShowVolume { get; set; } = true;
    public bool ShowLayout { get; set; } = true;
    public bool ShowCharging { get; set; } = true;
    public bool ShowDevices { get; set; } = true;
    public bool TimerSound { get; set; } = true;
    public int[] TimerPresets { get; set; } = { 5, 15, 25 };
    /// <summary>Apps whose microphone use should not be shown as a call (always-on mic tools).</summary>
    public string[] IgnoredCallApps { get; set; } = { "NVIDIA Broadcast", "Voicemeeter", "Krisp" };

    public const double MinLength = 0.7;
    public const double MaxLength = 2.2;

    [JsonIgnore] public static string FilePath => Path.Combine(Log.Dir, "settings.json");


    public event Action? Changed;

    public void NotifyChanged()
    {
        Save();
        Changed?.Invoke();
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings);
                if (s is not null)
                {
                    s.Scale = Math.Clamp(s.Scale, 0.7, 1.6);
                    s.Length = s.Length <= 0 ? 1 : Math.Clamp(s.Length, MinLength, MaxLength);
                    s.TopGap = Math.Clamp(s.TopGap, 0, 40);
                    if (s.TimerPresets is not { Length: 3 }) s.TimerPresets = new[] { 5, 15, 25 };
                    s.IgnoredCallApps ??= Array.Empty<string>();
                    return s;
                }
            }
        }
        catch (Exception ex) { Log.Error("settings load", ex); }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
        }
        catch (Exception ex) { Log.Error("settings save", ex); }
    }
}

/// <summary>Source-generated JSON (trimming-safe).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJson : JsonSerializerContext
{
}
