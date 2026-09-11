using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Analysis;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class LinearCombinationAnalysisControl : UserControl
{
    private readonly ComboBox sweep=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=105};
    private readonly NumericUpDown start=Number(-1);
    private readonly NumericUpDown stop=Number(1);
    private readonly DataGridView fixedGrid=new(){Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
    private readonly AffineTransferPlotControl plot=new(){Dock=DockStyle.Fill};
    private AnalogComputeDesignResult? result;
    private bool updating;

    public LinearCombinationAnalysisControl()
    {
        Dock=DockStyle.Fill;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(8)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Padding=new Padding(0,0,0,5)};
        var update=new Button{Text="Update Plot",AutoSize=true,MinimumSize=new Size(95,27)};
        toolbar.Controls.Add(Label("Sweep Input:"));toolbar.Controls.Add(sweep);toolbar.Controls.Add(Label("From (V):"));toolbar.Controls.Add(start);toolbar.Controls.Add(Label("To (V):"));toolbar.Controls.Add(stop);toolbar.Controls.Add(update);
        var split=new SplitContainer{Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,SplitterWidth=6};
        split.SizeChanged+=(_,_)=>{var available=split.ClientSize.Width-split.SplitterWidth;if(available>480)split.SplitterDistance=Math.Clamp(250,190,available-290);};
        var fixedGroup=new GroupBox{Text="Fixed Inputs",Dock=DockStyle.Fill,Padding=new Padding(7,18,7,7)};
        fixedGrid.Columns.Add("Input","Input");fixedGrid.Columns.Add("Voltage","Voltage (V)");fixedGrid.Columns[0].ReadOnly=true;
        fixedGroup.Controls.Add(fixedGrid);split.Panel1.Controls.Add(fixedGroup);split.Panel2.Controls.Add(plot);
        root.Controls.Add(toolbar,0,0);root.Controls.Add(split,0,1);Controls.Add(root);
        sweep.SelectedIndexChanged+=(_,_)=>{if(!updating){RefreshRows();Analyze();}};
        update.Click+=(_,_)=>Analyze();fixedGrid.CellEndEdit+=(_,_)=>Analyze();
    }

    public void UpdateResult(AnalogComputeDesignResult value)
    {
        ArgumentNullException.ThrowIfNull(value);result=value;
        plot.LowerLimit=value.Request.SupplyVeeVoltage+value.Request.OpAmp.OutputSwingHeadroomVoltage;
        plot.UpperLimit=value.Request.SupplyVccVoltage-value.Request.OpAmp.OutputSwingHeadroomVoltage;
        var names=value.Request.Inputs.Select(input=>input.Name).ToArray();
        var existing=sweep.Items.Cast<object>().Select(Convert.ToString).ToArray();
        if(!existing.SequenceEqual(names,StringComparer.OrdinalIgnoreCase))
        {
            updating=true;
            try
            {
                sweep.Items.Clear();sweep.Items.AddRange(names.Cast<object>().ToArray());fixedGrid.Rows.Clear();
                foreach(var input in value.Request.Inputs)fixedGrid.Rows.Add(input.Name,input.NominalVoltage);
                if(sweep.Items.Count>0)sweep.SelectedIndex=0;
            }
            finally{updating=false;}
        }
        RefreshRows();Analyze();
    }

    private void RefreshRows()
    {
        foreach(DataGridViewRow row in fixedGrid.Rows)
        {
            var swept=string.Equals(Convert.ToString(row.Cells[0].Value),Convert.ToString(sweep.SelectedItem),StringComparison.OrdinalIgnoreCase);
            row.Cells[1].ReadOnly=swept;row.DefaultCellStyle.BackColor=swept?SystemColors.Control:SystemColors.Window;
            row.Cells[1].ToolTipText=swept?"This input is swept on the horizontal axis.":"This input is held at the entered voltage.";
        }
    }

    private void Analyze()
    {
        if(result is null||sweep.SelectedItem is null)return;
        try
        {
            var fixedValues=fixedGrid.Rows.Cast<DataGridViewRow>().ToDictionary(row=>Convert.ToString(row.Cells[0].Value)!,row=>double.TryParse(Convert.ToString(row.Cells[1].Value),out var number)?number:throw new FormatException($"{row.Cells[0].Value} fixed voltage is invalid."),StringComparer.OrdinalIgnoreCase);
            var selected=Convert.ToString(sweep.SelectedItem)!;
            plot.SweepInput=selected;plot.Analysis=new AffineSweepAnalysisService().Analyze(result,selected,fixedValues,(double)start.Value,(double)stop.Value);
        }
        catch(Exception exception){MessageBox.Show(this,exception.Message,"Analysis",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }

    private static NumericUpDown Number(decimal value)=>new CompactNumericUpDown{Minimum=-1_000_000,Maximum=1_000_000,Value=value,DecimalPlaces=4,Width=125,ThousandsSeparator=true};
    private static Label Label(string text)=>new(){Text=text,AutoSize=true,Margin=new Padding(5,7,4,0)};
}
