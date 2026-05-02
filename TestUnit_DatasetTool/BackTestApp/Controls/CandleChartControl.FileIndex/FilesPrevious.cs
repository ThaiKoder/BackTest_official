using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControl.FileIndex;

public class FilesPrevious
{
    private const string IndexPath = "data/bin/_index.bin";
    private const int ExpectedFileCount = 817;

    private static global::BacktestApp.Controls.CandleChartControl.FileIndex LoadIndex()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();
        var index = chart.Test_indexReader();
        index.Load(IndexPath);
        return index;
    }

    private static uint[] ReadExpectedStartsRaw(
        global::BacktestApp.Controls.CandleChartControl.FileIndex index)
    {
        var result = new uint[index.Count];

        for (long i = 0; i < index.Count; i++)
            result[i] = index.Read(i).StartYmd;

        return result;
    }

    private static uint[] TraverseFilesPreviousAndRebuildChronological(
        global::BacktestApp.Controls.CandleChartControl.FileIndex index,
        int range)
    {
        var byIdx = new SortedDictionary<int, uint>();

        int startCursor = (int)index.Count - 1;
        var step = index.FilesPrevious(startCursor, range);

        while (true)
        {
            foreach (var file in step.Added)
            {
                if (file.Idx != -1)
                    byIdx[file.Idx] = file.StartYmd;
            }

            if (step.PreviousCursorIdx == -1)
                break;

            step = index.FilesPrevious(step.PreviousCursorIdx, range);
        }

        return byIdx.Values.ToArray();
    }

    [Fact]
    public void No_Duplicate_Files()
    {
        using var index = LoadIndex();

        var seen = TraverseFilesPreviousAndRebuildChronological(index, range: 3);

        Assert.Equal(seen.Length, new HashSet<uint>(seen).Count);
    }

    [Fact]
    public void Files_Are_Strictly_Chronological_After_Rebuild()
    {
        using var index = LoadIndex();

        var seen = TraverseFilesPreviousAndRebuildChronological(index, range: 3);

        for (int i = 1; i < seen.Length; i++)
        {
            Assert.True(
                seen[i] > seen[i - 1],
                $"FilesPrevious doit permettre de reconstruire les fichiers en ordre chronologique strict. i={i}, prev={seen[i - 1]}, cur={seen[i]}");
        }
    }

    [Fact]
    public void Same_File_Count_As_Index()
    {
        using var index = LoadIndex();

        var seen = TraverseFilesPreviousAndRebuildChronological(index, range: 3);

        Assert.Equal(ExpectedFileCount, index.Count);
        Assert.Equal(index.Count, seen.Length);
    }

    [Fact]
    public void Full_FilesPrevious_NoDuplicate_Chronological_SameQuantity()
    {
        using var index = LoadIndex();

        var expected = ReadExpectedStartsRaw(index);
        var actual = TraverseFilesPreviousAndRebuildChronological(index, range: 3);

        Assert.Equal(ExpectedFileCount, index.Count);
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected, actual);
        Assert.Equal(actual.Length, new HashSet<uint>(actual).Count);

        for (int i = 1; i < actual.Length; i++)
            Assert.True(actual[i] > actual[i - 1]);
    }
}