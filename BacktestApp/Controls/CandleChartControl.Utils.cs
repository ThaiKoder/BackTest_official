using System.Runtime.CompilerServices;

namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double TsNsToEpochSeconds(long tsNs) => tsNs / 1_000_000_000.0;


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Clamp(double v, double min, double max)
        => v < min ? min : (v > max ? max : v);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ClampInt(int v, int min, int max)
        => v < min ? min : (v > max ? max : v);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long ClampLong(long v, long min, long max)
        => v < min ? min : (v > max ? max : v);
}