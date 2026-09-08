using System.ComponentModel;
using System.Drawing.Drawing2D;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Analysis;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class RectifierWaveformPlotControl : Control
{
    private AnalogComputeDesignResult? result;
    private IReadOnlyList<RectifierWaveformPoint> points=[];
    private RectangleF plotBounds;
    private double minimumY;
    private double maximumY;
    private int? hoverIndex;
    private int? pinnedIndex;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AnalogComputeDesignResult? Result
    {
        get=>result;
        set
        {
            result=value;
            points=value is null?[]:new RectifierWaveformAnalysisService().Build(value);
            hoverIndex=null;pinnedIndex=null;Invalidate();
        }
    }

    public RectifierWaveformPlotControl()
    {
        DoubleBuffered=true;ResizeRedraw=true;BackColor=SystemColors.Window;Cursor=Cursors.Cross;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if(points.Count==0||!plotBounds.Contains(e.Location)){if(hoverIndex is not null){hoverIndex=null;Invalidate();}return;}
        var period=(e.X-plotBounds.Left)/plotBounds.Width*RectifierWaveformAnalysisService.PeriodCount;
        var nearest=(int)Math.Round(period/(double)RectifierWaveformAnalysisService.PeriodCount*(points.Count-1));
        nearest=Math.Clamp(nearest,0,points.Count-1);if(hoverIndex!=nearest){hoverIndex=nearest;Invalidate();}
    }

    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(hoverIndex is not null){hoverIndex=null;Invalidate();}}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left&&hoverIndex is int index){pinnedIndex=pinnedIndex==index?null:index;Invalidate();}}

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);e.Graphics.Clear(SystemColors.Window);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        if(points.Count<2){e.Graphics.DrawString("No rectifier waveform data.",Font,Brushes.DimGray,18,18);return;}
        plotBounds=new RectangleF(78,48,Math.Max(120,ClientSize.Width-110),Math.Max(100,ClientSize.Height-110));
        minimumY=points.Min(point=>point.SelectedVoltage);
        maximumY=points.Max(point=>point.SelectedVoltage);
        var padding=Math.Max(.1,(maximumY-minimumY)*.08);minimumY-=padding;maximumY+=padding;
        using var grid=new Pen(Color.Gainsboro);using var axis=new Pen(Color.DimGray,1.2f);
        for(var half=0;half<=RectifierWaveformAnalysisService.PeriodCount*2;half++)
        {
            var x=plotBounds.Left+plotBounds.Width*half/(RectifierWaveformAnalysisService.PeriodCount*2f);
            e.Graphics.DrawLine(grid,x,plotBounds.Top,x,plotBounds.Bottom);
            if(half%2==0)
            {
                var cycle=half/2;var label=cycle==0?"0":cycle==1?"T":$"{cycle}T";
                e.Graphics.DrawString(label,Font,Brushes.DimGray,x-8,plotBounds.Bottom+6);
            }
        }
        for(var row=0;row<=8;row++)
        {
            var y=plotBounds.Top+plotBounds.Height*row/8f;var value=maximumY-(maximumY-minimumY)*row/8;
            e.Graphics.DrawLine(grid,plotBounds.Left,y,plotBounds.Right,y);e.Graphics.DrawString(value.ToString("G4"),Font,Brushes.DimGray,8,y-8);
        }
        e.Graphics.DrawRectangle(axis,plotBounds.X,plotBounds.Y,plotBounds.Width,plotBounds.Height);
        DrawCurve(e.Graphics,points.Select(point=>(point.Periods,point.SelectedVoltage)),Color.Navy,DashStyle.Solid);
        using var titleFont=new Font(Font,FontStyle.Bold);e.Graphics.DrawString("Vout — three normalized periods",titleFont,Brushes.Black,plotBounds.Left,16);
        e.Graphics.DrawString("Normalized time",Font,Brushes.Black,plotBounds.Left+plotBounds.Width/2-40,plotBounds.Bottom+32);
        var state=e.Graphics.Save();e.Graphics.TranslateTransform(18,plotBounds.Top+plotBounds.Height/2+30);e.Graphics.RotateTransform(-90);e.Graphics.DrawString("Voltage (V)",Font,Brushes.Black,0,0);e.Graphics.Restore(state);
        DrawCursor(e.Graphics);
    }

    private void DrawCurve(Graphics graphics,IEnumerable<(double X,double Y)> values,Color color,DashStyle dash)
    {
        var screen=values.Select(value=>new PointF(plotBounds.Left+(float)(value.X/RectifierWaveformAnalysisService.PeriodCount*plotBounds.Width),plotBounds.Bottom-(float)((value.Y-minimumY)/(maximumY-minimumY)*plotBounds.Height))).ToArray();
        using var pen=new Pen(color,2){DashStyle=dash,LineJoin=LineJoin.Round};graphics.DrawLines(pen,screen);
    }

    private void DrawCursor(Graphics graphics)
    {
        var index=pinnedIndex??hoverIndex;if(index is null)return;var point=points[index.Value];
        var x=plotBounds.Left+(float)(point.Periods/RectifierWaveformAnalysisService.PeriodCount*plotBounds.Width);
        using var pen=new Pen(Color.DarkGoldenrod,1.2f){DashStyle=DashStyle.Dash};graphics.DrawLine(pen,x,plotBounds.Top,x,plotBounds.Bottom);
        var text=$"t = {point.Periods:0.###}T\nVout = {point.SelectedVoltage:G6} V";
        var size=graphics.MeasureString(text,Font);var boxX=x+12;if(boxX+size.Width+14>ClientSize.Width)boxX=x-size.Width-20;
        var box=new RectangleF(boxX,plotBounds.Top+8,size.Width+12,size.Height+9);using var background=new SolidBrush(Color.FromArgb(246,255,250,225));graphics.FillRectangle(background,box);graphics.DrawRectangle(Pens.DarkGoldenrod,box.X,box.Y,box.Width,box.Height);graphics.DrawString(text,Font,Brushes.Black,box.X+6,box.Y+4);
    }
}
