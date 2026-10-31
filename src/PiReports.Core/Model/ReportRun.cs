namespace PiReports.Core.Model;

public enum RunStatus
{
    Running,
    Succeeded,
    Failed,
}

public enum RunTrigger
{
    Schedule,
    Manual,
}

public class ReportRun
{
    public long Id { get; set; }

    public int DefinitionId { get; set; }

    public CaptureDefinition? Definition { get; set; }

    public RunTrigger Trigger { get; set; }

    public string? RequestedBy { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Running;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Path of the archived output under the configured output root.</summary>
    public string? OutputPath { get; set; }

    public string? FileName { get; set; }

    public long? OutputBytes { get; set; }

    /// <summary>Non-fatal problems, e.g. the display never stopped changing or a delivery failed.</summary>
    public string? Warnings { get; set; }

    public string? Error { get; set; }
}
