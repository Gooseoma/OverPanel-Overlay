namespace Overpanel.Overlay.Services;

/// <summary>Снимок того, что происходит в панели. Из него собирается Discord activity.</summary>
public sealed class PanelSnapshot
{
    public string Path { get; init; } = "/";
    public string Title { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string RoleLabel { get; init; } = string.Empty;
    public string RoleTag { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsAction { get; init; }

    public bool SameAs(PanelSnapshot other) =>
        Path == other.Path &&
        Details == other.Details &&
        RoleLabel == other.RoleLabel &&
        RoleTag == other.RoleTag &&
        Project == other.Project &&
        DisplayName == other.DisplayName;

    public override string ToString() =>
        $"{Details} | {RoleLabel} [{RoleTag}] | {Project} | {Path}";
}
