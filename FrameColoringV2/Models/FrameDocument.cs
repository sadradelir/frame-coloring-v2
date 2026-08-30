using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SImage = SixLabors.ImageSharp.Image;

namespace FrameColoringV2.Models;

/// <summary>
/// A single frame that is open in the editor: the pixels plus the file they came from.
/// The file can live anywhere on disk, nothing here assumes a folder next to the executable.
/// </summary>
public sealed class FrameDocument : IDisposable
{
    private FrameDocument(string path, Image<Rgba32> image)
    {
        FilePath = Path.GetFullPath(path);
        Image = image;
    }

    public string FilePath { get; private set; }

    public string FileName => Path.GetFileName(FilePath);

    public string FolderPath => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public Image<Rgba32> Image { get; private set; }

    public bool IsDirty { get; private set; }

    public string DisplayName => IsDirty ? FileName + " *" : FileName;

    public static FrameDocument Load(string path) => new(path, SImage.Load<Rgba32>(path));

    public void MarkDirty() => IsDirty = true;

    /// <summary>Swaps in a new image (crop, trim, ...) and disposes the previous one.</summary>
    public void ReplaceImage(Image<Rgba32> image)
    {
        if (ReferenceEquals(image, Image)) return;

        var old = Image;
        Image = image;
        old.Dispose();
        IsDirty = true;
    }

    /// <summary>
    /// Puts another image in place and hands the previous one back without disposing it,
    /// which is how the undo history swaps snapshots in and out.
    /// </summary>
    public Image<Rgba32> SwapImage(Image<Rgba32> replacement, bool dirty)
    {
        var previous = Image;
        Image = replacement;
        IsDirty = dirty;
        return previous;
    }

    public void Save() => SaveTo(FilePath, rebind: true);

    /// <summary>Saves to another path. When <paramref name="rebind"/> is true the document follows the new path.</summary>
    public void SaveTo(string path, bool rebind)
    {
        string fullPath = Path.GetFullPath(path);
        string? folder = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        // File.Create truncates, unlike OpenWrite, so shrinking images cannot leave stale bytes behind.
        using (var stream = File.Create(fullPath))
        {
            Image.SaveAsPng(stream);
        }

        if (rebind) FilePath = fullPath;
        IsDirty = false;
    }

    public void Reload()
    {
        var reloaded = SImage.Load<Rgba32>(FilePath);
        var old = Image;
        Image = reloaded;
        old.Dispose();
        IsDirty = false;
    }

    public void Dispose() => Image.Dispose();
}
