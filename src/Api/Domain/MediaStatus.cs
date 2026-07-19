namespace Keepr.Api.Domain;

/// <summary>Lifecycle of a media file. See docs/ai-design-decisions.md (D2, D9).</summary>
public enum MediaStatus
{
    /// <summary>Multipart upload started; bytes reserved against quota but not yet confirmed.</summary>
    Pending = 0,

    /// <summary>Upload completed and verified; counts against quota as actual bytes.</summary>
    Ready = 1,

    /// <summary>Upload failed or was aborted; reserved quota has been released.</summary>
    Failed = 2
}
