using Prism.Models;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Prism.Controls;
public class DocumentSurface : FrameworkElement
{
    public EditorDocument? Document { get; set; }
    public Layer? SelectedLayer { get; set; }
    public double Zoom { get; set; } = .55;
    public bool ShowSelection { get; set; } = true;
    public Rect? Marquee { get; set; }
    public bool Cropping { get; set; }
    public Point? BrushPosition { get; set; }
    public double BrushDiameter { get; set; }
    public void Refresh() { if(Document is null||!double.IsFinite(Zoom)||Zoom<=0)return; Width=Document.Width*Zoom;Height=Document.Height*Zoom;InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(Document == null)return;
        dc.PushTransform(new ScaleTransform(Zoom,Zoom));
        DrawDocument(dc);
        var accent=Layer.Brush("#BBA4FF");
        if(ShowSelection && SelectedLayer is {Visible:true,Locked:false} layer) {
            var rect=layer.Bounds;
            dc.PushTransform(new RotateTransform(layer.Rotation,rect.X+rect.Width/2,rect.Y+rect.Height/2));
            dc.DrawRectangle(null,new Pen(accent,1/Zoom),rect);
            foreach(var x in new[]{rect.Left,rect.Left+rect.Width/2,rect.Right}) foreach(var y in new[]{rect.Top,rect.Top+rect.Height/2,rect.Bottom}) {
                if(x==rect.Left+rect.Width/2 && y==rect.Top+rect.Height/2)continue;
                dc.DrawRectangle(Layer.Brush("#282332"),new Pen(accent,1/Zoom),new Rect(x-3/Zoom,y-3/Zoom,6/Zoom,6/Zoom));
            }
            dc.Pop();
        }
        if(Marquee is Rect selection && selection.Width>0 && selection.Height>0) {
            if(Cropping) {
                var outside=new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(new Rect(0,0,Document.Width,Document.Height)),new RectangleGeometry(selection));
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(142,10,9,16)),null,outside);
            }
            var pen=new Pen(Brushes.White,1/Zoom){DashStyle=new DashStyle(new double[]{4,4},0)};
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(24,184,164,255)),new Pen(Layer.Brush("#26212F"),2/Zoom),selection);
            dc.DrawRectangle(null,pen,selection);
            if(Cropping) for(int i=1;i<3;i++) {
                dc.DrawLine(new Pen(Brushes.White,.5/Zoom),new Point(selection.X+selection.Width*i/3,selection.Y),new Point(selection.X+selection.Width*i/3,selection.Bottom));
                dc.DrawLine(new Pen(Brushes.White,.5/Zoom),new Point(selection.X,selection.Y+selection.Height*i/3),new Point(selection.Right,selection.Y+selection.Height*i/3));
            }
        }
        if(BrushPosition is Point pointer && BrushDiameter>0) {
            dc.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromArgb(160,0,0,0)),2.5/Zoom),pointer,BrushDiameter/2,BrushDiameter/2);
            dc.DrawEllipse(null,new Pen(Brushes.White,.9/Zoom),pointer,BrushDiameter/2,BrushDiameter/2);
        }
        dc.Pop();
    }
    public void DrawDocument(DrawingContext dc)
    {
        if(Document==null)return;
        dc.PushClip(new RectangleGeometry(new Rect(0,0,Document.Width,Document.Height)));
        // Transparency uses a tiled drawing, so zoomed-out documents remain inexpensive.
        var pattern = new DrawingGroup();
        using(var tile=pattern.Open()) { tile.DrawRectangle(Layer.Brush("#EEEEF0"),null,new Rect(0,0,24,24));tile.DrawRectangle(Layer.Brush("#D1D1D5"),null,new Rect(0,0,12,12));tile.DrawRectangle(Layer.Brush("#D1D1D5"),null,new Rect(12,12,12,12)); }
        dc.DrawRectangle(new DrawingBrush(pattern){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,24,24)},null,new Rect(0,0,Document.Width,Document.Height));
        foreach(var layer in Document.Layers.AsEnumerable().Reverse().Where(l=>l.Visible)) DrawLayer(dc,layer);
        dc.Pop();
    }
    public static void DrawLayer(DrawingContext dc,Layer layer)
    {
        dc.PushOpacity(layer.Opacity);
        dc.PushTransform(new RotateTransform(layer.Rotation,layer.X+layer.Width/2,layer.Y+layer.Height/2));
        dc.PushTransform(new TranslateTransform(layer.X,layer.Y));
        dc.PushTransform(new ScaleTransform(layer.Width/layer.SourceWidth,layer.Height/layer.SourceHeight));
        var bounds=new Rect(0,0,layer.SourceWidth,layer.SourceHeight);
        foreach(var erasure in layer.Erasures)dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(bounds),StrokeArea(erasure)));
        switch(layer.Kind) {
            case "image": if(layer.GetImage() is {} image) {
                var brush=new ImageBrush(image){Stretch=Stretch.UniformToFill,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Center};
                dc.DrawRectangle(brush,null,bounds);
            } break;
            case "text":
                var ft=layer.Format();
                dc.PushTransform(new ScaleTransform(layer.SourceWidth/Math.Max(1,ft.WidthIncludingTrailingWhitespace),layer.SourceHeight/Math.Max(1,ft.Height)));
                dc.DrawText(ft,new Point());dc.Pop();break;
            case "gradient":
                var gradient=new LinearGradientBrush(); gradient.StartPoint=new Point(.5,0);gradient.EndPoint=new Point(.5,1);
                gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(layer.Color),0));gradient.GradientStops.Add(new GradientStop(Colors.Transparent,.65));gradient.GradientStops.Add(new GradientStop(Color.FromArgb(210,9,19,18),1));
                dc.DrawRectangle(gradient,null,bounds);break;
            case "paint":
                var painting=new DrawingGroup();
                foreach(var stroke in layer.Strokes) {
                    if(stroke.Points.Count==0)continue;
                    var geometry=StrokeArea(stroke);
                    if(stroke.IsEraser) {
                        var clipped=new DrawingGroup { ClipGeometry=new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(bounds),geometry) };
                        clipped.Children.Add(painting);painting=clipped;
                        // A new outer group lets later strokes paint over previously erased pixels.
                        var next=new DrawingGroup();next.Children.Add(painting);painting=next;
                    } else painting.Children.Add(new GeometryDrawing(Layer.Brush(stroke.Color),null,geometry));
                }
                dc.DrawDrawing(painting);break;
            default: dc.DrawRectangle(Layer.Brush(layer.Color),null,bounds);break;
        }
        foreach(var _ in layer.Erasures)dc.Pop();
        dc.Pop();dc.Pop();dc.Pop();dc.Pop();
    }
    public static Geometry StrokeArea(PaintStroke stroke)
    {
        if(stroke.Points.Count==0)return Geometry.Empty;
        Geometry geometry;
        if(stroke.Points.Count==1)geometry=new EllipseGeometry(stroke.Points[0],stroke.Size/2,stroke.Size/2);
        else {
            var path=new StreamGeometry();using(var ctx=path.Open()){ctx.BeginFigure(stroke.Points[0],false,false);ctx.PolyLineTo(stroke.Points.Skip(1).ToList(),true,false);}
            geometry=path.GetWidenedPathGeometry(new Pen(Brushes.Black,stroke.Size){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round});
        }
        Geometry? clip=null;
        if(stroke.ClipPolygon is {Count:>=3} points){var polygon=new StreamGeometry();using(var ctx=polygon.Open()){ctx.BeginFigure(points[0],true,true);ctx.PolyLineTo(points.Skip(1).ToList(),true,false);}clip=polygon;}
        else if(stroke.Clip is Rect rect)clip=new RectangleGeometry(rect);
        return clip==null?geometry:new CombinedGeometry(GeometryCombineMode.Intersect,geometry,clip);
    }
    public BitmapSource RenderBitmap(bool transparent=true,double scale=1)
    {
        if(Document==null)throw new InvalidOperationException("No document is open.");
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()) {
            dc.PushTransform(new ScaleTransform(scale,scale));
            dc.PushClip(new RectangleGeometry(new Rect(0,0,Document.Width,Document.Height)));
            if(!transparent)dc.DrawRectangle(Brushes.White,null,new Rect(0,0,Document.Width,Document.Height));
            foreach(var layer in Document.Layers.AsEnumerable().Reverse().Where(l=>l.Visible))DrawLayer(dc,layer);
            dc.Pop();dc.Pop();
        }
        var result=new RenderTargetBitmap(Math.Max(1,(int)Math.Round(Document.Width*scale)),Math.Max(1,(int)Math.Round(Document.Height*scale)),96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return result;
    }
}
