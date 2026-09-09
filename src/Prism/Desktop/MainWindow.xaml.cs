using Microsoft.Win32;
using Prism.Controls;
using Prism.Models;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Prism;
public partial class MainWindow : Window
{
    private EditorDocument _document = null!;
    private Layer? _selected;
    private string _tool = "move", _foreground = "#B8A4FF";
    private string? _filePath;
    private bool _ready, _syncing, _dragging, _panning, _space, _dirty;
    private Point _start, _panStart;
    private double _scrollX, _scrollY;
    private Rect _originalBounds;
    private string _resize = "";
    private PaintStroke? _stroke;
    private readonly EditHistory _edits = new();
    private List<EditHistory.Entry> _history => _edits.Entries;
    private int _historyIndex => _edits.Index;
    private bool _fitMode=true;
    private readonly Dictionary<string,string> _toolNames = new() { ["move"]="Move",["select"]="Marquee",["crop"]="Crop",["brush"]="Brush",["eraser"]="Eraser",["dropper"]="Eyedropper",["text"]="Text",["shape"]="Rectangle",["hand"]="Hand",["zoom"]="Zoom" };
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(HorizontalWheel);
        Width=Math.Min(1500,SystemParameters.WorkArea.Width-48);
        Height=Math.Min(970,SystemParameters.WorkArea.Height-48);
        _document=EditorDocument.Sample();_selected=_document.Layers[0];Surface.Document=_document;Surface.SelectedLayer=_selected;
        _edits.Reset(_document,"Opened sample document");
        InitializeLicensing();
        _ready=true;
        Loaded+=(_,_)=> { UpdateAll();SelectTool("move");FitCanvas(); };
        PreviewKeyUp+=(_,e)=> { if(e.Key==Key.Space) { _space=false;Surface.Cursor=CursorForTool(); } };
        Deactivated+=(_,_)=> { _space=false;_panning=false; };
        Surface.MouseLeave+=(_,_)=>{Surface.BrushPosition=null;Surface.Refresh();};
        CanvasScroll.ScrollChanged+=(_,_)=>{if(_ready){DrawRulers();UpdateNavigatorViewport();}};
        BrushSize.ValueChanged+=(_,_)=>{if(_ready){BrushSizeLabel.Text=$"{BrushSize.Value:0} px";Surface.BrushDiameter=BrushSize.Value;Surface.Refresh();}};
    }
    private static Brush B(string hex)=>Layer.Brush(hex);
    private void Status(string text) => StatusText.Text=text;
    private void Commit(string label)
    {
        if(_edits.Matches(_document)) { UpdateAll();return; }
        _edits.Push(_document,label);_dirty=_edits.IsDirty;UpdateAll();Status(label);
    }
    private void UpdateAll()
    {
        if(!_ready)return;
        Surface.Document=_document;Surface.SelectedLayer=_selected;Surface.Refresh();
        Title="Prism — "+_document.Name+(_dirty?" •":"");TitleDocument.Text=_document.Name;TabName.Text=_document.Name+(_dirty?"  •":"");
        SaveStatus.Text=_dirty?"Unsaved changes":_filePath==null?"Sample document":"Saved locally";
        CanvasSizeLabel.Text=$"{_document.Width:0} × {_document.Height:0} px";
        LayerCount.Text=$"{_document.Layers.Count} layers";
        UndoButton.IsEnabled=_historyIndex>0;RedoButton.IsEnabled=_historyIndex<_history.Count-1;
        UpdateProperties();UpdateLayers();UpdateHistory();UpdateContext();DrawRulers();
        NavigatorImage.Source=Surface.RenderBitmap(scale:Math.Min(1,300/_document.Width));
        Dispatcher.BeginInvoke(UpdateNavigatorViewport);
    }
    private void UpdateProperties()
    {
        _syncing=true;
        PropertiesPanel.IsEnabled=_selected is {Locked:false}&&_licensing.Status.CanEdit;
        EmptyInspector.Visibility=_selected==null&&AdjustmentsPanel.Visibility!=Visibility.Visible?Visibility.Visible:Visibility.Collapsed;
        PropertiesPanel.Visibility=_selected==null||AdjustmentsPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
        if(_selected is {} layer) {
            XField.Text=layer.X.ToString("0.#");YField.Text=layer.Y.ToString("0.#");WidthField.Text=layer.Width.ToString("0.#");HeightField.Text=layer.Height.ToString("0.#");RotationField.Text=layer.Rotation.ToString("0.#");
            PropertyKind.Text=layer.KindLabel+(layer.Locked?" · Locked":"");PropertyIcon.Kind=LayerIcon(layer);
            CharacterPanel.Visibility=layer.Kind=="text"?Visibility.Visible:Visibility.Collapsed;
            GeneralColorButton.Visibility=layer.Kind is "shape" or "gradient"?Visibility.Visible:Visibility.Collapsed;
            FontSizeField.Text=layer.FontSize.ToString("0.#");WeightSelect.SelectedIndex=layer.Bold?1:0;
            foreach(ComboBoxItem item in FontSelect.Items)if((string)item.Content==layer.FontFamily)FontSelect.SelectedItem=item;
            LayerColorText.Text=layer.Color.TrimStart('#');LayerColorSwatch.Background=B(layer.Color);OpacityField.Text=$"{layer.Opacity*100:0}%";
        }
        _syncing=false;
    }
    private string LayerIcon(Layer l)=>l.Kind switch {"text"=>"text","image"=>"image","paint"=>"brush","gradient"=>"contrast",_=>"shape"};
    private void UpdateLayers()
    {
        LayersList.Children.Clear();
        foreach(var layer in _document.Layers) {
            var selected=layer==_selected;
            var row=new Border { Background=B(selected?"#40374F":"#242529"),BorderBrush=B(selected?"#675382":"#303137"),BorderThickness=new Thickness(selected?2:0,0,0,1),Padding=new Thickness(6,6,9,6),AllowDrop=true,ToolTip=layer.Name+" · "+layer.KindLabel };
            var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(37)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(22)});
            var eye=new Button {Style=(Style)FindResource("IconButton"),Width=25,Height=29,Padding=new Thickness(5),ToolTip=layer.Visible?"Hide layer":"Show layer",Content=new Glyph{Kind=layer.Visible?"eye":"eye-off",Width=14,Height=14,Color=B(layer.Visible?"#C2BACF":"#67616F")}};
            eye.Click+=(_,e)=>{e.Handled=true;if(!RequireLicense())return;layer.Visible=!layer.Visible;Commit(layer.Visible?"Show "+layer.Name:"Hide "+layer.Name);};grid.Children.Add(eye);
            var thumb=new Border{Width=29,Height=29,Background=B("#302D36"),BorderBrush=B(selected?"#AF97DF":"#51505A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3)};
            if(layer.Kind=="image")thumb.Background=new ImageBrush(layer.GetImage()){Stretch=Stretch.UniformToFill};
            else if(layer.Kind=="shape")thumb.Background=B(layer.Color);
            else thumb.Child=new Glyph{Kind=LayerIcon(layer),Width=15,Height=15,Color=B(selected?"#DDD0F9":"#B7B1C2"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumn(thumb,1);grid.Children.Add(thumb);
            var name=new TextBlock{Text=layer.Name,FontSize=12,Margin=new Thickness(8,0,0,0),Foreground=B(selected?"#F0E8FF":"#C9C6D1"),TextTrimming=TextTrimming.CharacterEllipsis};Grid.SetColumn(name,2);grid.Children.Add(name);
            if(layer.Locked){var icon=new Glyph{Kind="lock",Width=12,Height=12,Color=B("#8C8698"),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(icon,3);grid.Children.Add(icon);}
            row.Child=grid;row.Tag=layer.Id;
            Point dragStart=default;
            row.MouseLeftButtonDown+=(_,e)=> {dragStart=e.GetPosition(row);if(e.ClickCount==2){_selected=layer;RenameLayer();}else{_selected=layer;Surface.SelectedLayer=layer;Surface.Refresh();UpdateProperties();UpdateContext();HighlightLayerRows();}e.Handled=true;};
            row.MouseRightButtonUp+=(_,e)=>{_selected=layer;HighlightLayerRows();UpdateProperties();ShowLayerMenu(row);e.Handled=true;};
            row.MouseMove+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&(e.GetPosition(row)-dragStart).Length>7)DragDrop.DoDragDrop(row,new DataObject("PrismLayer",layer.Id),DragDropEffects.Move);};
            row.DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent("PrismLayer")?DragDropEffects.Move:DragDropEffects.None;row.BorderThickness=new Thickness(0,e.GetPosition(row).Y<row.ActualHeight/2?2:0,0,e.GetPosition(row).Y>=row.ActualHeight/2?2:0);row.BorderBrush=B("#B8A4FF");e.Handled=true;};
            row.DragLeave+=(_,_)=>HighlightLayerRows();
            row.Drop+=(_,e)=>{e.Handled=true;if(!RequireLicense())return;if(e.Data.GetData("PrismLayer") is string id){var source=_document.Layers.FirstOrDefault(l=>l.Id==id);if(source!=null&&source!=layer){bool after=e.GetPosition(row).Y>=row.ActualHeight/2;_document.Layers.Remove(source);_document.Layers.Insert(_document.Layers.IndexOf(layer)+(after?1:0),source);Commit("Reorder layer");}}};
            LayersList.Children.Add(row);
        }
    }
    private void HighlightLayerRows()
    {
        foreach(var row in LayersList.Children.OfType<Border>()) {bool active=(string?)row.Tag==_selected?.Id;row.Background=B(active?"#40374F":"#242529");row.BorderBrush=B(active?"#8064A8":"#303137");row.BorderThickness=new Thickness(active?2:0,0,0,1);}
    }
    private void UpdateHistory()
    {
        HistoryList.Children.Clear();
        for(int i=0;i<_history.Count;i++) {
            int index=i;var content=new StackPanel{Orientation=Orientation.Horizontal};content.Children.Add(new Glyph{Kind=i==0?"image":"history",Width=14,Height=14,Margin=new Thickness(0,0,9,0),Color=B(i<=_historyIndex?"#BBA6EB":"#67616F")});content.Children.Add(new TextBlock{Text=_history[i].Label,FontSize=11,Foreground=B(i<=_historyIndex?"#D0C7DF":"#77717F"),TextTrimming=TextTrimming.CharacterEllipsis});
            var button=new Button{Content=content,HorizontalContentAlignment=HorizontalAlignment.Left,Background=B(i==_historyIndex?"#40374F":"#242529"),Padding=new Thickness(10,9,10,9)};button.Click+=(_,_)=>RestoreHistory(index);HistoryList.Children.Add(button);
        }
    }
    private void RestoreHistory(int index)
    {
        if(index<0||index>=_history.Count||!RequireLicense())return;
        string? id=_selected?.Id;string name=_document.Name;_document=_edits.Restore(index);_document.Name=name;_selected=_document.Layers.FirstOrDefault(l=>l.Id==id)??_document.Layers.FirstOrDefault();_dirty=_edits.IsDirty;Surface.Marquee=null;UpdateAll();if(_fitMode)FitCanvas();Status(_history[index].Label);
    }
    private void UpdateContext()
    {
        ContextBar.Visibility=_selected!=null||Surface.Marquee!=null?Visibility.Visible:Visibility.Collapsed;
        ContextIcon.Kind=_tool=="crop"?"crop":_selected?.Kind=="text"?"text":"settings";
        ContextLabel.Text=_tool=="crop"?"Apply crop":_selected?.Kind=="text"?"Edit text":"Layer properties";
    }
    private void SelectTool(string tool)
    {
        _tool=tool;ActiveToolIcon.Kind=tool;ActiveToolName.Text=_toolNames[tool];
        MoveOptions.Visibility=tool=="move"?Visibility.Visible:Visibility.Collapsed;
        BrushOptions.Visibility=tool is "brush" or "eraser"?Visibility.Visible:Visibility.Collapsed;
        ToolInstruction.Visibility=tool is "move" or "brush" or "eraser"?Visibility.Collapsed:Visibility.Visible;
        ToolInstruction.Text=tool switch {"crop"=>"Drag a crop area · Enter to apply · Esc to cancel","select"=>"Drag to select · Ctrl+D to deselect","text"=>"Click to add text · Double-click text to edit","shape"=>"Drag to draw a rectangle · Hold Shift for a square","dropper"=>"Click the canvas to sample a color","zoom"=>"Click to zoom in · Alt+click to zoom out",_=>"Drag to pan · Ctrl+0 to fit canvas"};
        foreach(var button in ToolRail.Children.OfType<Button>().Where(b=>b.Tag!=null)) {var active=(string)button.Tag==tool;button.Background=B(active?"#4B3D64":"#242529");button.BorderBrush=B(active?"#74608F":"#242529");if(button.Content is Glyph glyph)glyph.Color=B(active?"#D5C2FF":"#BDBBC8");}
        Surface.ShowSelection=tool=="move"&&TransformControls.IsChecked==true;Surface.Cropping=tool=="crop";Surface.BrushPosition=null;Surface.BrushDiameter=tool is "brush" or "eraser"?BrushSize.Value:0;Surface.Cursor=CursorForTool();Surface.Refresh();UpdateContext();
        Status(tool=="move"?"Drag to move · Drag a handle to resize · Shift to constrain":tool is "brush"?"Paint with the foreground color · [ and ] change brush size":tool=="eraser"?"Erase pixels on the selected image or paint layer · Ctrl+Z to undo":ToolInstruction.Text);
    }
    private Cursor CursorForTool()=>_tool switch {"move"=>Cursors.Arrow,"hand"=>Cursors.Hand,"text"=>Cursors.IBeam,_=>Cursors.Cross};
    private void Tool_Click(object sender,RoutedEventArgs e)=>SelectTool((string)((Button)sender).Tag);
    private Point DocPoint(MouseEventArgs e) {var p=e.GetPosition(Surface);return new Point(p.X/Surface.Zoom,p.Y/Surface.Zoom);}
    private Point Clamp(Point p)=>new(Math.Clamp(p.X,0,_document.Width),Math.Clamp(p.Y,0,_document.Height));
    private Layer? HitLayer(Point p)=>_document.Layers.FirstOrDefault(l=>l.Visible&&!l.Locked&&l.Contains(p));
    private void Surface_MouseDown(object sender,MouseButtonEventArgs e)
    {
        if(_tool=="hand"||_space)return;
        Keyboard.ClearFocus();_start=_tool=="move"?DocPoint(e):Clamp(DocPoint(e));_resize="";
        if(_tool=="zoom"){SetZoom(Surface.Zoom*((Keyboard.Modifiers&ModifierKeys.Alt)!=0?1/1.2:1.2));e.Handled=true;return;}
        if(_tool=="dropper"){SampleColor(_start);e.Handled=true;return;}
        if(!RequireLicense()){e.Handled=true;return;}
        if(_tool=="text"){var target=HitLayer(_start);if(target?.Kind=="text")_selected=target;else _selected=null;EditText(_start);e.Handled=true;return;}
        if(_tool=="move") {
            if(_selected is {Locked:false} current){var r=current.Bounds;var tol=8/Surface.Zoom;var p=current.Unrotate(_start);var padded=r;padded.Inflate(tol,tol);if(padded.Contains(p)){if(Math.Abs(p.X-r.Right)<tol)_resize+="r";else if(Math.Abs(p.X-r.Left)<tol)_resize+="l";if(Math.Abs(p.Y-r.Bottom)<tol)_resize+="b";else if(Math.Abs(p.Y-r.Top)<tol)_resize+="t";}}
            if(_resize==""&&AutoSelect.IsChecked==true)_selected=HitLayer(_start);
            if(_selected==null||_selected.Locked){UpdateAll();return;}
            if(e.ClickCount==2&&_selected.Kind=="text"){EditText();return;}
            _originalBounds=_selected.Bounds;
        }
        else if(_tool is "brush" or "eraser") {
            if(_tool=="eraser"&&(_selected==null||_selected.Locked||!_selected.Visible||_selected.Kind is not ("paint" or "image"))){Status("Select an unlocked image or paint layer to erase.");return;}
            if(_tool=="brush"&&(_selected?.Kind!="paint"||_selected.Locked||!_selected.Visible)){_selected=NewPaintLayer("Brush strokes");_document.Layers.Insert(0,_selected);}
            if(_selected!.ContentWidth<=0){_selected.ContentWidth=_selected.Width;_selected.ContentHeight=_selected.Height;}
            _stroke=new PaintStroke{Color=_foreground,Size=BrushSize.Value*_selected.SourceWidth/_selected.Width,IsEraser=_tool=="eraser",Points=[_selected.ToLocal(_start)]};
            if(Surface.Marquee is Rect selection)_stroke.ClipPolygon=new[]{selection.TopLeft,selection.TopRight,selection.BottomRight,selection.BottomLeft}.Select(_selected.ToLocal).ToList();
            if(_selected.Kind=="paint")_selected.Strokes.Add(_stroke);else _selected.Erasures.Add(_stroke);
        }
        else if(_tool=="shape") {_selected=new Layer{Name="Rectangle",Kind="shape",X=_start.X,Y=_start.Y,Width=1,Height=1,Color=_foreground};_document.Layers.Insert(0,_selected);}
        else if(_tool is "select" or "crop")Surface.Marquee=new Rect(_start,_start);
        _dragging=true;Surface.CaptureMouse();Surface.SelectedLayer=_selected;Surface.Refresh();UpdateProperties();e.Handled=true;
    }
    private void Surface_MouseMove(object sender,MouseEventArgs e)
    {
        if(_tool is "brush" or "eraser"){Surface.BrushPosition=DocPoint(e);Surface.BrushDiameter=BrushSize.Value;Surface.Refresh();}
        if(!_dragging)return;
        var p=_tool=="move"?DocPoint(e):Clamp(DocPoint(e));var delta=p-_start;bool shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
        if(_tool=="move"&&_selected is {Locked:false} layer) {
            if(_resize.Length>0) {
                p=new RotateTransform(-layer.Rotation,_originalBounds.X+_originalBounds.Width/2,_originalBounds.Y+_originalBounds.Height/2).Transform(p);
                double left=_originalBounds.Left,top=_originalBounds.Top,right=_originalBounds.Right,bottom=_originalBounds.Bottom;
                if(_resize.Contains('l'))left=Math.Min(p.X,right-8);if(_resize.Contains('r'))right=Math.Max(p.X,left+8);if(_resize.Contains('t'))top=Math.Min(p.Y,bottom-8);if(_resize.Contains('b'))bottom=Math.Max(p.Y,top+8);
                double width=right-left,height=bottom-top;
                if(shift){height=width*_originalBounds.Height/_originalBounds.Width;if(_resize.Contains('t'))top=bottom-height;else bottom=top+height;}
                var center=new RotateTransform(layer.Rotation,_originalBounds.X+_originalBounds.Width/2,_originalBounds.Y+_originalBounds.Height/2).Transform(new Point((left+right)/2,(top+bottom)/2));
                layer.X=center.X-width/2;layer.Y=center.Y-height/2;layer.Width=width;layer.Height=height;
            }else{if(shift){if(Math.Abs(delta.X)>Math.Abs(delta.Y))delta.Y=0;else delta.X=0;}layer.X=Math.Round(_originalBounds.X+delta.X);layer.Y=Math.Round(_originalBounds.Y+delta.Y);}
            UpdateProperties();
        }
        else if(_tool is "brush" or "eraser"&&_stroke!=null&&_selected!=null) {var local=_selected.ToLocal(p);if((_stroke.Points[^1]-local).Length>.4)_stroke.Points.Add(local);}
        else if(_tool is "shape" or "select" or "crop") {
            if(shift){double side=Math.Min(Math.Abs(delta.X),Math.Abs(delta.Y));p=new Point(_start.X+Math.Sign(delta.X)*side,_start.Y+Math.Sign(delta.Y)*side);}
            var rect=new Rect(_start,p);
            if(_tool=="shape"&&_selected!=null){_selected.X=rect.X;_selected.Y=rect.Y;_selected.Width=Math.Max(1,rect.Width);_selected.Height=Math.Max(1,rect.Height);}
            else Surface.Marquee=rect;
        }
        Surface.Refresh();
    }
    private void Surface_MouseUp(object sender,MouseButtonEventArgs e)
    {
        if(!_dragging)return;_dragging=false;Surface.ReleaseMouseCapture();_stroke=null;
        if(_tool is "select" or "crop"){UpdateContext();Status(_tool=="crop"?"Press Enter to apply this crop, or Esc to cancel.":"Selection active. Paint inside the selected area; Ctrl+D to deselect.");}
        else Commit(_tool switch{"move"=>_resize.Length>0?"Transform layer":"Move layer","brush"=>"Brush stroke","eraser"=>"Eraser stroke",_=>"Draw rectangle"});
        e.Handled=true;
    }
    private Layer NewPaintLayer(string name)=>new(){Name=name,Kind="paint",Width=_document.Width,Height=_document.Height,ContentWidth=_document.Width,ContentHeight=_document.Height};
    private void Workspace_MouseDown(object sender,MouseButtonEventArgs e)
    {
        var position=e.GetPosition(CanvasScroll);
        if(position.X>=CanvasScroll.ViewportWidth||position.Y>=CanvasScroll.ViewportHeight)return;
        if(_tool!="hand"&&!_space)return;_panning=true;_panStart=e.GetPosition(CanvasScroll);_scrollX=CanvasScroll.HorizontalOffset;_scrollY=CanvasScroll.VerticalOffset;CanvasScroll.CaptureMouse();e.Handled=true;
    }
    private void Workspace_MouseMove(object sender,MouseEventArgs e){if(!_panning)return;var delta=e.GetPosition(CanvasScroll)-_panStart;CanvasScroll.ScrollToHorizontalOffset(_scrollX-delta.X);CanvasScroll.ScrollToVerticalOffset(_scrollY-delta.Y);}
    private void Workspace_MouseUp(object sender,MouseButtonEventArgs e){if(!_panning)return;_panning=false;CanvasScroll.ReleaseMouseCapture();e.Handled=true;}
    private void Canvas_MouseWheel(object sender,MouseWheelEventArgs e)
    {
        if((Keyboard.Modifiers&ModifierKeys.Control)!=0)SetZoom(Surface.Zoom*(e.Delta>0?1.1:1/1.1));
        else if((Keyboard.Modifiers&ModifierKeys.Shift)!=0)CanvasScroll.ScrollToHorizontalOffset(CanvasScroll.HorizontalOffset-e.Delta);
        else CanvasScroll.ScrollToVerticalOffset(CanvasScroll.VerticalOffset-e.Delta);
        e.Handled=true;
    }
    private IntPtr HorizontalWheel(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        // WPF does not expose WM_MOUSEHWHEEL as a routed mouse-wheel event.
        if(message==0x020E&&CanvasScroll.IsMouseOver) {
            var delta=unchecked((short)((wParam.ToInt64()>>16)&0xffff));
            CanvasScroll.ScrollToHorizontalOffset(CanvasScroll.HorizontalOffset+delta);
            handled=true;
        }
        return IntPtr.Zero;
    }
    private void SetZoom(double zoom)
    {
        if(!_ready)return;_fitMode=false;
        var anchor=new Point(CanvasScroll.ViewportWidth/2,CanvasScroll.ViewportHeight/2);
        var origin=Surface.TranslatePoint(new Point(),CanvasScroll);
        var docAnchor=new Point((anchor.X-origin.X)/Surface.Zoom,(anchor.Y-origin.Y)/Surface.Zoom);
        Surface.Zoom=Math.Clamp(zoom,.01,4);Surface.Refresh();_syncing=true;ZoomSlider.Value=Surface.Zoom*100;_syncing=false;ZoomLabel.Text=$"{Surface.Zoom*100:0.#}%";TabZoom.Text=ZoomLabel.Text;
        Dispatcher.BeginInvoke(()=>{if(!_fitMode){var now=Surface.TranslatePoint(new Point(docAnchor.X*Surface.Zoom,docAnchor.Y*Surface.Zoom),CanvasScroll);CanvasScroll.ScrollToHorizontalOffset(CanvasScroll.HorizontalOffset+now.X-anchor.X);CanvasScroll.ScrollToVerticalOffset(CanvasScroll.VerticalOffset+now.Y-anchor.Y);}DrawRulers();UpdateNavigatorViewport();});
    }
    private void FitCanvas(){if(!_ready||CanvasArea.ActualWidth<100)return;SetZoom(Math.Min((CanvasArea.ActualWidth-100)/_document.Width,(CanvasArea.ActualHeight-100)/_document.Height));_fitMode=true;CanvasScroll.ScrollToHorizontalOffset(0);CanvasScroll.ScrollToVerticalOffset(0);}
    private void DrawRulers()
    {
        if(!_ready||!double.IsFinite(Surface.Width)||!double.IsFinite(Surface.Height))return;
        var origin=Surface.TranslatePoint(new Point(),CanvasScroll);
        HorizontalRuler.Origin=origin.X+21;HorizontalRuler.Scale=Surface.Zoom;HorizontalRuler.InvalidateVisual();
        VerticalRuler.Origin=origin.Y;VerticalRuler.Scale=Surface.Zoom;VerticalRuler.InvalidateVisual();
    }
    private bool _navigatorDragging;
    private Rect NavigatorBounds()
    {
        double scale=Math.Max(0,Math.Min((NavigatorMap.ActualWidth-4)/_document.Width,(NavigatorMap.ActualHeight-4)/_document.Height));
        return new Rect((NavigatorMap.ActualWidth-_document.Width*scale)/2,(NavigatorMap.ActualHeight-_document.Height*scale)/2,_document.Width*scale,_document.Height*scale);
    }
    private void UpdateNavigatorViewport()
    {
        if(!_ready||NavigatorMap.ActualWidth<=4||NavigatorMap.ActualHeight<=4)return;
        var bounds=NavigatorBounds();var origin=Surface.TranslatePoint(new Point(),CanvasScroll);
        var visible=new Rect(-origin.X/Surface.Zoom,-origin.Y/Surface.Zoom,CanvasScroll.ViewportWidth/Surface.Zoom,CanvasScroll.ViewportHeight/Surface.Zoom);visible.Intersect(new Rect(0,0,_document.Width,_document.Height));
        if(visible.IsEmpty){NavigatorViewport.Visibility=Visibility.Collapsed;return;}NavigatorViewport.Visibility=Visibility.Visible;
        double scale=bounds.Width/_document.Width;Canvas.SetLeft(NavigatorViewport,bounds.X+visible.X*scale);Canvas.SetTop(NavigatorViewport,bounds.Y+visible.Y*scale);NavigatorViewport.Width=visible.Width*scale;NavigatorViewport.Height=visible.Height*scale;
    }
    private void NavigateTo(Point point)
    {
        var bounds=NavigatorBounds();if(bounds.Width<=0)return;
        var target=Surface.TranslatePoint(new Point(Math.Clamp((point.X-bounds.X)/bounds.Width,0,1)*Surface.Width,Math.Clamp((point.Y-bounds.Y)/bounds.Height,0,1)*Surface.Height),CanvasScroll);
        CanvasScroll.ScrollToHorizontalOffset(CanvasScroll.HorizontalOffset+target.X-CanvasScroll.ViewportWidth/2);CanvasScroll.ScrollToVerticalOffset(CanvasScroll.VerticalOffset+target.Y-CanvasScroll.ViewportHeight/2);
    }
    private void Navigator_MouseDown(object sender,MouseButtonEventArgs e){_navigatorDragging=true;NavigatorMap.CaptureMouse();NavigateTo(e.GetPosition(NavigatorMap));}
    private void Navigator_MouseMove(object sender,MouseEventArgs e){if(_navigatorDragging)NavigateTo(e.GetPosition(NavigatorMap));}
    private void Navigator_MouseUp(object sender,MouseButtonEventArgs e){_navigatorDragging=false;NavigatorMap.ReleaseMouseCapture();}
    private void Navigator_SizeChanged(object sender,SizeChangedEventArgs e){if(_ready)UpdateNavigatorViewport();}
    private void CanvasArea_SizeChanged(object sender,SizeChangedEventArgs e){if(_ready){if(_fitMode)FitCanvas();DrawRulers();}}
    private void Zoom_Changed(object sender,RoutedPropertyChangedEventArgs<double> e){if(_ready&&!_syncing)SetZoom(e.NewValue/100);}
    private void Fit_Click(object sender,RoutedEventArgs e)=>FitCanvas();
    private void ZoomIn_Click(object sender,RoutedEventArgs e)=>SetZoom(Surface.Zoom*1.2);
    private void ZoomOut_Click(object sender,RoutedEventArgs e)=>SetZoom(Surface.Zoom/1.2);
    private void Transform_Toggled(object sender,RoutedEventArgs e){if(!_ready)return;Surface.ShowSelection=TransformControls.IsChecked==true&&_tool=="move";Surface.Refresh();}
    private static bool Number(TextBox field,out double number)=>double.TryParse(field.Text.Trim().TrimEnd('%'),NumberStyles.Float,CultureInfo.CurrentCulture,out number)&&double.IsFinite(number);
    private bool Editable()=>_selected is {Locked:false}&&RequireLicense();
    private void TransformField_Changed(object sender,RoutedEventArgs e)
    {
        if(!_ready||_syncing||!Editable())return;
        if(!Number(XField,out var x)||!Number(YField,out var y)||!Number(WidthField,out var w)||!Number(HeightField,out var h)||!Number(RotationField,out var r)||w<1||h<1||w>20000||h>20000){UpdateProperties();return;}
        _selected!.X=x;_selected.Y=y;_selected.Width=w;_selected.Height=h;_selected.Rotation=r%360;Commit("Transform layer");
    }
    private void PropertyField_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){Keyboard.ClearFocus();e.Handled=true;}else if(e.Key==Key.Escape){UpdateProperties();Keyboard.ClearFocus();e.Handled=true;}}
    private void Font_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!_ready||_syncing||!Editable()||_selected?.Kind!="text")return;
        _selected.FontFamily=(FontSelect.SelectedItem as ComboBoxItem)?.Content.ToString()??"Arial";_selected.Bold=WeightSelect.SelectedIndex==1;Commit("Change typography");
    }
    private void FontSize_Changed(object sender,RoutedEventArgs e)
    {
        if(!_ready||_syncing||!Editable()||_selected?.Kind!="text")return;
        if(Number(FontSizeField,out var size)&&size>=4&&size<=1000){double ratio=size/_selected.FontSize;_selected.Width*=ratio;_selected.Height*=ratio;_selected.FontSize=size;Commit("Change font size");}else UpdateProperties();
    }
    private void Opacity_Changed(object sender,RoutedEventArgs e){if(!_ready||_syncing||!Editable())return;if(Number(OpacityField,out var opacity)&&opacity>=0&&opacity<=100){_selected!.Opacity=opacity/100;Commit("Change layer opacity");}else UpdateProperties();}
    private void Align_Click(object sender,RoutedEventArgs e){if(!Editable())return;_selected!.X=(string)((Button)sender).Tag switch{"left"=>0,"center"=>(_document.Width-_selected.Width)/2,_=>_document.Width-_selected.Width};Commit("Align layer");}
    private void Undo_Click(object sender,RoutedEventArgs e)=>RestoreHistory(_historyIndex-1);
    private void Redo_Click(object sender,RoutedEventArgs e)=>RestoreHistory(_historyIndex+1);
    private void Properties_Click(object sender,RoutedEventArgs e){AdjustmentsPanel.Visibility=Visibility.Collapsed;UpdateProperties();PropertiesTab.Foreground=B("#B8A4FF");AdjustmentsTab.Foreground=B("#9B9CA8");}
    private void Adjustments_Click(object sender,RoutedEventArgs e){EmptyInspector.Visibility=Visibility.Collapsed;PropertiesPanel.Visibility=Visibility.Collapsed;AdjustmentsPanel.Visibility=Visibility.Visible;PropertiesTab.Foreground=B("#9B9CA8");AdjustmentsTab.Foreground=B("#B8A4FF");}
    private void Layers_Click(object sender,RoutedEventArgs e){LayersScroller.Visibility=Visibility.Visible;HistoryScroller.Visibility=Visibility.Collapsed;LayerOptions.Visibility=Visibility.Visible;LayersTab.Foreground=B("#B8A4FF");HistoryTab.Foreground=B("#9B9CA8");}
    private void History_Click(object sender,RoutedEventArgs e){LayersScroller.Visibility=Visibility.Collapsed;HistoryScroller.Visibility=Visibility.Visible;LayerOptions.Visibility=Visibility.Collapsed;LayersTab.Foreground=B("#9B9CA8");HistoryTab.Foreground=B("#B8A4FF");}
    private void Lock_Click(object sender,RoutedEventArgs e){if(_selected==null||!RequireLicense())return;_selected.Locked=!_selected.Locked;Commit(_selected.Locked?"Lock layer":"Unlock layer");}
    private void Duplicate_Click(object sender,RoutedEventArgs e)
    {
        if(_selected==null||!RequireLicense())return;var copy=_selected.Clone();copy.Id=Guid.NewGuid().ToString("N");copy.Name+=" copy";copy.X+=20;copy.Y+=20;copy.Locked=false;_document.Layers.Insert(_document.Layers.IndexOf(_selected),copy);_selected=copy;Commit("Duplicate layer");
    }
    private void AddLayer_Click(object sender,RoutedEventArgs e){if(!RequireLicense())return;_selected=NewPaintLayer("Layer "+(_document.Layers.Count+1));_document.Layers.Insert(0,_selected);Commit("New paint layer");}
    private void DeleteLayer_Click(object sender,RoutedEventArgs e){if(!Editable())return;int index=_document.Layers.IndexOf(_selected!);_document.Layers.Remove(_selected!);_selected=_document.Layers.ElementAtOrDefault(Math.Min(index,_document.Layers.Count-1));Commit("Delete layer");}
    private void ReorderLayer(int offset){if(_selected==null||!RequireLicense())return;int old=_document.Layers.IndexOf(_selected),target=Math.Clamp(old+offset,0,_document.Layers.Count-1);_document.Layers.RemoveAt(old);_document.Layers.Insert(target,_selected);Commit("Reorder layer");}
    private void LayerMenu_Click(object sender,RoutedEventArgs e)=>ShowLayerMenu((FrameworkElement)sender);
    private void ShowLayerMenu(FrameworkElement owner)=>ShowMenu(owner,[("Rename layer", "F2",()=>RenameLayer()),("Duplicate layer","Ctrl+J",()=>Duplicate_Click(this,new())),("Bring forward","Ctrl+]",()=>ReorderLayer(-1)),("Send backward","Ctrl+[",()=>ReorderLayer(1)),(_selected?.Locked==true?"Unlock layer":"Lock layer","",()=>Lock_Click(this,new())),("Delete layer","Delete",()=>DeleteLayer_Click(this,new()))]);
    private void RenameLayer(){if(_selected==null||!RequireLicense())return;string? result=TextDialog("Rename layer","Layer name",_selected.Name,false);if(result!=null){_selected.Name=result;Commit("Rename layer");}}
    private void EditLayer_Click(object sender,RoutedEventArgs e){if(_tool=="crop")ApplyCrop();else if(_selected?.Kind=="text")EditText();else Properties_Click(this,new());}
    private void EditText(Point? position=null)
    {
        if(!RequireLicense())return;
        if(_selected?.Locked==true){Status("Unlock this layer to edit it.");return;}
        bool existing=_selected?.Kind=="text";string? text=TextDialog(existing?"Edit text":"Add text","Text content",existing?_selected!.Text:"Your next great idea.",true);
        if(text==null)return;
        if(!existing){_selected=new Layer{Kind="text",Name="Text",X=position?.X??100,Y=position?.Y??100,FontSize=80,Color=_foreground};_document.Layers.Insert(0,_selected);}
        _selected!.Text=text;_selected.SizeToText();if(!existing)_selected.Name=text.Replace('\n',' ').Substring(0,Math.Min(24,text.Length));
        Commit(existing?"Edit text":"Add text layer");SelectTool("move");
    }
    private void Color_Click(object sender,RoutedEventArgs e){var color=ColorDialog(_foreground);if(color!=null){_foreground=color;ForegroundSwatch.Background=B(color);Status("Foreground color: "+color);}}
    private void LayerColor_Click(object sender,RoutedEventArgs e){if(!Editable())return;var color=ColorDialog(_selected!.Color);if(color!=null){_selected.Color=color;Commit("Change layer color");}}
    private void SampleColor(Point point)
    {
        var bitmap=new FormatConvertedBitmap(Surface.RenderBitmap(),PixelFormats.Bgra32,null,0);var pixels=new byte[4];bitmap.CopyPixels(new Int32Rect(Math.Clamp((int)point.X,0,bitmap.PixelWidth-1),Math.Clamp((int)point.Y,0,bitmap.PixelHeight-1),1,1),pixels,4,0);
        _foreground=$"#{pixels[2]:X2}{pixels[1]:X2}{pixels[0]:X2}";ForegroundSwatch.Background=B(_foreground);Status("Sampled "+_foreground);
    }
    private void ApplyCrop()
    {
        if(!RequireLicense())return;
        if(Surface.Marquee is not Rect rect||rect.Width<10||rect.Height<10){Status("Draw a crop area first.");return;}
        rect.Intersect(new Rect(0,0,_document.Width,_document.Height));
        foreach(var layer in _document.Layers){layer.X-=rect.X;layer.Y-=rect.Y;}_document.Width=Math.Round(rect.Width);_document.Height=Math.Round(rect.Height);Surface.Marquee=null;Commit("Crop canvas");SelectTool("move");FitCanvas();
    }
    private void Adjustment_Click(object sender,RoutedEventArgs e)
    {
        if(!Editable()||_selected!.Kind!="image"){Status("Select an unlocked image layer to apply an adjustment.");return;}
        string adjustment=(string)((Button)sender).Tag;var image=new FormatConvertedBitmap(_selected.GetImage(),PixelFormats.Bgra32,null,0);int stride=image.PixelWidth*4;byte[] pixels=new byte[stride*image.PixelHeight];image.CopyPixels(pixels,stride,0);
        for(int i=0;i<pixels.Length;i+=4){if(adjustment=="grayscale"){byte gray=(byte)(pixels[i]*.114+pixels[i+1]*.587+pixels[i+2]*.299);pixels[i]=pixels[i+1]=pixels[i+2]=gray;}else for(int c=0;c<3;c++)pixels[i+c]=(byte)Math.Clamp(adjustment switch{"brighten"=>pixels[i+c]+15,"darken"=>pixels[i+c]-15,_=>(pixels[i+c]-128)*1.15+128},0,255);}
        var modified=BitmapSource.Create(image.PixelWidth,image.PixelHeight,96,96,PixelFormats.Bgra32,null,pixels,stride);modified.Freeze();_selected.CachedImage=modified;_selected.ImageData=EncodePng(modified);Commit(adjustment switch{"grayscale"=>"Black & white","contrast"=>"Increase contrast",_=>"Adjust brightness"});
    }
    private static string EncodePng(BitmapSource bitmap){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return Convert.ToBase64String(stream.ToArray());}
    private void Menu_Click(object sender,RoutedEventArgs e)
    {
        var button=(Button)sender;var items=new List<(string,string,Action)>();
        switch((string)button.Tag){
            case "file":items=[("New document…","Ctrl+N",()=>New_Click(this,new())),("Open…","Ctrl+O",()=>Open_Click(this,new())),("Place image…","Ctrl+Shift+O",()=>PlaceImage()),("Save project","Ctrl+S",()=>SaveProject()),("Save project as…","Ctrl+Shift+S",()=>SaveProject(true)),("Export image…","Ctrl+Shift+E",()=>Export_Click(this,new()))];break;
            case "edit":items=[("Undo","Ctrl+Z",()=>RestoreHistory(_historyIndex-1)),("Redo","Ctrl+Shift+Z",()=>RestoreHistory(_historyIndex+1)),("Edit layer…","",()=>EditLayer_Click(this,new()))];break;
            case "image":items=[("Crop canvas","C",()=>SelectTool("crop")),("Image adjustments","",()=>Adjustments_Click(this,new())),("Fit on screen","Ctrl+0",()=>FitCanvas())];break;
            case "layer":ShowLayerMenu(button);return;
            case "select":items=[("Select all","Ctrl+A",()=>{Surface.Marquee=new Rect(0,0,_document.Width,_document.Height);Surface.Refresh();}), ("Deselect","Ctrl+D",()=>{Surface.Marquee=null;Surface.Refresh();})];break;
            case "view":items=[("Zoom in","Ctrl++",()=>SetZoom(Surface.Zoom*1.2)),("Zoom out","Ctrl+−",()=>SetZoom(Surface.Zoom/1.2)),("Fit on screen","Ctrl+0",()=>FitCanvas()),("Actual size","Ctrl+1",()=>SetZoom(1)),("History","",()=>History_Click(this,new())),("Restore sample document","",()=>LoadSample())];break;
        }
        ShowMenu(button,items);
    }
    private void ShowMenu(FrameworkElement owner,IEnumerable<(string Label,string Shortcut,Action Action)> items)
    {
        var menu=new ContextMenu{PlacementTarget=owner,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
        foreach(var item in items){var entry=new MenuItem{Header=item.Label,InputGestureText=item.Shortcut};entry.Click+=(_,_)=>item.Action();menu.Items.Add(entry);}menu.IsOpen=true;
    }
    private bool ConfirmDiscard()
    {
        if(!_dirty)return true;
        var result=MessageBox.Show(this,"Save changes to “"+_document.Name+"” before continuing?","Prism",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
        return result==MessageBoxResult.No||(result==MessageBoxResult.Yes&&SaveProject());
    }
    private void SetDocument(EditorDocument doc,string? path=null)
    {
        _document=doc;_filePath=path;_selected=doc.Layers.FirstOrDefault(l=>!l.Locked)??doc.Layers.FirstOrDefault();_dirty=false;_edits.Reset(doc);Surface.Marquee=null;UpdateAll();SelectTool("move");FitCanvas();
    }
    private void LoadSample(){if(ConfirmDiscard())SetDocument(EditorDocument.Sample());}
    private void New_Click(object sender,RoutedEventArgs e)
    {
        if(!RequireLicense())return;
        var dialog=MakeDialog("New document",440);var stack=new StackPanel();AddDialogHeading(stack,"A fresh canvas","Set the size for your next idea.");
        var name=Field(stack,"Name","Untitled");
        var presets=new ComboBox{Margin=new Thickness(0,12,0,12)};foreach(var p in new[]{"Custom","Landscape · 1500 × 1050","Square · 1080 × 1080","Portrait · 1080 × 1350","Full HD · 1920 × 1080"})presets.Items.Add(new ComboBoxItem{Content=p});presets.SelectedIndex=1;stack.Children.Add(presets);
        var dimensions=new Grid();dimensions.ColumnDefinitions.Add(new ColumnDefinition());dimensions.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(16)});dimensions.ColumnDefinitions.Add(new ColumnDefinition());var left=new StackPanel();var right=new StackPanel();Grid.SetColumn(right,2);dimensions.Children.Add(left);dimensions.Children.Add(right);var width=Field(left,"Width (px)","1500");var height=Field(right,"Height (px)","1050");stack.Children.Add(dimensions);
        presets.SelectionChanged+=(_,_)=>{(int w,int h)=presets.SelectedIndex switch{2=>(1080,1080),3=>(1080,1350),4=>(1920,1080),_=>(1500,1050)};width.Text=w.ToString();height.Text=h.ToString();};
        var transparent=new CheckBox{Content="Transparent background",IsChecked=false,Margin=new Thickness(0,18,0,0)};stack.Children.Add(transparent);var error=new TextBlock{Foreground=B("#E7AAA9"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};stack.Children.Add(error);
        AddDialogButtons(stack,dialog,"Create document",()=>{if(!Number(width,out var w)||!Number(height,out var h)||w<32||h<32||w>8000||h>8000||w*h>24_000_000){error.Text="Use dimensions from 32–8000 px, up to 24 megapixels.";return;}if(!ConfirmDiscard())return;var doc=new EditorDocument{Name=string.IsNullOrWhiteSpace(name.Text)?"Untitled":name.Text.Trim(),Width=Math.Round(w),Height=Math.Round(h)};if(transparent.IsChecked!=true)doc.Layers.Add(new Layer{Name="Background",Kind="shape",Color="#FFFFFF",Width=doc.Width,Height=doc.Height,Locked=true});SetDocument(doc);SaveStatus.Text="New document";dialog.DialogResult=true;});dialog.Content=stack;dialog.ShowDialog();
    }
    private void Open_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="Open in Prism",Filter="Images and Prism projects|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tif;*.tiff;*.prism|Prism project|*.prism|All files|*.*"};if(dialog.ShowDialog(this)==true&&ConfirmDiscard())OpenPath(dialog.FileName);
    }
    private void OpenPath(string path)
    {
        try {
            if(System.IO.Path.GetExtension(path).Equals(".prism",StringComparison.OrdinalIgnoreCase)) {
                var doc=EditorDocument.Deserialize(File.ReadAllText(path));if(!double.IsFinite(doc.Width)||!double.IsFinite(doc.Height)||doc.Width<1||doc.Height<1||doc.Width*doc.Height>24_000_000||doc.Layers.Count>300)throw new InvalidDataException("This document exceeds the prototype's 24-megapixel or 300-layer limit.");
                foreach(var l in doc.Layers){_ = B(l.Color);if(l.Width<1||l.Height<1||!double.IsFinite(l.Width+l.Height+l.X+l.Y+l.Rotation+l.FontSize)||l.Opacity<0||l.Opacity>1)throw new InvalidDataException("This project contains an invalid layer.");if(l.Kind=="image")_ = l.GetImage();}
                SetDocument(doc,path);
            } else {
                var layer=ImageLayer(path);var image=layer.GetImage()!;SetDocument(new EditorDocument{Name=System.IO.Path.GetFileNameWithoutExtension(path),Width=image.PixelWidth,Height=image.PixelHeight,Layers=[layer]});SaveStatus.Text="Image opened";
            }
            Status("Opened "+System.IO.Path.GetFileName(path));
        }catch(Exception ex){MessageBox.Show(this,"This file could not be opened.\n\n"+ex.Message,"Prism",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    private Layer ImageLayer(string path)
    {
        var layer=new Layer{Name=System.IO.Path.GetFileNameWithoutExtension(path),Kind="image",ImageData=Convert.ToBase64String(File.ReadAllBytes(path))};var image=layer.GetImage()!;
        if((long)image.PixelWidth*image.PixelHeight>24_000_000)throw new InvalidDataException("Please use an image up to 24 megapixels in this prototype.");layer.Width=image.PixelWidth;layer.Height=image.PixelHeight;return layer;
    }
    private void PlaceImage(string? path=null)
    {
        if(!RequireLicense())return;
        if(path==null){var picker=new OpenFileDialog{Title="Place image as a layer",Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff"};if(picker.ShowDialog(this)!=true)return;path=picker.FileName;}
        try{var layer=ImageLayer(path);double scale=Math.Min(1,Math.Min(_document.Width/layer.Width,_document.Height/layer.Height));layer.Width*=scale;layer.Height*=scale;layer.X=(_document.Width-layer.Width)/2;layer.Y=(_document.Height-layer.Height)/2;_document.Layers.Insert(0,layer);_selected=layer;Commit("Place image");SelectTool("move");}catch(Exception ex){MessageBox.Show(this,ex.Message,"Couldn't place image");}
    }
    private bool SaveProject(bool saveAs=false)
    {
        string? path=_filePath;if(saveAs||path==null){var picker=new SaveFileDialog{Title="Save Prism project",FileName=_document.Name+".prism",Filter="Prism project|*.prism",DefaultExt=".prism",AddExtension=true};if(picker.ShowDialog(this)!=true)return false;path=picker.FileName;}
        try{var copy=_document.Clone();copy.Name=System.IO.Path.GetFileNameWithoutExtension(path);var data=copy.Serialize();var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temporary,data);File.Move(temporary,path,true);_document.Name=copy.Name;_filePath=path;_edits.MarkSaved();_dirty=false;UpdateAll();Status("Project saved locally. All layers are editable.");return true;}catch(Exception ex){MessageBox.Show(this,"Could not save this project.\n\n"+ex.Message,"Prism");return false;}
    }
    private void Export_Click(object sender,RoutedEventArgs e)
    {
        var dialog=MakeDialog("Export image",440);var stack=new StackPanel();AddDialogHeading(stack,"Ready for the outside world.","Export your composition as a flattened image.");
        var preview=new Image{Source=Surface.RenderBitmap(),Height=150,Stretch=Stretch.Uniform,Margin=new Thickness(0,16,0,16)};stack.Children.Add(new Border{Child=preview,Background=B("#191A1E"),CornerRadius=new CornerRadius(5)});
        stack.Children.Add(new TextBlock{Text="Format",Foreground=B("#A6A0B1"),FontSize=12,Margin=new Thickness(0,12,0,8)});var format=new ComboBox();format.Items.Add(new ComboBoxItem{Content="PNG · transparency supported"});format.Items.Add(new ComboBoxItem{Content="JPEG · smaller file, white background"});format.SelectedIndex=0;stack.Children.Add(format);
        stack.Children.Add(new TextBlock{Text=$"{_document.Width:0} × {_document.Height:0} px    ·    Original resolution",Foreground=B("#A6A0B1"),FontSize=12,Margin=new Thickness(0,15,0,0)});
        AddDialogButtons(stack,dialog,"Export image",()=>{bool jpg=format.SelectedIndex==1;var picker=new SaveFileDialog{Title="Export image",FileName=_document.Name+(jpg?".jpg":".png"),Filter=jpg?"JPEG image|*.jpg":"PNG image|*.png",AddExtension=true};if(picker.ShowDialog(dialog)!=true)return;try{BitmapEncoder encoder=jpg?new JpegBitmapEncoder{QualityLevel=94}:new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(Surface.RenderBitmap(!jpg)));using(var stream=File.Create(picker.FileName))encoder.Save(stream);Status("Exported "+System.IO.Path.GetFileName(picker.FileName));dialog.DialogResult=true;}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Export failed");}});dialog.Content=stack;dialog.ShowDialog();
    }
    private void Window_Drop(object sender,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop)&&e.Data.GetData(DataFormats.FileDrop) is string[] files&&files.Length>0){if(System.IO.Path.GetExtension(files[0]).Equals(".prism",StringComparison.OrdinalIgnoreCase)){if(ConfirmDiscard())OpenPath(files[0]);}else PlaceImage(files[0]);}e.Handled=true;}
    private Window MakeDialog(string title,double width)=>new PrismDialog{Owner=this,Title="Prism · "+title,Width=Math.Max(420,width),Background=B("#232329"),Foreground=B("#ECE8F4")};
    private void AddDialogHeading(StackPanel panel,string title,string subtitle){panel.Children.Add(new TextBlock{Text=title,FontSize=24,FontWeight=FontWeights.SemiBold,Foreground=B("#F2EFF7"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,9)});panel.Children.Add(new TextBlock{Text=subtitle,FontSize=13,LineHeight=20,Foreground=B("#AFA5BC"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,19)});}
    private TextBox Field(StackPanel parent,string label,string value){parent.Children.Add(new TextBlock{Text=label,FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=B("#CFC5DD"),Margin=new Thickness(0,8,0,8)});var field=new TextBox{Text=value,Style=(Style)FindResource("DialogField")};parent.Children.Add(field);return field;}
    private void AddDialogButtons(StackPanel stack,Window dialog,string label,Action submit){var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=new Button{Content="Cancel",Style=(Style)FindResource("DialogSecondaryButton"),Margin=new Thickness(0,0,10,0),IsCancel=true};cancel.Click+=(_,_)=>dialog.Close();var ok=new Button{Content=label,Style=(Style)FindResource("PrimaryButton"),MinHeight=37};ok.Click+=(_,_)=>submit();row.Children.Add(cancel);row.Children.Add(ok);if(dialog is PrismDialog frame)frame.Footer=row;else{row.Margin=new Thickness(0,24,0,0);stack.Children.Add(row);}}
    private string? TextDialog(string title,string label,string value,bool multiline)
    {
        var dialog=MakeDialog(title,470);var panel=new StackPanel();AddDialogHeading(panel,title,multiline?"Your text stays editable on its own layer.":"Give this layer a descriptive name.");var field=Field(panel,label,value);if(multiline){field.Height=145;field.AcceptsReturn=true;field.TextWrapping=TextWrapping.Wrap;field.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;}string? result=null;AddDialogButtons(panel,dialog,"Apply",()=>{if(!string.IsNullOrWhiteSpace(field.Text)){result=field.Text.Trim();dialog.DialogResult=true;}});dialog.Content=panel;dialog.Loaded+=(_,_)=>{field.Focus();field.SelectAll();};dialog.ShowDialog();return result;
    }
    private string? ColorDialog(string current)
    {
        var dialog=MakeDialog("Choose color",390);var panel=new StackPanel();AddDialogHeading(panel,"Choose a color","Pick a swatch or enter a hex color.");var preview=new Border{Height=62,Background=B(current),CornerRadius=new CornerRadius(6),Margin=new Thickness(0,0,0,12)};panel.Children.Add(preview);var field=Field(panel,"Hex color",current);var swatches=new WrapPanel{Margin=new Thickness(0,15,0,0)};foreach(string color in new[]{"#B8A4FF","#FFFFFF","#F8F7F2","#25252C","#000000","#FC9775","#E9C66D","#A7D1B3","#85BDEC","#DBA3CE","#C8D0E0","#E55966"}){string hex=color;var button=new Button{Width=43,Height=36,Margin=new Thickness(0,0,6,7),Background=B(hex),BorderBrush=B("#5B5369"),BorderThickness=new Thickness(1)};button.Click+=(_,_)=>field.Text=hex;swatches.Children.Add(button);}panel.Children.Add(swatches);field.TextChanged+=(_,_)=>{try{preview.Background=B(field.Text);}catch{}};string? result=null;AddDialogButtons(panel,dialog,"Apply color",()=>{try{var parsed=(Color)ColorConverter.ConvertFromString(field.Text);result=$"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";dialog.DialogResult=true;}catch{field.BorderBrush=B("#E88E96");}});dialog.Content=panel;dialog.ShowDialog();return result;
    }
    private void Help_Click(object sender,RoutedEventArgs e)
    {
        var dialog=MakeDialog("Keyboard shortcuts",510);var panel=new StackPanel();AddDialogHeading(panel,"Keyboard shortcuts","The essentials for your editing workflow.");
        foreach(var (label,key) in new[]{("Move / Select / Crop","V  /  M  /  C"),("Brush / Eraser / Eyedropper","B  /  E  /  I"),("Text / Rectangle / Hand / Zoom","T  /  U  /  H  /  Z"),("Pan temporarily","Hold Space + drag"),("Undo / Redo","Ctrl+Z / Ctrl+Shift+Z"),("New / Open / Save","Ctrl+N / O / S"),("Export image","Ctrl+Shift+E"),("Duplicate / Rename layer","Ctrl+J / F2"),("Fit canvas / Actual size","Ctrl+0 / Ctrl+1"),("Move by 1 px / 10 px","Arrow / Shift+Arrow"),("Apply crop / Cancel selection","Enter / Esc")}) {
            var row=new Grid{Margin=new Thickness(0,0,0,13)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(16)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            row.Children.Add(new TextBlock{Text=label,FontSize=12,Foreground=B("#C4BDD1"),TextWrapping=TextWrapping.Wrap});
            var shortcut=new TextBlock{Text=key,FontSize=11,Foreground=B("#B8A4FF"),HorizontalAlignment=HorizontalAlignment.Right};Grid.SetColumn(shortcut,2);row.Children.Add(shortcut);panel.Children.Add(row);
        }
        panel.Children.Add(new TextBlock{Text="Prism 0.2.1 · Native Windows editor\nDefault artwork: user-provided CIPHER 26 festival poster",FontSize=11,Foreground=B("#9E92AE"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        var done=new Button{Content="Got it",Style=(Style)FindResource("DialogSecondaryButton"),HorizontalAlignment=HorizontalAlignment.Right,IsCancel=true};done.Click+=(_,_)=>dialog.Close();((PrismDialog)dialog).Footer=done;dialog.Content=panel;dialog.ShowDialog();
    }
    private void Window_KeyDown(object sender,KeyEventArgs e)
    {
        if(!_ready||e.OriginalSource is TextBox||e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase)return;
        bool ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0,shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
        if(ctrl){switch(e.Key){case Key.N:New_Click(this,new());break;case Key.O:if(shift)PlaceImage();else Open_Click(this,new());break;case Key.S:SaveProject(shift);break;case Key.E:if(shift)Export_Click(this,new());break;case Key.Z:RestoreHistory(_historyIndex+(shift?1:-1));break;case Key.Y:RestoreHistory(_historyIndex+1);break;case Key.J:Duplicate_Click(this,new());break;case Key.D:Surface.Marquee=null;Surface.Refresh();break;case Key.A:Surface.Marquee=new Rect(0,0,_document.Width,_document.Height);Surface.Refresh();break;case Key.D0:case Key.NumPad0:FitCanvas();break;case Key.D1:case Key.NumPad1:SetZoom(1);break;case Key.OemPlus:case Key.Add:SetZoom(Surface.Zoom*1.2);break;case Key.OemMinus:case Key.Subtract:SetZoom(Surface.Zoom/1.2);break;case Key.OemCloseBrackets:ReorderLayer(-1);break;case Key.OemOpenBrackets:ReorderLayer(1);break;default:return;}e.Handled=true;return;}
        var tools=new Dictionary<Key,string>{{Key.V,"move"},{Key.M,"select"},{Key.C,"crop"},{Key.B,"brush"},{Key.E,"eraser"},{Key.I,"dropper"},{Key.T,"text"},{Key.U,"shape"},{Key.H,"hand"},{Key.Z,"zoom"}};
        if(tools.TryGetValue(e.Key,out var tool)){SelectTool(tool);e.Handled=true;return;}
        switch(e.Key){case Key.Space:_space=true;Surface.Cursor=Cursors.Hand;e.Handled=true;break;case Key.Delete:DeleteLayer_Click(this,new());e.Handled=true;break;case Key.F2:RenameLayer();e.Handled=true;break;case Key.Escape:Surface.Marquee=null;Surface.Refresh();SelectTool("move");break;case Key.Enter:if(_tool=="crop")ApplyCrop();break;case Key.D:_foreground="#000000";ForegroundSwatch.Background=B(_foreground);break;case Key.Left:case Key.Right:case Key.Up:case Key.Down:if(Editable()){double amount=shift?10:1;_selected!.X+=e.Key==Key.Left?-amount:e.Key==Key.Right?amount:0;_selected.Y+=e.Key==Key.Up?-amount:e.Key==Key.Down?amount:0;Commit("Nudge layer");e.Handled=true;}break;}
    }
    private void Title_Drag(object sender,MouseButtonEventArgs e){if(e.OriginalSource is TextBlock||e.OriginalSource is Border||e.OriginalSource is Grid){if(e.ClickCount==2)Maximize_Click(this,new());else DragMove();}}
    private void Minimize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void Maximize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void Window_Closing(object? sender,System.ComponentModel.CancelEventArgs e){if(_ready&&!ConfirmDiscard())e.Cancel=true;}
}
