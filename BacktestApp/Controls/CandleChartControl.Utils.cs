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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetTs(int logicalIndex)
        => RingTsAtLogical(logicalIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetO(int logicalIndex)
        => RingOAtLogical(logicalIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetH(int logicalIndex)
        => RingHAtLogical(logicalIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetL(int logicalIndex)
        => RingLAtLogical(logicalIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long GetC(int logicalIndex)
        => RingCAtLogical(logicalIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint GetV(int logicalIndex)
        => RingVAtLogical(logicalIndex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte GetSym(int logicalIndex)
        => RingSymAtLogical(logicalIndex);
}