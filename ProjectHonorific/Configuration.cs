using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace ProjectHonorific;

public enum HonorificGlowMode
{
    Solid,
    PresetGradient,
    TwoColorGradient,
}

public enum HonorificGradientAnimationStyle
{
    Pulse,
    Wave,
    Static,
}

[Serializable]
public sealed class HonorificTitleStyle
{
    public bool UseColor { get; set; } = true;

    public Vector3 Color { get; set; } = new(0.82f, 0.71f, 1.0f);

    public bool UseGlow { get; set; } = true;

    public HonorificGlowMode GlowMode { get; set; } = HonorificGlowMode.PresetGradient;

    public Vector3 Glow { get; set; } = new(0.0f, 0.2f, 0.4f);

    public Vector3 GradientSecondColor { get; set; } = new(0.9f, 0.1f, 0.9f);

    public int GradientColourSet { get; set; } = 13;

    public HonorificGradientAnimationStyle GradientAnimationStyle { get; set; } = HonorificGradientAnimationStyle.Wave;
}

[Serializable]
public sealed class HandwrittenTitleEntry
{
    public string Title { get; set; } = string.Empty;

    public float DurationSeconds { get; set; } = 8.0f;

    public HonorificTitleStyle? Style { get; set; }
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public static readonly string[] HonorificGradientPresetNames =
    [
        "Pride Rainbow",
        "Transgender",
        "Lesbian",
        "Bisexual",
        "Black & White",
        "Black & Red",
        "Black & Blue",
        "Black & Yellow",
        "Black & Green",
        "Black & Pink",
        "Black & Cyan",
        "Cherry Blossom",
        "Golden",
        "Pastel Rainbow",
        "Dark Rainbow",
        "Non-binary",
    ];

    public static int MaxHonorificGradientPreset => HonorificGradientPresetNames.Length - 1;

    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public string StatusFilePath { get; set; } = DefaultStatusFilePath;

    public float PollIntervalSeconds { get; set; } = 2.0f;

    public float StaleAfterSeconds { get; set; } = 600.0f;

    public bool ShowIdle { get; set; }

    public string IdleTitle { get; set; } = "Project idle";

    public bool RotateHandwrittenTitles { get; set; } = true;

    public float HandwrittenTitleRotationSeconds { get; set; } = 8.0f;

    public List<HandwrittenTitleEntry> HandwrittenTitleEntries { get; set; } = [];

    public List<string> HandwrittenTitles { get; set; } = [];

    public bool IsPrefix { get; set; }

    public bool UseColor { get; set; } = true;

    public Vector3 Color { get; set; } = new(0.82f, 0.71f, 1.0f);

    public bool UseGlow { get; set; } = true;

    public HonorificGlowMode GlowMode { get; set; } = HonorificGlowMode.PresetGradient;

    public Vector3 Glow { get; set; } = new(0.0f, 0.2f, 0.4f);

    public Vector3 GradientSecondColor { get; set; } = new(0.9f, 0.1f, 0.9f);

    public int GradientColourSet { get; set; } = 13;

    public HonorificGradientAnimationStyle GradientAnimationStyle { get; set; } = HonorificGradientAnimationStyle.Wave;

    public static string DefaultStatusFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "project-honorific-status.json");

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
