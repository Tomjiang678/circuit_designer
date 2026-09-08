using System.ComponentModel;
using System.Drawing.Drawing2D;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class AnalogBodePlotControl:Control
{
    private AnalogAnalysisResult? analysis;
    private int? hoverIndex;
    private RectangleF magnitudeBounds;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AnalogAnalysisResult? Analysis{get=>analysis;set{analysis=value;hoverIndex=null;Invalidate();}}
    public AnalogBodePlotControl(){DoubleBuffered=true;ResizeRedraw=true;BackColor=SystemColors.Window;Cursor=Cursors.Cross;}
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);if(analysis is null||!magnitudeBounds.Contains(e.Location)){hoverIndex=null;Invalidate();return;}
        var logMin=Math.Log10(analysis.Points[0].Input);var logMax=Math.Log10(analysis.Points[^1].Input);
        var value=Math.Pow(10,logMin+(e.X-magnitudeBounds.Left)/magnitudeBounds.Width*(logMax-logMin));
        hoverIndex=Enumerable.Range(0,analysis.Points.Count).MinBy(index=>Math.Abs(Math.Log(analysis.Points[index].Input/value)));Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hoverIndex=null;Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var current=analysis;if(current is null||current.Points.Count<2){e.Graphics.DrawString("No AC data.",Font,Brushes.DimGray,20,20);return;}
        var left=78f;var width=Math.Max(120,ClientSize.Width-110);var half=Math.Max(120,(ClientSize.Height-105)/2f);
        magnitudeBounds=new(left,38,width,half-18);var phaseBounds=new RectangleF(left,half+55,width,half-18);
        var minDb=current.Points.Min(point=>Math.Min(point.Target,Math.Min(point.Theoretical??point.Target,point.Actual)));
        var maxDb=current.Points.Max(point=>Math.Max(point.Target,Math.Max(point.Theoretical??point.Target,point.Actual)));
        if(maxDb-minDb<1){minDb-=.5;maxDb+=.5;}else{var pad=(maxDb-minDb)*.08;minDb-=pad;maxDb+=pad;}
        var phases=current.Points.SelectMany(point=>new[]{point.TargetPhaseDegrees??0,point.ActualPhaseDegrees??0}).ToArray();
        var minPhase=phases.Min()-10;var maxPhase=phases.Max()+10;if(maxPhase-minPhase<20){minPhase-=10;maxPhase+=10;}
        DrawAxes(e.Graphics,magnitudeBounds,"Magnitude (dB)",minDb,maxDb,current);
        DrawAxes(e.Graphics,phaseBounds,"Phase (deg)",minPhase,maxPhase,current);
        Draw(e.Graphics,magnitudeBounds,current.Points.Select(point=>(point.Input,point.Target)),minDb,maxDb,Color.Gray,DashStyle.Dash);
        Draw(e.Graphics,magnitudeBounds,current.Points.Select(point=>(point.Input,point.Theoretical??point.Target)),minDb,maxDb,Color.DarkOrange,DashStyle.Dot);
        Draw(e.Graphics,magnitudeBounds,current.Points.Select(point=>(point.Input,point.Actual)),minDb,maxDb,Color.Navy,DashStyle.Solid);
        Draw(e.Graphics,phaseBounds,current.Points.Select(point=>(point.Input,point.TargetPhaseDegrees??0)),minPhase,maxPhase,Color.Gray,DashStyle.Dash);
        Draw(e.Graphics,phaseBounds,current.Points.Select(point=>(point.Input,point.ActualPhaseDegrees??0)),minPhase,maxPhase,Color.Navy,DashStyle.Solid);
        e.Graphics.DrawString("Target --   Theoretical ···   Selected —",Font,Brushes.Black,left,12);
        if(hoverIndex is int index)
        {
            var p=current.Points[index];var x=X(p.Input,magnitudeBounds,current);
            using var pen=new Pen(Color.DarkOrange){DashStyle=DashStyle.Dash};e.Graphics.DrawLine(pen,x,magnitudeBounds.Top,x,phaseBounds.Bottom);
            var text=$"f={p.Input:G8} Hz\nTarget={p.Target:G7} dB\nTheory={(p.Theoretical??p.Target):G7} dB\nSelected={p.Actual:G7} dB\nPhase={(p.ActualPhaseDegrees??0):G7} deg";
            var size=e.Graphics.MeasureString(text,Font);var bx=x+10;if(bx+size.Width+12>ClientSize.Width)bx=x-size.Width-18;
            var box=new RectangleF(bx,magnitudeBounds.Top+8,size.Width+10,size.Height+7);e.Graphics.FillRectangle(Brushes.LightYellow,box);e.Graphics.DrawRectangle(Pens.DarkGoldenrod,box.X,box.Y,box.Width,box.Height);e.Graphics.DrawString(text,Font,Brushes.Black,box.X+5,box.Y+3);
        }
    }
    private void DrawAxes(Graphics g,RectangleF bounds,string label,double min,double max,AnalogAnalysisResult current)
    {
        using var grid=new Pen(Color.Gainsboro);for(var i=0;i<=5;i++){var y=bounds.Top+bounds.Height*i/5;g.DrawLine(grid,bounds.Left,y,bounds.Right,y);g.DrawString((max-(max-min)*i/5).ToString("G4"),Font,Brushes.DimGray,5,y-7);}
        g.DrawRectangle(Pens.DimGray,bounds.X,bounds.Y,bounds.Width,bounds.Height);g.DrawString(label,Font,Brushes.Black,5,bounds.Top-22);
        for(var i=0;i<=4;i++){var fraction=i/4d;var f=current.Points[0].Input*Math.Pow(current.Points[^1].Input/current.Points[0].Input,fraction);var x=bounds.Left+bounds.Width*(float)fraction;g.DrawLine(grid,x,bounds.Top,x,bounds.Bottom);g.DrawString(f.ToString("G4"),Font,Brushes.DimGray,x-12,bounds.Bottom+3);}
    }
    private static void Draw(Graphics g,RectangleF bounds,IEnumerable<(double F,double Y)> values,double min,double max,Color color,DashStyle dash)
    {
        var data=values.ToArray();var logMin=Math.Log10(data[0].F);var logMax=Math.Log10(data[^1].F);
        var points=data.Select(value=>new PointF(bounds.Left+(float)((Math.Log10(value.F)-logMin)/(logMax-logMin))*bounds.Width,bounds.Bottom-(float)((value.Y-min)/(max-min))*bounds.Height)).ToArray();
        using var pen=new Pen(color,2){DashStyle=dash};g.DrawLines(pen,points);
    }
    private static float X(double frequency,RectangleF bounds,AnalogAnalysisResult current)=>
        bounds.Left+(float)((Math.Log10(frequency)-Math.Log10(current.Points[0].Input))/(Math.Log10(current.Points[^1].Input)-Math.Log10(current.Points[0].Input)))*bounds.Width;
}
