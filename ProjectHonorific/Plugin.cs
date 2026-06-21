using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ProjectHonorific.Windows;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace ProjectHonorific;

public sealed class Plugin : IDalamudPlugin
{
    private const string MainCommandName = "/projecthonorific";
    private const string ShortCommandName = "/phonorific";
    public const int MaxTitleLength = 32;
    private const int MaxHandwrittenTitles = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("ProjectHonorific");
    private readonly ConfigWindow configWindow;
    private readonly ICallGateSubscriber<int, string, object> setCharacterTitle;
    private readonly ICallGateSubscriber<int, object> clearCharacterTitle;
    private DateTime nextPollAtUtc = DateTime.MinValue;
    private string? lastSentJson;
    private ulong? lastSentEntityId;
    private string? lastLoggedHonorificFailure;
    private bool disposed;

    private sealed record DisplayTitle(string Title, HonorificTitleStyle? Style);

    private sealed record DisplayTitleOption(string Title, double DurationSeconds, HonorificTitleStyle? Style);

    public Configuration Configuration { get; }

    public ProjectStatus? CurrentStatus { get; private set; }

    public string LastTitle { get; private set; } = string.Empty;

    public string LastError { get; private set; } = string.Empty;

    public string LastSummary { get; private set; } = "No status loaded.";

