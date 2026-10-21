using System.Globalization;

namespace CircuitModuleDesigner.Controls;

/// <summary>
/// Shared sizing helpers for editable parameter fields. Numeric boxes are sized
/// for the widest value the control can hold (not just its current value), and
/// combo boxes are sized for the longest item so enum names are not clipped.
/// </summary>
internal static class UiFieldMetrics
{
    public static int NumberWidth(decimal minimum, decimal maximum, int decimals, int minimumWidth = 130, int maximumWidth = 240)
    {
        var format = "N" + decimals.ToString(CultureInfo.InvariantCulture);
        var textWidth = new[] { minimum, maximum }
            .Select(value => value.ToString(format, CultureInfo.CurrentCulture))
            .Max(text => TextRenderer.MeasureText(text, SystemFonts.MessageBoxFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width);
        var width = textWidth + SystemInformation.VerticalScrollBarWidth + 10;
        return Math.Clamp(width, minimumWidth, maximumWidth);
    }

    public static void FitCombo(ComboBox box, int minimumWidth = 130, int maximumWidth = 260)
    {
        var longest = box.Items.Cast<object>()
            .Select(item => item?.ToString() ?? string.Empty)
            .OrderByDescending(text => TextRenderer.MeasureText(text, box.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width)
            .FirstOrDefault() ?? string.Empty;
        var textWidth = TextRenderer.MeasureText(longest, box.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        var width = textWidth + SystemInformation.VerticalScrollBarWidth + 12;
        box.Width = Math.Clamp(width, minimumWidth, maximumWidth);
        box.DropDownWidth = Math.Max(box.Width, width + 20);
    }
}
