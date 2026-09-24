using System.Security.Cryptography;
using System.Text;

namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Records the state of a target Unity project BEFORE Harness deployment,
/// so that Cleanup can restore it exactly to its original state.
///
/// Designed for HarnessDeployer — tracks exactly what was changed and what
/// pre-existed, so Cleanup never deletes or overwrites student-created files.
/// </summary>
public class HarnessSnapshot
{
    /// <summary>
    /// Absolute paths of .cs files that WE injected (did not previously exist).
    /// Cleanup will delete these, plus the corresponding .meta files.
    /// </summary>
    public List<string> FilesInjectedByUs { get; init; } = new();

    /// <summary>
    /// Absolute paths of .cs files that already existed in the target project
    /// before deployment. Cleanup MUST NOT touch these.
    /// </summary>
    public List<string> FilesPreExisting { get; init; } = new();

    /// <summary>
    /// Original raw bytes of EditorBuildSettings.asset before any modification.
    /// Null if the file did not exist.
    /// </summary>
    public byte[]? OriginalBuildSettingsBytes { get; init; }

    /// <summary>
    /// SHA-256 hex string of OriginalBuildSettingsBytes.
    /// Used during Cleanup to verify byte-for-byte restoration.
    /// </summary>
    public string? OriginalBuildSettingsSha256 { get; init; }

    /// <summary>
    /// True if we actually modified EditorBuildSettings.asset (e.g. added a scene).
    /// If false, Cleanup MUST NOT overwrite the file.
    /// </summary>
    public bool BuildSettingsModified { get; set; }

    /// <summary>
    /// True if DeployAsync completed without throwing.
    /// Used by Cleanup to decide whether to attempt file deletion.
    /// </summary>
    public bool DeployCompletedSuccessfully { get; set; }

    /// <summary>
    /// Computes the SHA-256 hex string of a byte array.
    /// </summary>
    public static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Reads a file and returns its SHA-256 hex string, or null if the file does not exist.
    /// </summary>
    public static string? ComputeFileSha256(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;
            var bytes = File.ReadAllBytes(filePath);
            return ComputeSha256(bytes);
        }
        catch
        {
            return null;
        }
    }
}