using System.Windows;
using System.Windows.Media;
namespace Prism.Controls;
public class Glyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(Glyph), new FrameworkPropertyMetadata("move", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Brush), typeof(Glyph), new FrameworkPropertyMetadata(new SolidColorBrush(System.Windows.Media.Color.FromRgb(191,191,203)), FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty,value); }
    public Brush Color { get => (Brush)GetValue(ColorProperty); set => SetValue(ColorProperty,value); }
    public Glyph() { Width = 19; Height = 19; }
    public static readonly Dictionary<string,string> Paths = new() {
      ["move"]="M12,2 L12,22 M2,12 L22,12 M8,6 L12,2 16,6 M8,18 L12,22 16,18 M6,8 L2,12 6,16 M18,8 L22,12 18,16",
      ["select"]="M8,3 L4,3 4,7 M11,3 L14,3 M17,3 L21,3 21,7 M21,10 L21,13 M21,16 L21,21 17,21 M14,21 L11,21 M8,21 L4,21 4,17 M4,14 L4,11",
      ["crop"]="M6,2 L6,18 22,18 M2,6 L18,6 18,22",
      ["brush"]="M9,14 L18,3 Q22,0 22,5 L13,17 Z M10,15 Q3,14 4,20 L2,22 Q11,23 12,17",
      ["eraser"]="M3,14 L13,3 Q15,1 17,3 L22,8 11,21 8,21 Z M9,8 L17,15 M11,21 L23,21",
      ["text"]="M3,6 L3,3 21,3 21,6 M12,3 L12,21 M8,21 L16,21",
      ["shape"]="M5,3 L19,3 Q21,3 21,5 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,5 Q3,3 5,3",
      ["ellipse"]="M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12",
      ["hand"]="M7,12 L7,5 Q7,2 10,4 L10,11 10,3 Q12,0 14,3 L14,11 14,5 Q17,2 18,5 L18,12 18,8 Q21,6 22,9 L22,17 Q21,23 14,23 L10,23 Q7,23 5,19 L1,12 Q1,9 4,10 L7,13",
      ["zoom"]="M16,16 L22,22 M18,10 A8,8 0 1 1 2,10 A8,8 0 1 1 18,10 M6,10 L14,10 M10,6 L10,14",
      ["dropper"]="M14,3 L21,10 M17,2 L22,7 M16,5 L5,16 3,21 8,19 19,8 M7,14 L11,18",
      ["undo"]="M8,4 L3,9 8,14 M3,9 L15,9 Q22,9 21,16 Q20,21 13,21",
      ["redo"]="M16,4 L21,9 16,14 M21,9 L9,9 Q2,9 3,16 Q4,21 11,21",
      ["export"]="M12,16 L12,2 M7,7 L12,2 17,7 M4,14 L4,22 20,22 20,14",
      ["plus"]="M12,4 L12,20 M4,12 L20,12",
      ["minus"]="M4,12 L20,12",
      ["close"]="M6,6 L18,18 M6,18 L18,6",
      ["chevron"]="M7,9 L12,14 17,9",
      ["chevron-right"]="M9,6 L15,12 9,18",
      ["expand"]="M3,9 L3,3 9,3 M15,3 L21,3 21,9 M21,15 L21,21 15,21 M9,21 L3,21 3,15",
      ["image"]="M4,3 L20,3 Q22,3 22,5 L22,19 Q22,21 20,21 L4,21 Q2,21 2,19 L2,5 Q2,3 4,3 M2,17 L8,11 13,16 17,12 22,17 M17,7 A1,1 0 1 1 15,7 A1,1 0 1 1 17,7",
      ["eye"]="M1,12 Q12,-2 23,12 Q12,26 1,12 M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12",
      ["eye-off"]="M1,12 Q12,-2 23,12 Q12,26 1,12 M3,3 L21,21",
      ["lock"]="M7,10 L7,6 Q7,1 12,1 Q17,1 17,6 L17,10 M4,10 L20,10 20,22 4,22 Z M12,15 L12,18",
      ["unlock"]="M7,10 L7,6 Q7,1 12,1 Q17,1 17,6 M4,10 L20,10 20,22 4,22 Z M12,15 L12,18",
      ["layers"]="M2,7 L12,2 22,7 12,12 Z M2,12 L12,17 22,12 M2,17 L12,22 22,17",
      ["duplicate"]="M9,8 L22,8 22,22 9,22 Z M5,17 L2,17 2,2 17,2 17,5",
      ["trash"]="M3,6 L21,6 M9,6 L9,2 15,2 15,6 M5,6 L6,22 18,22 19,6 M10,10 L10,18 M14,10 L14,18",
      ["folder"]="M2,6 L2,3 9,3 12,6 22,6 22,21 2,21 Z",
      ["history"]="M3,10 Q5,1 14,3 Q23,5 22,14 Q21,23 11,22 Q5,22 3,17 M3,3 L3,10 10,10 M13,7 L13,13 17,15",
      ["settings"]="M5,3 L5,21 M12,3 L12,21 M19,3 L19,21 M2,8 L8,8 M9,16 L15,16 M16,10 L22,10",
      ["align-left"]="M3,2 L3,22 M7,5 L21,5 M7,12 L16,12 M7,19 L21,19",
      ["align-center"]="M12,2 L12,22 M4,5 L20,5 M7,12 L17,12 M4,19 L20,19",
      ["align-right"]="M21,2 L21,22 M3,5 L17,5 M8,12 L17,12 M3,19 L17,19",
      ["rotate"]="M20,8 Q15,-1 7,3 Q0,6 3,15 Q6,24 15,21 Q20,19 21,15 M20,2 L20,8 14,8",
      ["check"]="M4,12 L9,17 20,6",
      ["help"]="M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M8,8 Q9,4 13,6 Q18,8 12,12 L12,14 M12,17 L12,18",
      ["save"]="M3,2 L18,2 22,6 22,22 2,22 2,2 Z M7,2 L7,9 17,9 17,2 M7,22 L7,14 17,14 17,22",
      ["more"]="M4,12 L5,12 M11,12 L12,12 M18,12 L19,12",
      ["sun"]="M17,12 A5,5 0 1 1 7,12 A5,5 0 1 1 17,12 M12,1 L12,4 M12,20 L12,23 M1,12 L4,12 M20,12 L23,12 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4",
      ["contrast"]="M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M12,2 L12,22 M15,4 L15,20 M18,6 L18,18 M21,10 L21,14",
      ["link"]="M9,15 L15,9 M8,16 L6,18 Q1,22 1,16 Q1,14 4,11 L8,7 Q12,3 15,7 M16,8 L18,6 Q23,2 23,8 Q23,10 20,13 L16,17 Q12,21 9,17",
      ["prism"]="M12,2 L23,21 1,21 Z M12,2 L12,21 M1,21 L17.5,11.5 M23,21 L6.5,11.5"
    };
    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);
        if (!Paths.TryGetValue(Kind,out var data)) data=Paths["shape"];
        dc.PushTransform(new ScaleTransform(ActualWidth/24,ActualHeight/24));
        dc.DrawGeometry(null,new Pen(Color,1.55){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},Geometry.Parse(data));
        dc.Pop();
    }
}
