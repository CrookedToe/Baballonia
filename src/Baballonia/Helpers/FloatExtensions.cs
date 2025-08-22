namespace Baballonia.Helpers;

public static class FloatExtensions
{
    public static float Remap (this float value, float inputMin, float inputMax, float min, float max)
    {
        return min + (value - inputMin) * (max - min) / (inputMax - inputMin);
    }
}
