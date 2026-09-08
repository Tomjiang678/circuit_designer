using System.Globalization;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

/// <summary>
/// NumericUpDown that keeps the configured input precision without padding the
/// displayed value with insignificant trailing zeroes.
/// </summary>
internal sealed class CompactNumericUpDown : NumericUpDown
{
    public CompactNumericUpDown()
    {
        Increment = 1m;
    }

    protected override void UpdateEditText()
    {
        var integerFormat = ThousandsSeparator ? "#,0" : "0";
        var format = DecimalPlaces > 0
            ? integerFormat + "." + new string('#', DecimalPlaces)
            : integerFormat;
        Text = Value.ToString(format, CultureInfo.CurrentCulture);
    }
}
