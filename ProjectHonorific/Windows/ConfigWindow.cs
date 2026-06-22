using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace ProjectHonorific.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private const int TitleInputBufferLength = 256;

    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("Project Honorific###ProjectHonorificConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(560.0f, 420.0f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var config = plugin.Configuration;

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            plugin.SetEnabled(enabled);
        }

        ImGui.SameLine();
        var displayProjectInformation = config.DisplayProjectInformation;
        if (ImGui.Checkbox("Display", ref displayProjectInformation))
        {
            plugin.SetDisplayProjectInformation(displayProjectInformation);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Include the generated project or idle title in the rotation. Handwritten titles can still rotate when this is off.");
        }

        var statusFilePath = config.StatusFilePath;
        if (ImGui.InputText("Status file", ref statusFilePath, 512))
        {
            plugin.SetStatusFilePath(statusFilePath);
        }

        if (ImGui.Button("Reload"))
        {
            plugin.Reload();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear title"))
        {
            plugin.ClearTitleFromUi();
        }

        ImGui.SameLine();
        if (ImGui.Button("Write sample"))
        {
            plugin.WriteSampleStatus();
        }

        var pollInterval = config.PollIntervalSeconds;
        if (ImGui.SliderFloat("Poll interval", ref pollInterval, 1.0f, 60.0f, "%.0f sec"))
        {
            plugin.SetPollIntervalSeconds(pollInterval);
        }

        var staleAfter = config.StaleAfterSeconds;
        if (ImGui.SliderFloat("Stale after", ref staleAfter, 10.0f, 3600.0f, "%.0f sec"))
        {
            plugin.SetStaleAfterSeconds(staleAfter);
        }

        var showIdle = config.ShowIdle;
        if (ImGui.Checkbox("Show idle title when inactive", ref showIdle))
        {
            plugin.SetShowIdle(showIdle);
        }

        var idleTitle = config.IdleTitle;
        if (ImGui.InputText("Idle title", ref idleTitle, TitleInputBufferLength))
        {
            plugin.SetIdleTitle(idleTitle);
        }

        DrawHandwrittenTitleSettings(config);

        var isPrefix = config.IsPrefix;
        if (ImGui.Checkbox("Use as prefix", ref isPrefix))
        {
            plugin.SetIsPrefix(isPrefix);
        }

        ImGui.Separator();
        DrawStyleSettings(config);
        ImGui.Separator();
        DrawStatus();
    }

    private void DrawHandwrittenTitleSettings(Configuration config)
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Handwritten title rotation");
        DrawDisabledWrapped("When Display is on, the generated project or idle title is included. Handwritten titles can still rotate without project information when Display is off.");

        var rotate = config.RotateHandwrittenTitles;
        if (ImGui.Checkbox("Rotate handwritten titles", ref rotate))
        {
            plugin.SetRotateHandwrittenTitles(rotate);
        }

        var rotationSeconds = config.HandwrittenTitleRotationSeconds;
        if (ImGui.SliderFloat("Generated title duration", ref rotationSeconds, 2.0f, 120.0f, "%.0f sec"))
        {
            plugin.SetHandwrittenTitleRotationSeconds(rotationSeconds);
        }

        if (ImGui.Button("Add handwritten title"))
        {
            plugin.AddHandwrittenTitle();
        }

        for (var i = 0; i < config.HandwrittenTitleEntries.Count; i++)
        {
            var entry = config.HandwrittenTitleEntries[i];
            var title = entry.Title;
            ImGui.TextUnformatted($"Title {i + 1}");
            ImGui.SameLine();
            var removeWidth = ImGui.CalcTextSize("Remove").X + (ImGui.GetStyle().FramePadding.X * 2.0f);
            var countWidth = ImGui.CalcTextSize($"{Plugin.MaxTitleLength}/{Plugin.MaxTitleLength}").X;
            var inputWidth = ImGui.GetContentRegionAvail().X - removeWidth - countWidth - (ImGui.GetStyle().ItemSpacing.X * 2.0f);
            ImGui.SetNextItemWidth(MathF.Max(140.0f, inputWidth));
            if (ImGui.InputText($"##HandwrittenTitle{i}", ref title, TitleInputBufferLength))
            {
                plugin.SetHandwrittenTitle(i, title);
            }

            ImGui.SameLine();
            DrawTitleCharacterCount(title.Length);

            ImGui.SameLine();
            if (ImGui.Button($"Remove##HandwrittenTitle{i}"))
            {
                plugin.RemoveHandwrittenTitle(i);
                break;
            }

            var durationSeconds = entry.DurationSeconds;
            if (ImGui.SliderFloat($"Title {i + 1} duration##HandwrittenTitleDuration{i}", ref durationSeconds, 2.0f, 120.0f, "%.0f sec"))
            {
                plugin.SetHandwrittenTitleDuration(i, durationSeconds);
            }

            var style = entry.Style ?? new HonorificTitleStyle();
            if (ImGui.TreeNode($"Title {i + 1} Honorific style##HandwrittenTitleStyle{i}"))
            {
                DrawDisabledWrapped("This style only applies to this handwritten title. Removing the title removes its style.");
                DrawStyleControls(
                    $"HandwrittenTitleStyle{i}",
                    style.UseColor,
                    value => plugin.SetHandwrittenTitleUseColor(i, value),
                    style.Color,
                    value => plugin.SetHandwrittenTitleColor(i, value),
                    style.UseGlow,
                    value => plugin.SetHandwrittenTitleUseGlow(i, value),
                    style.GlowMode,
                    value => plugin.SetHandwrittenTitleGlowMode(i, value),
                    style.Glow,
                    value => plugin.SetHandwrittenTitleGlow(i, value),
                    style.GradientSecondColor,
                    value => plugin.SetHandwrittenTitleGradientSecondColor(i, value),
                    style.GradientColourSet,
                    value => plugin.SetHandwrittenTitleGradientColourSet(i, value),
                    style.GradientAnimationStyle,
                    value => plugin.SetHandwrittenTitleGradientAnimationStyle(i, value));
                ImGui.TreePop();
            }
        }
    }

    private static void DrawTitleCharacterCount(int length)
    {
        var color = length > Plugin.MaxTitleLength
            ? new Vector4(1.0f, 0.25f, 0.25f, 1.0f)
            : length == Plugin.MaxTitleLength
                ? new Vector4(1.0f, 0.75f, 0.2f, 1.0f)
                : new Vector4(0.65f, 0.65f, 0.65f, 1.0f);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted($"{length}/{Plugin.MaxTitleLength}");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Honorific counts C# string length, so some symbols may count as more than one character.");
        }
    }

    private void DrawStyleSettings(Configuration config)
    {
        ImGui.TextUnformatted("Honorific title capabilities");
        DrawDisabledWrapped("Sends title, prefix/suffix, color, glow, preset gradient, two-color gradient, and animation fields through Honorific IPC. These global settings apply to generated project titles and become the starting style for new handwritten titles.");

        DrawStyleControls(
            "GlobalStyle",
            config.UseColor,
            plugin.SetUseColor,
            config.Color,
            plugin.SetColor,
            config.UseGlow,
            plugin.SetUseGlow,
            config.GlowMode,
            plugin.SetGlowMode,
            config.Glow,
            plugin.SetGlow,
            config.GradientSecondColor,
            plugin.SetGradientSecondColor,
            config.GradientColourSet,
            plugin.SetGradientColourSet,
            config.GradientAnimationStyle,
            plugin.SetGradientAnimationStyle);
    }

    private void DrawStyleControls(
        string id,
        bool useColor,
        Action<bool> setUseColor,
        Vector3 color,
        Action<Vector3> setColor,
        bool useGlow,
        Action<bool> setUseGlow,
        HonorificGlowMode glowMode,
        Action<HonorificGlowMode> setGlowMode,
        Vector3 glow,
        Action<Vector3> setGlow,
        Vector3 gradientSecondColor,
        Action<Vector3> setGradientSecondColor,
        int gradientColourSet,
        Action<int> setGradientColourSet,
        HonorificGradientAnimationStyle gradientAnimationStyle,
        Action<HonorificGradientAnimationStyle> setGradientAnimationStyle)
    {
        if (ImGui.Checkbox($"Use color##{id}UseColor", ref useColor))
        {
            setUseColor(useColor);
        }

        if (ImGui.ColorEdit3($"Color##{id}Color", ref color))
        {
            setColor(color);
        }

        if (ImGui.Checkbox($"Use glow##{id}UseGlow", ref useGlow))
        {
            setUseGlow(useGlow);
        }

        if (ImGui.BeginCombo($"Glow mode##{id}GlowMode", GetGlowModeLabel(glowMode)))
        {
            foreach (var mode in Enum.GetValues<HonorificGlowMode>())
            {
                var selected = mode == glowMode;
                if (ImGui.Selectable(GetGlowModeLabel(mode), selected))
                {
                    setGlowMode(mode);
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        if (glowMode is HonorificGlowMode.PresetGradient or HonorificGlowMode.TwoColorGradient)
        {
            DrawDisabledWrapped("Preset and two-color gradients are Honorific supporter/donator visual options.");
        }

        if (glowMode is HonorificGlowMode.Solid or HonorificGlowMode.TwoColorGradient)
        {
            if (ImGui.ColorEdit3($"{(glowMode == HonorificGlowMode.TwoColorGradient ? "First gradient color" : "Glow color")}##{id}Glow", ref glow))
            {
                setGlow(glow);
            }
        }

        if (glowMode == HonorificGlowMode.TwoColorGradient)
        {
            if (ImGui.ColorEdit3($"Second gradient color##{id}SecondGradientColor", ref gradientSecondColor))
            {
                setGradientSecondColor(gradientSecondColor);
            }
        }

        if (glowMode == HonorificGlowMode.PresetGradient)
        {
            DrawGradientPresetCombo(gradientColourSet, id, setGradientColourSet);
        }

        if (glowMode is HonorificGlowMode.PresetGradient or HonorificGlowMode.TwoColorGradient)
        {
            if (ImGui.BeginCombo($"Gradient animation##{id}GradientAnimation", gradientAnimationStyle.ToString()))
            {
                foreach (var style in Enum.GetValues<HonorificGradientAnimationStyle>())
                {
                    var selected = style == gradientAnimationStyle;
                    if (ImGui.Selectable(style.ToString(), selected))
                    {
                        setGradientAnimationStyle(style);
                    }

                    if (selected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
        }
    }

    private void DrawGradientPresetCombo(int currentPreset, string id, Action<int> setGradientColourSet)
    {
        if (!ImGui.BeginCombo($"Gradient preset##{id}GradientPreset", GetGradientPresetLabel(currentPreset)))
        {
            return;
        }

        for (var preset = 0; preset < Configuration.HonorificGradientPresetNames.Length; preset++)
        {
            var selected = preset == currentPreset;
            if (ImGui.Selectable(GetGradientPresetLabel(preset), selected))
            {
                setGradientColourSet(preset);
            }

            if (selected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private static string GetGlowModeLabel(HonorificGlowMode mode)
    {
        return mode switch
        {
            HonorificGlowMode.Solid => "Solid glow",
            HonorificGlowMode.PresetGradient => "Preset gradient",
            HonorificGlowMode.TwoColorGradient => "Two-color gradient",
            _ => mode.ToString(),
        };
    }

    private static string GetGradientPresetLabel(int preset)
    {
        return preset >= 0 && preset < Configuration.HonorificGradientPresetNames.Length
            ? $"{preset}: {Configuration.HonorificGradientPresetNames[preset]}"
            : $"Unknown preset ({preset})";
    }

    private static void DrawDisabledWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.65f, 0.65f, 1.0f));
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private void DrawStatus()
    {
        ImGui.TextUnformatted("Current bridge status");
        ImGui.TextWrapped(plugin.LastSummary);
        if (!string.IsNullOrWhiteSpace(plugin.LastTitle))
        {
            ImGui.TextUnformatted($"Honorific title: {plugin.LastTitle}");
        }

        if (plugin.IsStale)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.65f, 0.2f, 1.0f), "Status is stale.");
        }

        if (!string.IsNullOrWhiteSpace(plugin.LastError))
        {
            ImGui.TextColored(new Vector4(1.0f, 0.2f, 0.2f, 1.0f), plugin.LastError);
        }

        if (plugin.CurrentStatus is not { } status)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted($"Project: {status.Project}");
        ImGui.TextUnformatted($"Activity: {status.Activity}");
        if (!string.IsNullOrWhiteSpace(status.Details))
        {
            ImGui.TextWrapped($"Details: {status.Details}");
        }

        if (!string.IsNullOrWhiteSpace(status.Workspace))
        {
            ImGui.TextWrapped($"Workspace folder: {status.Workspace}");
        }

        ImGui.TextUnformatted($"Updated: {status.UpdatedAt.LocalDateTime}");
    }
}
