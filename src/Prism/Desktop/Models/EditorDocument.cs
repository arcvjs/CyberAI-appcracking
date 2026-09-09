using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prism.Models;
public class EditorDocument
{
    public string Name { get; set; } = "Untitled";
    public double Width { get; set; } = 1500;
    public double Height { get; set; } = 1050;
    public List<Layer> Layers { get; set; } = [];
    public string Serialize() => JsonSerializer.Serialize(this);
    public static EditorDocument Deserialize(string data)
    {
        var document=JsonSerializer.Deserialize<EditorDocument>(data) ?? throw new InvalidDataException("This is not a Prism document.");
        document.Validate();
        return document;
    }
    public EditorDocument Clone() => new() { Name=Name,Width=Width,Height=Height,Layers=Layers.Select(l=>l.Clone()).ToList() };
    public void Validate()
    {
        if(!double.IsFinite(Width+Height)||Width<1||Height<1||Width>8000||Height>8000||Width*Height>24_000_000)
            throw new InvalidDataException("Use a canvas up to 8000 px per side and 24 megapixels.");
        if(Layers==null||Layers.Count>300)throw new InvalidDataException("A project can contain up to 300 layers.");
        foreach(var layer in Layers) {
            if(layer==null||layer.Width<1||layer.Height<1||layer.Width>100000||layer.Height>100000||!double.IsFinite(layer.X+layer.Y+layer.Width+layer.Height+layer.Rotation+layer.FontSize+layer.Opacity)||layer.Opacity<0||layer.Opacity>1||layer.FontSize<1||layer.FontSize>5000)
                throw new InvalidDataException("The project contains invalid layer dimensions.");
            if(layer.Kind is not ("text" or "image" or "paint" or "shape" or "gradient"))throw new InvalidDataException("This project contains an unsupported layer type.");
            _=Layer.Brush(layer.Color);
            if(layer.Strokes==null||layer.Erasures==null)throw new InvalidDataException("The project contains invalid paint data.");
            foreach(var stroke in layer.Strokes.Concat(layer.Erasures)) {
                if(stroke.Points==null||stroke.Points.Count>1_000_000||!double.IsFinite(stroke.Size)||stroke.Size<.1||stroke.Size>10000||stroke.Points.Any(p=>!double.IsFinite(p.X+p.Y)))throw new InvalidDataException("The project contains invalid brush strokes.");
                _=Layer.Brush(stroke.Color);
            }
        }
    }
    public static EditorDocument Sample()
    {
        var imagePath=Path.Combine(AppContext.BaseDirectory,"Assets","cipher-festival.png");
        var poster=new Layer {
            Name="CIPHER 26 poster",
            Kind="image",
            ImageData=Convert.ToBase64String(File.ReadAllBytes(imagePath))
        };
        var image=poster.GetImage()!;
        poster.Width=image.PixelWidth;
        poster.Height=image.PixelHeight;
        poster.Locked=true;
        var scale=image.PixelWidth/2668d;
        Layer TextLayer(string name,string text,double x,double y,double size,string color,bool bold=false)
        {
            var layer=new Layer {Name=name,Kind="text",Text=text,X=x*scale,Y=y*scale,
                FontFamily="Segoe UI",FontSize=size*scale,Color=color,Bold=bold};
            layer.SizeToText();
            return layer;
        }
        var headline=TextLayer("Made in · editable text","Made in",145,115,68,"#F3F7ED",true);
        var wordmark=TextLayer("Prism · editable text","Prism.",165,205,68,"#172317",true);
        var box=new Layer {Name="Prism · lime highlight",Kind="shape",Color="#B8E68B",
            X=145*scale,Y=197*scale,Width=wordmark.Width+40*scale,Height=wordmark.Height+20*scale};
        return new EditorDocument {
            Name="CIPHER 26",
            Width=image.PixelWidth,
            Height=image.PixelHeight,
            Layers=[headline,wordmark,box,poster]
        };
    }
}
public class Layer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Layer";
    public string Kind { get; set; } = "shape";
    public string Text { get; set; } = "Your text";
    public string Color { get; set; } = "#B8A4FF";
    public string FontFamily { get; set; } = "Arial";
    public double FontSize { get; set; } = 100;
    public bool Bold { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 200;
    public double Rotation { get; set; }
    public double Opacity { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public string? ImageData { get; set; }
    public List<PaintStroke> Strokes { get; set; } = [];
    public List<PaintStroke> Erasures { get; set; } = [];
    public double ContentWidth { get; set; }
    public double ContentHeight { get; set; }
    [JsonIgnore] public BitmapSource? CachedImage { get; set; }
    [JsonIgnore] public Rect Bounds => new(X,Y,Math.Max(1,Width),Math.Max(1,Height));
    [JsonIgnore] public double SourceWidth => ContentWidth>0?ContentWidth:Width;
    [JsonIgnore] public double SourceHeight => ContentHeight>0?ContentHeight:Height;
    [JsonIgnore] public string KindLabel => Kind switch { "text"=>"Type layer", "image"=>"Image layer", "paint"=>"Paint layer", "gradient"=>"Gradient overlay", _=>"Shape layer" };
    public BitmapSource? GetImage()
    {
        if (CachedImage != null || ImageData == null) return CachedImage;
        using var stream = new MemoryStream(Convert.FromBase64String(ImageData));
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); CachedImage=bitmap;
        return bitmap;
    }
    public Layer Clone() => new() {
        Id=Id,Name=Name,Kind=Kind,Text=Text,Color=Color,FontFamily=FontFamily,FontSize=FontSize,Bold=Bold,
        X=X,Y=Y,Width=Width,Height=Height,Rotation=Rotation,Opacity=Opacity,Visible=Visible,Locked=Locked,
        ImageData=ImageData,CachedImage=CachedImage,ContentWidth=ContentWidth,ContentHeight=ContentHeight,
        Strokes=Strokes.Select(s=>s.Clone()).ToList(),Erasures=Erasures.Select(s=>s.Clone()).ToList()
    };
    public Point Unrotate(Point point)=>new RotateTransform(-Rotation,X+Width/2,Y+Height/2).Transform(point);
    public Point ToLocal(Point point) { var p=Unrotate(point);return new Point((p.X-X)*SourceWidth/Width,(p.Y-Y)*SourceHeight/Height); }
    public bool Contains(Point point)=>Bounds.Contains(Unrotate(point));
    public void SizeToText() { var formatted=Format();Width=Math.Max(1,formatted.WidthIncludingTrailingWhitespace);Height=Math.Max(1,formatted.Height); }
    public static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    public FormattedText Format(double dpi = 1)
    {
        var formatted = new FormattedText(Text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily(FontFamily),FontStyles.Normal,Bold?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal),Math.Max(1,FontSize),Brush(Color),dpi);
        formatted.LineHeight=FontSize*1.02;
        return formatted;
    }
}
public class PaintStroke
{
    public List<Point> Points { get; set; } = [];
    public string Color { get; set; } = "#B8A4FF";
    public double Size { get; set; } = 20;
    public bool IsEraser { get; set; }
    public Rect? Clip { get; set; }
    public List<Point>? ClipPolygon { get; set; }
    public PaintStroke Clone()=>new() {Points=[..Points],Color=Color,Size=Size,IsEraser=IsEraser,Clip=Clip,ClipPolygon=ClipPolygon==null?null:[..ClipPolygon]};
}
