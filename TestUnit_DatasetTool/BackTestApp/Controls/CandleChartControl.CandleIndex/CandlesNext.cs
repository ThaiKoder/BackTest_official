using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControl.CandleIndex;

//Pour 1 fichier BIN
public class CandlesNext
{
    private const string OneFilePath =
        "data/bin/glbx-mdp3-20100606-20100612.ohlcv-1m.bin";

    private static global::BacktestApp.Controls.CandleChartControl.CandleIndex LoadOneFile()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();
        var index = chart.Test_candleReader();
        index.Load(OneFilePath);
        return index;
    }

    private static long[] ReadExpectedTsRaw(
        global::BacktestApp.Controls.CandleChartControl.CandleIndex index)
    {
        var result = new long[index.Count];

        for (long i = 0; i < index.Count; i++)
            result[i] = index.Read(i).Ts;

        return result;
    }

    private static long[] TraverseCandlesNext(
        global::BacktestApp.Controls.CandleChartControl.CandleIndex index,
        int range)
    {
        var seen = new List<long>();
        var step = index.CandlesNext(0, range);

        while (true)
        {
            foreach (var candle in step.Added)
            {
                if (candle.Idx != -1)
                    seen.Add(candle.Ts);
            }

            if (step.NextCursorIdx == -1)
                break;

            step = index.CandlesNext(step.NextCursorIdx, range);
        }

        return seen.ToArray();
    }

    [Fact]
    public void No_Duplicate_Candles_In_One_File()
    {
        using var index = LoadOneFile();

        var seen = TraverseCandlesNext(index, range: 10);
        var unique = new HashSet<long>(seen);

        Assert.Equal(seen.Length, unique.Count);
    }

    [Fact]
    public void Candles_Are_Strictly_Chronological_In_One_File()
    {
        using var index = LoadOneFile();

        var seen = TraverseCandlesNext(index, range: 10);

        for (int i = 1; i < seen.Length; i++)
        {
            Assert.True(
                seen[i] > seen[i - 1],
                $"CandlesNext doit lire les candles en ordre chronologique strict. i={i}, prev={seen[i - 1]}, cur={seen[i]}");
        }
    }

    [Fact]
    public void Same_Candle_Count_As_Raw_File_In_One_File()
    {
        using var index = LoadOneFile();

        var expected = ReadExpectedTsRaw(index);
        var actual = TraverseCandlesNext(index, range: 10);

        Assert.Equal(expected.Length, actual.Length);
    }

    [Fact]
    public void Full_CandlesNext_One_File_NoDuplicate_Chronological_SameQuantity()
    {
        using var index = LoadOneFile();

        var expected = ReadExpectedTsRaw(index);
        var actual = TraverseCandlesNext(index, range: 10);

        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected, actual);
        Assert.Equal(actual.Length, new HashSet<long>(actual).Count);

        for (int i = 1; i < actual.Length; i++)
            Assert.True(actual[i] > actual[i - 1]);
    }
}