using System.Numerics;
using System.Reflection;
using Raylib_cs;
using static Raylib_cs.Raylib;
using Vaporworks;

namespace Afterlight;

public static class TrialGate
{
    public static int Blocked(VaporSession session,string[] args)
    {
        bool capture=args.Contains("--gate-shot");
        SetTraceLogLevel(TraceLogLevel.Warning);
        SetConfigFlags(ConfigFlags.VSyncHint | (capture ? ConfigFlags.HiddenWindow : 0));
        InitWindow(1000,640,"Afterlight | License required");SetTargetFPS(60);SetExitKey(KeyboardKey.Null);
        var regular=Load("segoeui.ttf",40);var bold=Load("seguisb.ttf",48);var display=Load("bahnschrift.ttf",96);
        Texture2D art=default;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Afterlight.Assets.courtyard.png"))
        {if(stream!=null){using var mem=new MemoryStream();stream.CopyTo(mem);var img=LoadImageFromMemory(".png",mem.ToArray());art=LoadTextureFromImage(img);UnloadImage(img);}}
        bool expired=session.Code=="trial_ended";
        var primary=new Rectangle(170,439,380,58);var exit=new Rectangle(565,439,265,58);
        int frame=0;
        while(!WindowShouldClose())
        {
            frame++;SetMouseCursor(MouseCursor.Default);
            var mouse=GetMousePosition();bool hover=CheckCollisionPointRec(mouse,primary),quitHover=CheckCollisionPointRec(mouse,exit);
            BeginDrawing();ClearBackground(Scene.Ink);
            if(art.Id!=0)DrawTexturePro(art,new(0,0,art.Width,art.Height),new(0,0,1000,640),Vector2.Zero,0,Color.White);
            DrawRectangle(0,0,1000,640,new(8,23,31,170));
            Text("V A P O R  /  AFTERLIGHT",48,38,16,Scene.White,true);
            DrawRectangle(125,137,750,412,new(16,34,43,248));
            DrawRectangleLinesEx(new(125,137,750,412),1,new(70,97,105,255));
            DrawRectangle(170,178,31,3,expired?Scene.Orange:Scene.Teal);
            Text(expired?"TRIAL COMPLETE":"LICENSE REQUIRED",215,170,14,expired?Scene.Orange:Scene.Teal,true);
            DrawTextEx(display,expired?"Your trial has ended.":"Return to your library.",new(167,217),49,.5f,Scene.White);
            Text(expired?"The courtyard will be here when you get back.":"Afterlight needs a verified Vapor session to start.",170,291,22,Scene.White);
            Text(expired?"A full game license is required to continue playing.":"Open the launcher to check your license and launch the game.",170,329,18,Fade(Scene.White,.63f));
            Text("AFTERLIGHT   /   OUTPOST 07",170,383,13,Fade(Scene.White,.48f),true);
            DrawRectangleRec(primary,hover?Scene.White:Scene.Teal);Text("Open Vapor library",201,453,22,Scene.Ink,true);
            DrawRectangleRec(exit,new(35,56,65,255));DrawRectangleLinesEx(exit,1,Fade(Scene.White,.25f));Text("Close game",625,453,22,Scene.White,true);
            Text("AFTERLIGHT",381,580,13,Fade(Scene.White,.5f));
            EndDrawing();
            if(hover||quitHover)SetMouseCursor(MouseCursor.PointingHand);
            if(capture&&frame==8)
            {
                string screenshotPath=Environment.GetEnvironmentVariable("VAPOR_GATE_SHOT")??Path.Combine(AppContext.BaseDirectory,"trial-gate.png");
                var screenshot=LoadImageFromScreen();
                ExportImage(screenshot,screenshotPath);
                UnloadImage(screenshot);
                break;
            }
            if(IsKeyPressed(KeyboardKey.Escape)||quitHover&&IsMouseButtonPressed(MouseButton.Left))break;
            if(IsKeyPressed(KeyboardKey.Enter)||hover&&IsMouseButtonPressed(MouseButton.Left)){VaporAPI.OpenLauncher();break;}
        }
        if(art.Id!=0)UnloadTexture(art);
        if(regular.Texture.Id!=GetFontDefault().Texture.Id)UnloadFont(regular);if(bold.Texture.Id!=GetFontDefault().Texture.Id)UnloadFont(bold);if(display.Texture.Id!=GetFontDefault().Texture.Id)UnloadFont(display);
        CloseWindow();return 0;
        void Text(string text,float x,float y,float size,Color color,bool strong=false)=>DrawTextEx(strong?bold:regular,text,new(x,y),size,.4f,color);
    }
    static Font Load(string file,int size)
    {
        string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),file);
        if(!File.Exists(path))return GetFontDefault();var font=LoadFontEx(path,size,null,0);SetTextureFilter(font.Texture,TextureFilter.Bilinear);return font;
    }
}
