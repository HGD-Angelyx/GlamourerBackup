using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Ipc;
using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace GlamourerBackup;

public sealed class GlamourerBackup : IDalamudPlugin
{
    private const string CommandName = "/gbackup";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IPluginLog _log;
    private readonly IFramework _framework;
    private readonly IObjectTable _objectTable;
    private readonly WindowSystem _windowSystem;

    private readonly string _glamourerDesignsDir;
    private readonly string _glamourerConfigDir;
    private readonly string _backupBaseDir;

    private readonly ICallGateSubscriber<string, uint, (int, string?)>? _getState;

    public string BackupBaseDir => _backupBaseDir;
    public Configuration Configuration { get; }
    public SettingsWindow SettingsWindow { get; }

    private DateTime _lastBackup = DateTime.MinValue;
    private bool _settingsVisible;

    public GlamourerBackup(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        IFramework framework,
        IObjectTable objectTable)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _log = log;
        _framework = framework;
        _objectTable = objectTable;

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(pluginInterface);

        var configDir = pluginInterface.ConfigDirectory.FullName;
        _backupBaseDir = Path.Combine(configDir, "Backups");

        var xlcoreDir = Path.GetDirectoryName(Path.GetDirectoryName(configDir));
        var pluginConfigsDir = Path.Combine(xlcoreDir!, "pluginConfigs", "Glamourer");
        _glamourerConfigDir = pluginConfigsDir;
        _glamourerDesignsDir = Path.Combine(pluginConfigsDir, "designs");

        if (!Directory.Exists(_backupBaseDir))
            Directory.CreateDirectory(_backupBaseDir);

        _getState = pluginInterface.GetIpcSubscriber<string, uint, (int, string?)>("Glamourer.GetStateBase64Name");

        _windowSystem = new WindowSystem("GlamourerBackup");
        SettingsWindow = new SettingsWindow(this);
        _windowSystem.AddWindow(SettingsWindow);

        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Glamourer Backup settings."
        });

        _pluginInterface.UiBuilder.Draw += OnDraw;
        _pluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;

        _framework.Update += OnFrameworkUpdate;

        if (Configuration.BackupOnPluginStart)
            _ = RunBackupAsync();

        _log.Information("Glamourer Backup loaded. Designs dir: {Dir}", _glamourerDesignsDir);
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _pluginInterface.UiBuilder.Draw -= OnDraw;
        _pluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;
        _commandManager.RemoveHandler(CommandName);
        _windowSystem.RemoveAllWindows();
    }

    private void OnCommand(string command, string args) => ToggleSettings();

    private void ToggleSettings()
    {
        _settingsVisible = !_settingsVisible;
        SettingsWindow.IsOpen = _settingsVisible;
    }

    private void OnDraw() => _windowSystem.Draw();

    private void OnFrameworkUpdate(IFramework framework)
    {
        var elapsed = DateTime.UtcNow - _lastBackup;
        if (elapsed.TotalMinutes >= Configuration.BackupIntervalMinutes)
        {
            _lastBackup = DateTime.UtcNow;
            _ = RunBackupAsync();
        }
    }

    public async Task RunBackupAsync()
    {
        try
        {
            if (!Directory.Exists(_glamourerDesignsDir))
            {
                _log.Warning("Glamourer designs directory not found: {Dir}", _glamourerDesignsDir);
            }
            else
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupDir = Path.Combine(_backupBaseDir, $"backup_{timestamp}");
                Directory.CreateDirectory(backupDir);

                var designFiles = Directory.GetFiles(_glamourerDesignsDir, "*.json");
                var copiedCount = 0;

                foreach (var file in designFiles)
                {
                    var dest = Path.Combine(backupDir, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: true);
                    copiedCount++;
                }

                if (Configuration.IncludeEphemeralConfig)
                {
                    var eph = Path.Combine(_glamourerConfigDir, "ephemeral_config.json");
                    if (File.Exists(eph))
                        File.Copy(eph, Path.Combine(backupDir, "ephemeral_config.json"), overwrite: true);
                }

                if (Configuration.IncludeOrganization)
                {
                    var org = Path.Combine(_glamourerConfigDir, "design_filesystem", "organization.json");
                    if (File.Exists(org))
                        File.Copy(org, Path.Combine(backupDir, "organization.json"), overwrite: true);
                }

                _log.Information("Backup complete: {Count} designs saved to {Dir}", copiedCount, backupDir);
            }

            if (Configuration.IncludeCurrentOutfit)
                await RunCurrentOutfitBackupAsync();

            PruneOldBackups();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Backup failed");
        }
    }

    private async Task RunCurrentOutfitBackupAsync()
    {
        var player = _objectTable.LocalPlayer;
        if (player == null)
        {
            _log.Warning("No local player found, skipping current-outfit backup.");
            return;
        }

        try
        {
            var playerName = player.Name.TextValue;
            var (ec, stateBase64) = _getState!.InvokeFunc(playerName, 0u);

            if (ec != 0 || stateBase64 == null)
            {
                _log.Warning("Failed to get current outfit state (error {Ec}). Is Glamourer installed?", ec);
                return;
            }

            var jsonBytes = Convert.FromBase64String(stateBase64);
            var json = Encoding.UTF8.GetString(jsonBytes);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"current_outfit_{timestamp}.json";
            var filePath = Path.Combine(_backupBaseDir, fileName);

            await File.WriteAllTextAsync(filePath, json);

            _log.Information("Current outfit backed up to {File}", filePath);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to back up current outfit (Glamourer might not be installed)");
        }
    }

    private void PruneOldBackups()
    {
        try
        {
            var dirs = Directory.GetDirectories(_backupBaseDir)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.CreationTimeUtc)
                .ToList();

            if (dirs.Count <= Configuration.MaxBackupsToKeep)
                return;

            foreach (var dir in dirs.Skip(Configuration.MaxBackupsToKeep))
            {
                dir.Delete(recursive: true);
                _log.Information("Pruned old backup: {Dir}", dir.FullName);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to prune old backups");
        }

        PruneCurrentOutfitBackups();
    }

    private void PruneCurrentOutfitBackups()
    {
        try
        {
            var pattern = "current_outfit_*.json";
            var files = Directory.GetFiles(_backupBaseDir, pattern)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();

            if (files.Count <= Configuration.MaxCurrentOutfitBackups)
                return;

            foreach (var file in files.Skip(Configuration.MaxCurrentOutfitBackups))
            {
                file.Delete();
                _log.Information("Pruned old current-outfit backup: {File}", file.FullName);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to prune current-outfit backups");
        }
    }
}

