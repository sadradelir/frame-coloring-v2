using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FrameColoringV2.Models;

/// <summary>
/// Undo / redo for whole frames. A step holds a snapshot of every frame an operation
/// touched; undoing swaps the snapshot back in and keeps the current pixels for redo.
/// Old steps are dropped once the history grows past <see cref="MaxSteps"/> or
/// <see cref="MaxBytes"/>, so a long session cannot eat all the memory.
/// </summary>
public sealed class UndoHistory : IDisposable
{
    public const int MaxSteps = 40;
    public const long MaxBytes = 512L * 1024 * 1024;

    private readonly List<UndoStep> undoSteps = new();
    private readonly List<UndoStep> redoSteps = new();
    private UndoStep? pending;

    public bool CanUndo => undoSteps.Count > 0;

    public bool CanRedo => redoSteps.Count > 0;

    public string? UndoLabel => undoSteps.Count > 0 ? undoSteps[^1].Label : null;

    public string? RedoLabel => redoSteps.Count > 0 ? redoSteps[^1].Label : null;

    /// <summary>Starts collecting a step. Call <see cref="Capture"/> before the frames change.</summary>
    public void Begin(string label)
    {
        pending?.Dispose();
        pending = new UndoStep(label);
    }

    public void Capture(FrameDocument document) => pending?.Capture(document);

    public void Capture(IEnumerable<FrameDocument> documents)
    {
        foreach (var document in documents) Capture(document);
    }

    /// <summary>Convenience for operations that snapshot everything up front.</summary>
    public void BeginAndCapture(string label, IEnumerable<FrameDocument> documents)
    {
        Begin(label);
        Capture(documents);
    }

    public void Commit()
    {
        if (pending == null) return;

        if (pending.IsEmpty)
        {
            pending.Dispose();
            pending = null;
            return;
        }

        undoSteps.Add(pending);
        pending = null;

        ClearRedo();
        Trim();
    }

    public void Cancel()
    {
        pending?.Dispose();
        pending = null;
    }

    public string? Undo() => Move(undoSteps, redoSteps);

    public string? Redo() => Move(redoSteps, undoSteps);

    private static string? Move(List<UndoStep> from, List<UndoStep> to)
    {
        if (from.Count == 0) return null;

        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        step.Apply();
        to.Add(step);

        return step.Label;
    }

    /// <summary>Drops everything, for instance when the open frames are replaced.</summary>
    public void Clear()
    {
        Cancel();
        foreach (var step in undoSteps) step.Dispose();
        foreach (var step in redoSteps) step.Dispose();
        undoSteps.Clear();
        redoSteps.Clear();
    }

    /// <summary>Forgets any step that refers to frames that are no longer open.</summary>
    public void Forget(IEnumerable<FrameDocument> documents)
    {
        var gone = documents.ToHashSet();
        if (gone.Count == 0) return;

        RemoveWhere(undoSteps, step => step.Touches(gone));
        RemoveWhere(redoSteps, step => step.Touches(gone));

        if (pending != null && pending.Touches(gone)) Cancel();
    }

    private static void RemoveWhere(List<UndoStep> steps, Func<UndoStep, bool> predicate)
    {
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            if (!predicate(steps[i])) continue;

            steps[i].Dispose();
            steps.RemoveAt(i);
        }
    }

    private void ClearRedo()
    {
        foreach (var step in redoSteps) step.Dispose();
        redoSteps.Clear();
    }

    private void Trim()
    {
        while (undoSteps.Count > MaxSteps || TotalBytes() > MaxBytes)
        {
            if (undoSteps.Count <= 1) return; // always keep at least one step

            undoSteps[0].Dispose();
            undoSteps.RemoveAt(0);
        }
    }

    private long TotalBytes() => undoSteps.Sum(step => step.Bytes) + redoSteps.Sum(step => step.Bytes);

    public void Dispose() => Clear();

    private sealed class UndoStep : IDisposable
    {
        private readonly List<Entry> entries = new();

        public UndoStep(string label) => Label = label;

        public string Label { get; }

        public bool IsEmpty => entries.Count == 0;

        public long Bytes => entries.Sum(entry => (long)entry.Snapshot.Width * entry.Snapshot.Height * 4);

        public void Capture(FrameDocument document)
        {
            if (entries.Any(entry => entry.Document == document)) return;

            entries.Add(new Entry(document, document.Image.Clone(), document.IsDirty));
        }

        public bool Touches(HashSet<FrameDocument> documents) => entries.Any(entry => documents.Contains(entry.Document));

        /// <summary>Swaps the stored pixels with the current ones, which makes this reversible.</summary>
        public void Apply()
        {
            foreach (var entry in entries)
            {
                bool dirtyNow = entry.Document.IsDirty;
                entry.Snapshot = entry.Document.SwapImage(entry.Snapshot, entry.WasDirty);
                entry.WasDirty = dirtyNow;
            }
        }

        public void Dispose()
        {
            foreach (var entry in entries) entry.Snapshot.Dispose();
            entries.Clear();
        }

        private sealed class Entry
        {
            public Entry(FrameDocument document, Image<Rgba32> snapshot, bool wasDirty)
            {
                Document = document;
                Snapshot = snapshot;
                WasDirty = wasDirty;
            }

            public FrameDocument Document { get; }
            public Image<Rgba32> Snapshot { get; set; }
            public bool WasDirty { get; set; }
        }
    }
}
