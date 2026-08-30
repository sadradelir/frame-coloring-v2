using FrameColoringV2.Imaging;

namespace FrameColoringV2.Models;

/// <summary>
/// The set of frames currently open in the editor, wherever they came from on disk.
/// </summary>
public sealed class FrameSession : IDisposable
{
    private readonly List<FrameDocument> documents = new();

    public IReadOnlyList<FrameDocument> Documents => documents;

    public int Count => documents.Count;

    public bool HasUnsavedChanges => documents.Any(d => d.IsDirty);

    public int UnsavedCount => documents.Count(d => d.IsDirty);

    public FrameDocument this[int index] => documents[index];

    /// <summary>Folder the frames came from, or null when they are spread over several folders.</summary>
    public string? CommonFolder
    {
        get
        {
            if (documents.Count == 0) return null;
            string first = documents[0].FolderPath;
            return documents.All(d => string.Equals(d.FolderPath, first, StringComparison.OrdinalIgnoreCase))
                ? first
                : null;
        }
    }

    /// <summary>Loads the given files. Returns the paths that could not be loaded.</summary>
    public List<(string path, string error)> Open(IEnumerable<string> paths, bool replaceExisting)
    {
        if (replaceExisting) CloseAll();

        var failures = new List<(string, string)>();
        var existing = documents.Select(d => d.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string fullPath = Path.GetFullPath(path);
            if (!existing.Add(fullPath)) continue;

            try
            {
                documents.Add(FrameDocument.Load(fullPath));
            }
            catch (Exception ex)
            {
                failures.Add((fullPath, ex.Message));
            }
        }

        Sort();
        return failures;
    }

    public List<(string path, string error)> OpenFolder(string folder, bool replaceExisting) =>
        Open(EnumerateImages(folder), replaceExisting);

    public static IEnumerable<string> EnumerateImages(string folder) =>
        Directory.EnumerateFiles(folder).Where(FrameOps.IsSupportedImage);

    /// <summary>Expands a drop or a command line into image paths (folders are expanded one level).</summary>
    public static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (string file in EnumerateImages(path)) yield return file;
            }
            else if (File.Exists(path) && FrameOps.IsSupportedImage(path))
            {
                yield return path;
            }
        }
    }

    public void Remove(IEnumerable<FrameDocument> toRemove)
    {
        foreach (var document in toRemove.ToList())
        {
            if (documents.Remove(document)) document.Dispose();
        }
    }

    public void CloseAll()
    {
        foreach (var document in documents) document.Dispose();
        documents.Clear();
    }

    private void Sort() =>
        documents.Sort((a, b) => string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase));

    public void Dispose() => CloseAll();
}
