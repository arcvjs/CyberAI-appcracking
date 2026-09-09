using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace Prism.Controls;
public class Ruler : FrameworkElement
{
    public bool Vertical { get; set; }
    public double Origin { get; set; }
    public double Scale { get; set; }=1;
    protected override void OnRender(DrawingContext dc)
    {
        if(!double.IsFinite(Origin+Scale)||Scale<=0)return;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        var major=new Pen(new SolidColorBrush(Color.FromRgb(103,101,112)),1);
        var minor=new Pen(new SolidColorBrush(Color.FromRgb(70,69,79)),1);
        double length=Vertical?ActualHeight:ActualWidth;
        double unit=Scale<.2?500:Scale<.5?200:Scale>1.5?50:100;
        double step=unit*Scale;
        for(int i=(int)Math.Floor(-Origin/step);Origin+i*step<length;i++) {
            double p=Math.Round(Origin+i*step)+.5;
            var label=new FormattedText((i*unit).ToString("0"),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),9,new SolidColorBrush(Color.FromRgb(141,138,151)),VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if(Vertical){dc.DrawLine(major,new Point(15,p),new Point(21,p));dc.PushTransform(new TranslateTransform(3,p-4));dc.PushTransform(new RotateTransform(-90));dc.DrawText(label,new Point());dc.Pop();dc.Pop();}
            else {dc.DrawLine(major,new Point(p,15),new Point(p,21));dc.DrawText(label,new Point(p+4,1));}
            for(int j=1;j<5;j++){double q=Math.Round(p+step*j/5)+.5;if(Vertical)dc.DrawLine(minor,new Point(j==2?16:18,q),new Point(21,q));else dc.DrawLine(minor,new Point(q,j==2?16:18),new Point(q,21));}
        }
        dc.Pop();
    }
}
