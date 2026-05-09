using System.Windows.Media;

namespace FlightSaver.Models;

public enum AltitudeBand { Low, Mid, High }

public static class AltitudeBands
{
    public static AltitudeBand Classify(double meters)
    {
        if (meters < 1000) return AltitudeBand.Low;
        if (meters < 6000) return AltitudeBand.Mid;
        return AltitudeBand.High;
    }

    public static Color ToColor(this AltitudeBand band) => band switch
    {
        AltitudeBand.Low  => Color.FromRgb(0xFB, 0x71, 0x85),
        AltitudeBand.Mid  => Color.FromRgb(0xFC, 0xD3, 0x4D),
        AltitudeBand.High => Color.FromRgb(0x67, 0xE8, 0xF9),
        _ => Colors.White,
    };
}
