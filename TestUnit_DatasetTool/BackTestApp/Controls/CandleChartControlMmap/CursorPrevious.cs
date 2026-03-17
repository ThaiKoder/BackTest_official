using System.Linq;
using Xunit;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlMmap;

public class CursorPrevious
{
    [Fact]
    public void LoadPrevious_Should_Move_Backward_And_Change_LoadedCandles()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_InitializeFilesAndCandlesMode();
        Assert.True(chart.Test_AdvanceCandlesNext(), "Il faut au moins un loadNext() avant de tester loadPrevious().");

        int beforeFileIdx = chart.Test_GetUiLoadedFileIdx();
        int beforeCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        var beforeTs = chart.Test_GetLoadedTimestamps().ToArray();

        Assert.True(beforeFileIdx >= 0);
        Assert.True(beforeCurrentIdx >= 0);
        Assert.NotEmpty(beforeTs);

        chart.loadPrevious();

        int afterFileIdx = chart.Test_GetUiLoadedFileIdx();
        int afterCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        var afterTs = chart.Test_GetLoadedTimestamps().ToArray();

        Assert.True(afterFileIdx >= 0);
        Assert.True(afterCurrentIdx >= 0);
        Assert.NotEmpty(afterTs);

        Assert.False(beforeTs.SequenceEqual(afterTs),
            "loadPrevious() doit modifier la fenêtre chargée.");

        if (afterFileIdx == beforeFileIdx)
        {
            Assert.True(afterCurrentIdx < beforeCurrentIdx,
                $"Le curseur candle doit reculer dans le même fichier. before={beforeCurrentIdx}, after={afterCurrentIdx}");
        }
        else
        {
            Assert.True(afterFileIdx < beforeFileIdx,
                $"Le fileIdx doit reculer. before={beforeFileIdx}, after={afterFileIdx}");
        }
    }

    [Fact]
    public void LoadPrevious_From_First_Position_Should_Be_NoOp()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();
        chart.Test_InitializeFilesAndCandlesMode();

        int beforeFileIdx = chart.Test_GetUiLoadedFileIdx();
        int beforeCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        var beforeTs = chart.Test_GetLoadedTimestamps().ToArray();

        chart.loadPrevious();

        Assert.Equal(beforeFileIdx, chart.Test_GetUiLoadedFileIdx());
        Assert.Equal(beforeCurrentIdx, chart.Test_GetUiCandleCurrentIdx());
        Assert.True(beforeTs.SequenceEqual(chart.Test_GetLoadedTimestamps()));
    }
}
