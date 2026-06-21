using System;

namespace ProjectHonorific;

public sealed class ProjectStatus
{
    public int Version { get; set; } = 1;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Project { get; set; } = string.Empty;

    public string Activity { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string ToolProvider { get; set; } = "codex";

    public bool Detectable { get; set; } = true;

    public string Workspace { get; set; } = string.Empty;
}