public class SettingsWindow : Window
{
    private readonly GlamourerBackup _plugin;
    private int _intervalMinutes;
    private int _maxBackups;
    private int _maxCurrentOutfitBackups;
    private bool _includeEph;
    private bool _includeOrg;
    private bool _includeCurrentOutfit;
    private string _statusMessage = string.Empty;
    private int _statusFrameCount;

    public SettingsWindow(GlamourerBackup plugin) : base("Glamourer Backup Settings")
    {
        _plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 300),
            MaximumSize = new Vector2(700, 500)
        };
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize;
        LoadSettings();
    }

    private void LoadSettings()
    {
        _intervalMinutes = _plugin.Configuration.BackupIntervalMinutes;
        _maxBackups = _plugin.Configuration.MaxBackupsToKeep;
        _maxCurrentOutfitBackups = _plugin.Configuration.MaxCurrentOutfitBackups;
        _includeEph = _plugin.Configuration.IncludeEphemeralConfig;
        _includeOrg = _plugin.Configuration.IncludeOrganization;
        _includeCurrentOutfit = _plugin.Configuration.IncludeCurrentOutfit;
    }

    public override void Draw()
    {
        var changed = false;

        changed |= ImGui.InputInt("Backup interval (minutes)", ref _intervalMinutes);
        if (_intervalMinutes < 1) _intervalMinutes = 1;

        ImGui.Separator();
        ImGui.Text("Design Backups");

        changed |= ImGui.InputInt("Max backup folders to keep", ref _maxBackups);
        if (_maxBackups < 1) _maxBackups = 1;

        changed |= ImGui.Checkbox("Backup ephemeral config", ref _includeEph);
        changed |= ImGui.Checkbox("Backup folder organization", ref _includeOrg);

        ImGui.Separator();
        ImGui.Text("Current Outfit Backup");

        changed |= ImGui.Checkbox("Backup current equipped outfit", ref _includeCurrentOutfit);

        changed |= ImGui.InputInt("Max current-outfit backups to keep", ref _maxCurrentOutfitBackups);
        if (_maxCurrentOutfitBackups < 1) _maxCurrentOutfitBackups = 1;

        ImGui.Separator();

        if (ImGui.Button("Back up now"))
        {
            _ = _plugin.RunBackupAsync();
            _statusMessage = "Backup triggered!";
            _statusFrameCount = 0;
        }

        ImGui.SameLine();
        if (ImGui.Button("Open backups folder"))
        {
            var backupDir = _plugin.BackupBaseDir;
            if (Directory.Exists(backupDir))
                Process.Start(new ProcessStartInfo(backupDir) { UseShellExecute = true });
        }

        if (_statusFrameCount < 120)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), _statusMessage);
            _statusFrameCount++;
        }

        if (changed)
        {
            _plugin.Configuration.BackupIntervalMinutes = _intervalMinutes;
            _plugin.Configuration.MaxBackupsToKeep = _maxBackups;
            _plugin.Configuration.MaxCurrentOutfitBackups = _maxCurrentOutfitBackups;
            _plugin.Configuration.IncludeEphemeralConfig = _includeEph;
            _plugin.Configuration.IncludeOrganization = _includeOrg;
            _plugin.Configuration.IncludeCurrentOutfit = _includeCurrentOutfit;
            _plugin.Configuration.Save();
        }
    }
}