    public bool IsStale { get; private set; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        NormalizeConfiguration();

        setCharacterTitle = PluginInterface.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        clearCharacterTitle = PluginInterface.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");

        configWindow = new ConfigWindow(this)
        {
            IsOpen = false,
        };
        windowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(MainCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Project Honorific.",
        });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Project Honorific.",
        });

        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
    }

    public void Dispose()
    {
        disposed = true;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        CommandManager.RemoveHandler(ShortCommandName);
        CommandManager.RemoveHandler(MainCommandName);
        windowSystem.RemoveAllWindows();
    }

    public void ToggleConfigUi()
    {
        configWindow.Toggle();
    }

    public void SetEnabled(bool enabled)
    {
        Configuration.Enabled = enabled;
        Configuration.Save();
        if (!enabled)
        {
            ClearTitle(force: true);
        }
        else
        {
            RefreshStatus(force: true);
        }
    }

    public void SetStatusFilePath(string path)
    {
        Configuration.StatusFilePath = path.Trim();
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetPollIntervalSeconds(float seconds)
    {
        Configuration.PollIntervalSeconds = Math.Clamp(seconds, 1.0f, 60.0f);
        Configuration.Save();
    }

    public void SetStaleAfterSeconds(float seconds)
    {
        Configuration.StaleAfterSeconds = Math.Clamp(seconds, 10.0f, 86_400.0f);
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetShowIdle(bool showIdle)
    {
        Configuration.ShowIdle = showIdle;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetIdleTitle(string title)
    {
        Configuration.IdleTitle = title;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetRotateHandwrittenTitles(bool rotate)
    {
        Configuration.RotateHandwrittenTitles = rotate;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetHandwrittenTitleRotationSeconds(float seconds)
    {
        Configuration.HandwrittenTitleRotationSeconds = Math.Clamp(seconds, 2.0f, 120.0f);
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void AddHandwrittenTitle()
    {
        Configuration.HandwrittenTitleEntries ??= [];
        if (Configuration.HandwrittenTitleEntries.Count >= MaxHandwrittenTitles)
        {
            return;
        }

        Configuration.HandwrittenTitleEntries.Add(new HandwrittenTitleEntry
        {
            Title = "New handwritten title",
            DurationSeconds = Configuration.HandwrittenTitleRotationSeconds,
            Style = CreateStyleFromCurrentConfiguration(),
        });
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetHandwrittenTitle(int index, string title)
    {
        Configuration.HandwrittenTitleEntries ??= [];
        if (index < 0 || index >= Configuration.HandwrittenTitleEntries.Count)
        {
            return;
        }

        Configuration.HandwrittenTitleEntries[index].Title = CleanEditableTitle(title);
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetHandwrittenTitleDuration(int index, float seconds)
    {
        Configuration.HandwrittenTitleEntries ??= [];
        if (index < 0 || index >= Configuration.HandwrittenTitleEntries.Count)
        {
            return;
        }

        Configuration.HandwrittenTitleEntries[index].DurationSeconds = Math.Clamp(seconds, 2.0f, 120.0f);
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetHandwrittenTitleUseColor(int index, bool useColor)
    {
        if (TryGetHandwrittenTitleStyle(index, out var style))
        {
            style.UseColor = useColor;
            Configuration.Save();
            RefreshStatus(force: true);
        }
    }

    public void SetHandwrittenTitleColor(int index, Vector3 color)
    {
        if (TryGetHandwrittenTitleStyle(index, out var style))
        {
            style.Color = color;
            Configuration.Save();
            RefreshStatus(force: true);
        }
    }

    public void SetHandwrittenTitleUseGlow(int index, bool useGlow)
    {
        if (TryGetHandwrittenTitleStyle(index, out var style))
        {
            style.UseGlow = useGlow;
            Configuration.Save();
            RefreshStatus(force: true);
        }
    }

    public void SetHandwrittenTitleGlowMode(int index, HonorificGlowMode mode)
    {
        if (TryGetHandwrittenTitleStyle(index, out var style))
        {
            style.GlowMode = mode;
            Configuration.Save();
            RefreshStatus(force: true);
        }
    }

    public void SetHandwrittenTitleGlow(int index, Vector3 glow)
    {
        if (TryGetHandwrittenTitleStyle(index, out var style))
        {
            style.Glow = glow;
            Configuration.Save();
            RefreshStatus(force: true);
        }
    }

    public void SetHandwrittenTitleGradientSecondColor(int index, Vector3 color)
    {
        if (TryGetHandwrittenTitleStyle(index, out var style))
        {
            style.GradientSecondColor = color;
            Configuration.Save();
            RefreshStatus(force: true);
        }
    }

    public void SetHandwrittenTitleGradientColourSet(int index, int preset)
    {
        if (TryGetHandwrittenTitleStyle(index, out var style))
        {
            style.GradientColourSet = Math.Clamp(preset, 0, Configuration.MaxHonorificGradientPreset);
            Configuration.Save();
            RefreshStatus(force: true);
        }
    }

    public void SetHandwrittenTitleGradientAnimationStyle(int index, HonorificGradientAnimationStyle animationStyle)
    {
        if (TryGetHandwrittenTitleStyle(index, out var style))
        {
            style.GradientAnimationStyle = animationStyle;
            Configuration.Save();
            RefreshStatus(force: true);
        }
    }

    public void RemoveHandwrittenTitle(int index)
    {
        Configuration.HandwrittenTitleEntries ??= [];
        if (index < 0 || index >= Configuration.HandwrittenTitleEntries.Count)
        {
            return;
        }

        Configuration.HandwrittenTitleEntries.RemoveAt(index);
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetIsPrefix(bool isPrefix)
    {
        Configuration.IsPrefix = isPrefix;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetUseColor(bool useColor)
    {
        Configuration.UseColor = useColor;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetColor(Vector3 color)
    {
        Configuration.Color = color;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetUseGlow(bool useGlow)
    {
        Configuration.UseGlow = useGlow;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetGlowMode(HonorificGlowMode mode)
    {
        Configuration.GlowMode = mode;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetGlow(Vector3 glow)
    {
        Configuration.Glow = glow;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetGradientSecondColor(Vector3 color)
    {
        Configuration.GradientSecondColor = color;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetGradientColourSet(int preset)
    {
        Configuration.GradientColourSet = Math.Clamp(preset, 0, Configuration.MaxHonorificGradientPreset);
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void SetGradientAnimationStyle(HonorificGradientAnimationStyle style)
    {
        Configuration.GradientAnimationStyle = style;
        Configuration.Save();
        RefreshStatus(force: true);
    }

    public void Reload()
    {
        RefreshStatus(force: true);
    }

    public void ClearTitleFromUi()
    {
        ClearTitle(force: true);
    }

    public void WriteSampleStatus()
    {
        var status = new ProjectStatus
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            Project = "Better Deaths",
            Activity = "Developing",
            Details = "Testing Project Honorific",
            Category = "development",
            ToolProvider = "codex",
            Detectable = true,
        };
        WriteStatusFile(status);
        RefreshStatus(force: true);
    }

    public void WriteStatusFile(ProjectStatus status)
    {
        var path = ExpandPath(Configuration.StatusFilePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(status, JsonOptions));
    }

    private void NormalizeConfiguration()
    {
        if (string.IsNullOrWhiteSpace(Configuration.StatusFilePath))
        {
            Configuration.StatusFilePath = Configuration.DefaultStatusFilePath;
        }

        Configuration.PollIntervalSeconds = Math.Clamp(Configuration.PollIntervalSeconds, 1.0f, 60.0f);
        Configuration.StaleAfterSeconds = Math.Clamp(Configuration.StaleAfterSeconds, 10.0f, 86_400.0f);
        Configuration.GradientColourSet = Math.Clamp(Configuration.GradientColourSet, 0, Configuration.MaxHonorificGradientPreset);
        Configuration.HandwrittenTitleRotationSeconds = Math.Clamp(Configuration.HandwrittenTitleRotationSeconds, 2.0f, 120.0f);
        Configuration.HandwrittenTitleEntries ??= [];
        Configuration.HandwrittenTitles ??= [];
        if (Configuration.HandwrittenTitleEntries.Count == 0 && Configuration.HandwrittenTitles.Count > 0)
        {
            Configuration.HandwrittenTitleEntries = Configuration.HandwrittenTitles
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Take(MaxHandwrittenTitles)
                .Select(title => new HandwrittenTitleEntry
                {
                    Title = CleanEditableTitle(title),
                    DurationSeconds = Configuration.HandwrittenTitleRotationSeconds,
                    Style = CreateStyleFromCurrentConfiguration(),
                })
                .ToList();
            Configuration.HandwrittenTitles = [];
        }

        if (Configuration.HandwrittenTitleEntries.Count > MaxHandwrittenTitles)
        {
            Configuration.HandwrittenTitleEntries = Configuration.HandwrittenTitleEntries.Take(MaxHandwrittenTitles).ToList();
        }

        foreach (var entry in Configuration.HandwrittenTitleEntries)
        {
            entry.Title = CleanEditableTitle(entry.Title);
            entry.DurationSeconds = Math.Clamp(entry.DurationSeconds, 2.0f, 120.0f);
            entry.Style ??= CreateStyleFromCurrentConfiguration();
            NormalizeStyle(entry.Style);
        }

        Configuration.Save();
    }

    private bool TryGetHandwrittenTitleStyle(int index, out HonorificTitleStyle style)
    {
        style = null!;
        Configuration.HandwrittenTitleEntries ??= [];
        if (index < 0 || index >= Configuration.HandwrittenTitleEntries.Count)
        {
            return false;
        }

        var entry = Configuration.HandwrittenTitleEntries[index];
        entry.Style ??= CreateStyleFromCurrentConfiguration();
        NormalizeStyle(entry.Style);
        style = entry.Style;
        return true;
    }

    private HonorificTitleStyle CreateStyleFromCurrentConfiguration()
    {
        return new HonorificTitleStyle
        {
            UseColor = Configuration.UseColor,
            Color = Configuration.Color,
            UseGlow = Configuration.UseGlow,
            GlowMode = Configuration.GlowMode,
            Glow = Configuration.Glow,
            GradientSecondColor = Configuration.GradientSecondColor,
            GradientColourSet = Math.Clamp(Configuration.GradientColourSet, 0, Configuration.MaxHonorificGradientPreset),
            GradientAnimationStyle = Configuration.GradientAnimationStyle,
        };
    }

    private static void NormalizeStyle(HonorificTitleStyle style)
    {
        style.GradientColourSet = Math.Clamp(style.GradientColourSet, 0, Configuration.MaxHonorificGradientPreset);
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Equals("reload", StringComparison.OrdinalIgnoreCase))
        {
            RefreshStatus(force: true);
            return;
        }

        if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            ClearTitle(force: true);
            return;
        }

        if (trimmed.Equals("sample", StringComparison.OrdinalIgnoreCase))
        {
            WriteSampleStatus();
            return;
        }

        ToggleConfigUi();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now < nextPollAtUtc)
        {
            return;
        }

        nextPollAtUtc = now + TimeSpan.FromSeconds(Configuration.PollIntervalSeconds);
        RefreshStatus(force: false);
    }

    private void RefreshStatus(bool force)
    {
        LastError = string.Empty;
        if (!Configuration.Enabled)
        {
            LastSummary = "Disabled.";
            ClearTitle(force: false);
            return;
        }

        var path = ExpandPath(Configuration.StatusFilePath);
        if (!File.Exists(path))
        {
            CurrentStatus = null;
            IsStale = true;
            LastSummary = $"Waiting for status file: {path}";
            SetInactiveTitleOrKeep(force: false);
            return;
        }

        ProjectStatus? status;
        try
        {
            var json = File.ReadAllText(path);
            status = JsonSerializer.Deserialize<ProjectStatus>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            LastError = $"Could not read status file: {ex.Message}";
            Log.Warning(ex, "Could not read Project Honorific status file.");
            return;
        }

        if (status is null)
        {
            CurrentStatus = null;
            IsStale = true;
            LastSummary = "Status file was empty.";
            return;
        }

        if (status.UpdatedAt == default)
        {
            status.UpdatedAt = File.GetLastWriteTimeUtc(path);
        }

        status.Activity = NormalizeActivity(status.Activity);
        CurrentStatus = status;
        var age = DateTimeOffset.UtcNow - status.UpdatedAt.ToUniversalTime();
        IsStale = age.TotalSeconds > Configuration.StaleAfterSeconds;
        if (IsStale)
        {
            LastSummary = $"Stale: {BuildStatusSummary(status)}";
            SetInactiveTitleOrKeep(force, BuildStatusTitle(status));
            return;
        }

        if (!status.Detectable)
        {
            LastSummary = $"Not detectable: {BuildStatusSummary(status)}";
            SetInactiveTitleOrKeep(force: false, BuildStatusTitle(status));
            return;
        }

        if (!TrySetDisplayTitle(BuildStatusTitle(status), force))
        {
            LastSummary = "Status did not include a usable title.";
            return;
        }

        LastSummary = BuildStatusSummary(status);
    }

    private void SetInactiveTitleOrKeep(bool force, string? fallbackBaseTitle = null)
    {
        if (Configuration.ShowIdle && TrySetDisplayTitle(Configuration.IdleTitle, force))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(fallbackBaseTitle) && TrySetDisplayTitle(fallbackBaseTitle, force))
        {
            return;
        }

        TrySetDisplayTitle(string.Empty, force);
    }

    private bool TrySetDisplayTitle(string baseTitle, bool force)
    {
        var displayTitle = SelectDisplayTitle(baseTitle);
        var title = SanitizeTitle(displayTitle.Title);
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        SetTitle(title, displayTitle.Style, force);
        return true;
    }

    private void SetTitle(string title, HonorificTitleStyle? style, bool force)
    {
        var titleJson = BuildHonorificTitleJson(title, style);
        try
        {
            _ = Framework.RunOnFrameworkThread(() =>
            {
                try
                {
                    if (disposed)
                    {
                        return;
                    }

                    if (!TryGetLocalPlayerIpcTarget(out var objectIndex, out var entityId))
                    {
                        LastError = "Waiting for local player before sending Honorific title.";
                        return;
                    }

                    if (!force && entityId == lastSentEntityId && string.Equals(titleJson, lastSentJson, StringComparison.Ordinal))
                    {
                        return;
                    }

                    setCharacterTitle.InvokeAction(objectIndex, titleJson);
                    lastSentJson = titleJson;
                    lastSentEntityId = entityId;
                    lastLoggedHonorificFailure = null;
                    LastTitle = title;
                    LastError = string.Empty;
                }
                catch (Exception ex)
                {
                    HandleHonorificFailure(ex, "Could not send title to Honorific.");
                }
            });
        }
        catch (Exception ex)
        {
            HandleHonorificFailure(ex, "Could not queue Honorific title update.");
        }
    }

    private void ClearTitle(bool force)
    {
        try
        {
            _ = Framework.RunOnFrameworkThread(() =>
            {
                try
                {
                    if (disposed)
                    {
                        return;
                    }

                    if (!TryGetLocalPlayerIpcTarget(out var objectIndex, out _))
                    {
                        LastTitle = string.Empty;
                        lastSentJson = null;
                        lastSentEntityId = null;
                        return;
                    }

                    if (!force && lastSentJson is null)
                    {
                        LastTitle = string.Empty;
                        return;
                    }

                    clearCharacterTitle.InvokeAction(objectIndex);
                    lastLoggedHonorificFailure = null;
                    LastError = string.Empty;
                }
                catch (Exception ex)
                {
                    HandleHonorificFailure(ex, "Could not clear Honorific title.");
                }
                finally
                {
                    lastSentJson = null;
                    lastSentEntityId = null;
                    LastTitle = string.Empty;
                }
            });
        }
        catch (Exception ex)
        {
            HandleHonorificFailure(ex, "Could not queue Honorific title clear.");
        }
    }

    private bool TryGetLocalPlayerIpcTarget(out int objectIndex, out ulong entityId)
    {
        objectIndex = -1;
        entityId = 0;
        try
        {
            var localPlayer = Objects.LocalPlayer;
            if (localPlayer is null)
            {
                return false;
            }

            objectIndex = (int)localPlayer.ObjectIndex;
            entityId = localPlayer.EntityId;
            return objectIndex >= 0;
        }
        catch
        {
            return false;
        }
    }

    private void HandleHonorificFailure(Exception ex, string logMessage)
    {
        LastError = IsHonorificIpcUnavailable(ex)
            ? "Honorific IPC is not available. Make sure Honorific is loaded."
            : $"Honorific IPC failed: {ex.Message}";

        var failureKey = $"{logMessage}|{ex.GetType().FullName}|{ex.Message}";
        if (string.Equals(lastLoggedHonorificFailure, failureKey, StringComparison.Ordinal))
        {
            return;
        }

        lastLoggedHonorificFailure = failureKey;
        Log.Warning(ex, logMessage);
    }

    private static bool IsHonorificIpcUnavailable(Exception ex)
    {
        return ex.GetType().Name.Contains("IpcNotReady", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("was not registered", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("IPC", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildHonorificTitleJson(string title, HonorificTitleStyle? style)
    {
        var payload = new Dictionary<string, object>
        {
            ["Title"] = title,
            ["IsPrefix"] = Configuration.IsPrefix,
        };

        var useColor = style?.UseColor ?? Configuration.UseColor;
        var color = style?.Color ?? Configuration.Color;
        var useGlow = style?.UseGlow ?? Configuration.UseGlow;
        var glowMode = style?.GlowMode ?? Configuration.GlowMode;
        var glow = style?.Glow ?? Configuration.Glow;
        var gradientSecondColor = style?.GradientSecondColor ?? Configuration.GradientSecondColor;
        var gradientColourSet = Math.Clamp(style?.GradientColourSet ?? Configuration.GradientColourSet, 0, Configuration.MaxHonorificGradientPreset);
        var gradientAnimationStyle = style?.GradientAnimationStyle ?? Configuration.GradientAnimationStyle;

        if (useColor)
        {
            payload["Color"] = ToVectorPayload(color);
        }

        if (useGlow)
        {
            switch (glowMode)
            {
                case HonorificGlowMode.PresetGradient:
                    payload["GradientColourSet"] = gradientColourSet;
                    payload["GradientAnimationStyle"] = gradientAnimationStyle.ToString();
                    break;
                case HonorificGlowMode.TwoColorGradient:
                    payload["GradientColourSet"] = -1;
                    payload["GradientAnimationStyle"] = gradientAnimationStyle.ToString();
                    payload["Glow"] = ToVectorPayload(glow);
                    payload["Color3"] = ToVectorPayload(gradientSecondColor);
                    break;
                default:
                    payload["Glow"] = ToVectorPayload(glow);
                    break;
            }
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static object ToVectorPayload(Vector3 vector)
    {
        return new { vector.X, vector.Y, vector.Z };
    }

    private DisplayTitle SelectDisplayTitle(string baseTitle)
    {
        var sanitizedBaseTitle = SanitizeTitle(baseTitle);
        if (!Configuration.RotateHandwrittenTitles)
        {
            return new DisplayTitle(sanitizedBaseTitle, null);
        }

        var titles = new List<DisplayTitleOption>();
        if (!string.IsNullOrWhiteSpace(sanitizedBaseTitle))
        {
            titles.Add(new DisplayTitleOption(sanitizedBaseTitle, Math.Clamp(Configuration.HandwrittenTitleRotationSeconds, 2.0f, 120.0f), null));
        }

        titles.AddRange((Configuration.HandwrittenTitleEntries ?? [])
            .Select(entry => new DisplayTitleOption(
                SanitizeTitle(entry.Title),
                Math.Clamp(entry.DurationSeconds, 2.0f, 120.0f),
                entry.Style))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Title)));

        if (titles.Count == 0)
        {
            return new DisplayTitle(sanitizedBaseTitle, null);
        }

        if (titles.Count <= 1)
        {
            return new DisplayTitle(titles[0].Title, titles[0].Style);
        }

        var cycleSeconds = titles.Sum(entry => entry.DurationSeconds);
        var cursor = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0) % cycleSeconds;
        foreach (var title in titles)
        {
            if (cursor < title.DurationSeconds)
            {
                return new DisplayTitle(title.Title, title.Style);
            }

            cursor -= title.DurationSeconds;
        }

        return new DisplayTitle(titles[0].Title, titles[0].Style);
    }

    private static string BuildStatusTitle(ProjectStatus status)
    {
        var activity = NormalizeActivity(status.Activity);
        var project = status.Project.Trim();
        if (string.IsNullOrWhiteSpace(project) && string.IsNullOrWhiteSpace(status.Activity) && !string.IsNullOrWhiteSpace(status.Title))
        {
            return status.Title;
        }

        return FormatProjectTitle(activity, project);
    }

    private static string NormalizeActivity(string activity)
    {
        var normalized = (activity ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Developing";
        }

        return normalized.ToLowerInvariant() switch
        {
            "dev" or "develop" or "developing" or "work" or "working" or "coding" or "implement" or "implementing" => "Developing",
            "audit" or "auditing" or "review" or "reviewing" or "check" or "checking" or "verify" or "verifying" => "Auditing",
            "build" or "building" or "compile" or "compiling" => "Building",
            "test" or "testing" or "validate" or "validating" or "validation" => "Testing",
            "commit" or "committing" => "Committing",
            "push" or "pushing" or "push update" or "pushing update" or "release" or "releasing" or "publish" or "publishing" or "deploy" or "deploying" => "Pushing",
            "changelog" or "changelog update" or "changelog updates" or "update changelog" or "updating changelog" or "wording" or "word fixing" or "description" or "readme" or "notes" or "release notes" => "Updating Changelog",
            _ => normalized,
        };
    }

    private static string FormatProjectTitle(string activity, string project)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return GetActivityTitleLabel(activity);
        }

        return $"{GetActivityTitleLabel(activity)}: {project}";
    }

    private static string GetActivityTitleLabel(string activity)
    {
        return activity switch
        {
            "Developing" => "Dev",
            "Auditing" => "Audit",
            "Building" => "Build",
            "Testing" => "Test",
            "Committing" => "Commit",
            "Pushing" => "Push",
            "Updating Changelog" => "Log",
            _ => activity,
        };
    }

    private static string BuildStatusSummary(ProjectStatus status)
    {
        var parts = new[]
            {
                status.Activity,
                status.Project,
                status.Details,
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim());
        var summary = string.Join(" - ", parts);
        return string.IsNullOrWhiteSpace(summary) ? "No summary" : summary;
    }

    private static string SanitizeTitle(string rawTitle)
    {
        var text = new string((rawTitle ?? string.Empty)
            .Where(character => !char.IsControl(character))
            .ToArray()).Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }

        return TruncateForHonorific(text, MaxTitleLength, addEllipsis: true);
    }

    private static string CleanEditableTitle(string rawTitle)
    {
        var text = new string((rawTitle ?? string.Empty)
            .Where(character => !char.IsControl(character))
            .ToArray());
        return TruncateForHonorific(text, MaxTitleLength, addEllipsis: false);
    }

    private static string TruncateForHonorific(string text, int maxChars, bool addEllipsis)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var indexes = StringInfo.ParseCombiningCharacters(text);
        var suffix = addEllipsis ? "..." : string.Empty;
        var maxPrefixChars = Math.Max(0, maxChars - suffix.Length);
        var endIndex = 0;
        foreach (var index in indexes)
        {
            if (index > maxPrefixChars)
            {
                break;
            }

            endIndex = index;
        }

        var nextIndex = indexes.FirstOrDefault(index => index > endIndex);
        if (nextIndex > 0 && nextIndex <= maxPrefixChars)
        {
            endIndex = nextIndex;
        }

        if (endIndex == 0)
        {
            return suffix.Length <= maxChars ? suffix : string.Empty;
        }

        var truncated = text[..endIndex];
        return addEllipsis ? truncated.TrimEnd() + suffix : truncated;
    }

    private static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Configuration.DefaultStatusFilePath;
        }

        if (path.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.GetFullPath(Path.Combine(home, path[1..].TrimStart('\\', '/')));
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}
