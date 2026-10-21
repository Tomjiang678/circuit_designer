using System.ComponentModel;
using System.Drawing.Drawing2D;
using CircuitModuleDesigner.Core.Engineering;
using CircuitModuleDesigner.Modules.Comparators.Analysis;

namespace CircuitModuleDesigner.Modules.Comparators.Controls;

public sealed class RelaxationOscillatorPlotControl:Control
{
    private RelaxationOscillatorAnalysis? analysis;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RelaxationOscillatorAnalysis? Analysis
    {
        get=>analysis;
        set{analysis=value;Invalidate();}
    }

    public RelaxationOscillatorPlotControl()
    {
        Dock=DockStyle.Fill;DoubleBuffered=true;ResizeRedraw=true;BackColor=SystemColors.Window;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);e.Graphics.Clear(BackColor);
        var data=analysis;
        if(data is null||data.Points.Count<2)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var left=82f;
        var width=Math.Max(120,ClientSize.Width-112);
        var half=Math.Max(100,(ClientSize.Height-92)/2f);
        var outputBounds=new RectangleF(left,28,width,half-18);
        var capacitorBounds=new RectangleF(left,half+52,width,half-18);
        var minimum=data.Points.Min(point=>Math.Min(point.OutputVoltage,point.CapacitorVoltage));
        var maximum=data.Points.Max(point=>Math.Max(point.OutputVoltage,point.CapacitorVoltage));
        var padding=Math.Max(.05,(maximum-minimum)*.12);
        minimum-=padding;maximum+=padding;
        Axes(e.Graphics,outputBounds,"Vout (V)",minimum,maximum,data.Points[^1].TimeSeconds);
        Axes(e.Graphics,capacitorBounds,"Vcap (V)",minimum,maximum,data.Points[^1].TimeSeconds);
        Curve(e.Graphics,outputBounds,data.Points.Select(point=>(point.TimeSeconds,point.OutputVoltage)),minimum,maximum,Color.Navy);
        Curve(e.Graphics,capacitorBounds,data.Points.Select(point=>(point.TimeSeconds,point.CapacitorVoltage)),minimum,maximum,Color.DarkOrange);
        using var guide=new Pen(Color.Gray,1){DashStyle=DashStyle.Dash};
        Threshold(e.Graphics,capacitorBounds,guide,data.LowerThresholdVoltage,minimum,maximum,"VTL");
        Threshold(e.Graphics,capacitorBounds,guide,data.UpperThresholdVoltage,minimum,maximum,"VTH");
        var frequency=EngineeringValueFormatter.Format(data.FrequencyHz,"Hz");
        var size=e.Graphics.MeasureString(frequency,Font);
        e.Graphics.DrawString(frequency,Font,Brushes.DimGray,Math.Max(left,ClientSize.Width-size.Width-12),3);
    }

    private void Axes(Graphics graphics,RectangleF bounds,string label,double minimum,double maximum,double timeMaximum)
    {
        using var grid=new Pen(Color.Gainsboro);
        for(var index=0;index<=4;index++)
        {
            var y=bounds.Top+bounds.Height*index/4;
            graphics.DrawLine(grid,bounds.Left,y,bounds.Right,y);
            graphics.DrawString((maximum-(maximum-minimum)*index/4).ToString("G4"),Font,Brushes.DimGray,5,y-7);
        }
        for(var index=0;index<=4;index++)
        {
            var x=bounds.Left+bounds.Width*index/4;
            graphics.DrawLine(grid,x,bounds.Top,x,bounds.Bottom);
            var text=index==0?"0":EngineeringValueFormatter.Format(timeMaximum*index/4,"s");
            var size=graphics.MeasureString(text,Font);
            graphics.DrawString(text,Font,Brushes.DimGray,
                Math.Clamp(x-size.Width/2,2,Math.Max(2,ClientSize.Width-size.Width-2)),bounds.Bottom+3);
        }
        graphics.DrawRectangle(Pens.DimGray,bounds.X,bounds.Y,bounds.Width,bounds.Height);
        graphics.DrawString(label,Font,Brushes.Black,bounds.Left+5,bounds.Top+3);
    }

    private static void Curve(Graphics graphics,RectangleF bounds,IEnumerable<(double X,double Y)> values,
        double minimum,double maximum,Color color)
    {
        var samples=values.ToArray();
        var maxX=samples[^1].X;
        var points=samples.Select(value=>new PointF(
            bounds.Left+(float)(value.X/maxX)*bounds.Width,
            bounds.Bottom-(float)((value.Y-minimum)/(maximum-minimum))*bounds.Height)).ToArray();
        using var pen=new Pen(color,2){LineJoin=LineJoin.Round};
        graphics.DrawLines(pen,points);
    }

    private void Threshold(Graphics graphics,RectangleF bounds,Pen pen,double voltage,double minimum,double maximum,string label)
    {
        var y=bounds.Bottom-(float)((voltage-minimum)/(maximum-minimum))*bounds.Height;
        graphics.DrawLine(pen,bounds.Left,y,bounds.Right,y);
        graphics.DrawString($"{label}  {voltage:G4} V",Font,Brushes.DimGray,bounds.Left+8,y-16);
    }
}
