using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace GlamourerBackup;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public int BackupIntervalMinutes { get; set; } = 5;
    public int MaxBackupsToKeep { get; set; } = 50;
    public int MaxCurrentOutfitBackups { get; set; } = 20;
    public bool BackupOnPluginStart { get; set; } = true;
    public bool IncludeEphemeralConfig { get; set; } = true;
    public bool IncludeOrganization { get; set; } = true;
    public bool IncludeCurrentOutfit { get; set; } = true;

    [NonSerialized] private IDalamudPluginInterface? _pi;

    public void Initialize(IDalamudPluginInterface pi) => _pi = pi;

    public void Save() => _pi?.SavePluginConfig(this);
}
