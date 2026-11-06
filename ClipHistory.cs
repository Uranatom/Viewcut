namespace Viewcut;

internal sealed record ClipEditState(ClipDraft[] Clips, Guid[] Selection, Guid? Primary);
internal sealed record ClipEdit(string Name, ClipEditState Before, ClipEditState After);

// History is bounded and kept in memory; exported files and jobs are independent snapshots.
internal sealed class ClipHistory
{
    private const int Limit = 100;
    private readonly List<ClipEdit> edits = [];
    private int cursor;
    public long Access { get; set; }
    public ClipEdit? Undo => cursor > 0 ? edits[cursor - 1] : null;
    public ClipEdit? Redo => cursor < edits.Count ? edits[cursor] : null;
    private ClipEditState? Current => cursor > 0 ? edits[cursor - 1].After : edits.FirstOrDefault()?.Before;

    public void Validate(ClipDraft[] current)
    {
        if (Current is { } expected && !current.SequenceEqual(expected.Clips)) Clear();
    }

    public void Record(string name, ClipEditState before, ClipEditState after)
    {
        if (before.Clips.SequenceEqual(after.Clips)) return;
        Validate(before.Clips);
        if (cursor < edits.Count) edits.RemoveRange(cursor, edits.Count - cursor);
        edits.Add(new ClipEdit(name, before, after));
        if (edits.Count > Limit) edits.RemoveAt(0);
        cursor = edits.Count;
    }

    public void Applied(bool redo) => cursor += redo ? 1 : -1;
    public void Clear() { edits.Clear(); cursor = 0; }
}
