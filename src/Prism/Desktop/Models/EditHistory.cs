namespace Prism.Models;

/// <summary>Snapshots share immutable image payloads, and copy mutable geometry only.</summary>
public sealed class EditHistory
{
    public record Entry(EditorDocument Document,string Label,long Revision);
    public List<Entry> Entries { get; } = [];
    public int Index { get; private set; }
    private long _nextRevision;
    private long _savedRevision;
    public bool IsDirty => Entries.Count>0 && Entries[Index].Revision!=_savedRevision;
    public bool CanUndo=>Index>0;
    public bool CanRedo=>Index<Entries.Count-1;
    public bool Matches(EditorDocument doc)
    {
        if(Entries.Count==0)return false;
        var previous=Entries[Index].Document;
        return previous.Width==doc.Width && previous.Height==doc.Height && previous.Layers.Count==doc.Layers.Count && previous.Layers.Zip(doc.Layers).All(pair=>SameLayer(pair.First,pair.Second));
    }
    private static bool SameLayer(Layer a,Layer b)=>
        a.Id==b.Id&&a.Name==b.Name&&a.Kind==b.Kind&&a.Text==b.Text&&a.Color==b.Color&&a.FontFamily==b.FontFamily&&a.FontSize==b.FontSize&&a.Bold==b.Bold&&
        a.X==b.X&&a.Y==b.Y&&a.Width==b.Width&&a.Height==b.Height&&a.Rotation==b.Rotation&&a.Opacity==b.Opacity&&a.Visible==b.Visible&&a.Locked==b.Locked&&
        a.ImageData==b.ImageData&&a.ContentWidth==b.ContentWidth&&a.ContentHeight==b.ContentHeight&&SameStrokes(a.Strokes,b.Strokes)&&SameStrokes(a.Erasures,b.Erasures);
    private static bool SameStrokes(List<PaintStroke> a,List<PaintStroke> b)=>a.Count==b.Count&&a.Zip(b).All(p=>p.First.Size==p.Second.Size&&p.First.Color==p.Second.Color&&p.First.IsEraser==p.Second.IsEraser&&p.First.Clip==p.Second.Clip&&p.First.Points.SequenceEqual(p.Second.Points)&&(p.First.ClipPolygon??[]).SequenceEqual(p.Second.ClipPolygon??[]));
    public void Reset(EditorDocument document,string label="Open document")
    {
        Entries.Clear();_nextRevision=0;_savedRevision=0;Index=0;
        Entries.Add(new(document.Clone(),label,0));
    }
    public void Push(EditorDocument document,string label)
    {
        if(Index<Entries.Count-1)Entries.RemoveRange(Index+1,Entries.Count-Index-1);
        Entries.Add(new(document.Clone(),label,++_nextRevision));
        if(Entries.Count>60)Entries.RemoveAt(0);
        Index=Entries.Count-1;
    }
    public EditorDocument Restore(int index)
    {
        if(index<0||index>=Entries.Count)throw new ArgumentOutOfRangeException(nameof(index));
        Index=index;return Entries[index].Document.Clone();
    }
    public void MarkSaved()=>_savedRevision=Entries[Index].Revision;
}
