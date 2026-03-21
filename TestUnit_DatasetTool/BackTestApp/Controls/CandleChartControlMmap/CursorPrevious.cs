using System.Linq;
using Xunit;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlMmap;

public class CursorPrevious
{

    [Fact]
    public void CandlesPrevious_Should_Start_From_Last_File_And_Last_Candle()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_LoadIndexFile(Path.Combine("data", "bin", "_index.bin"));

        long fileCount = chart.Test_IndexCount;
        Assert.True(fileCount > 0);

        int lastFileIdx = (int)fileCount - 1;

        chart.Test_CandlesLoadFromCurrentFileIndex(lastFileIdx);

        long candleCount = chart.Test_CandleCount;
        Assert.True(candleCount > 0);

        int lastCandleIdx = (int)candleCount - 1;

        var step = chart.CandlesPrevious(lastCandleIdx, 10);

        Assert.Equal(lastCandleIdx, step.CurrentIdx);
        Assert.NotEmpty(step.Window);

        // la dernière candle réelle du fichier doit être présente dans la fenêtre
        Assert.Contains(step.Window, c => c.Idx == lastCandleIdx);
    }

    // Test minimal pour que loadPrevious() recule correctement le step des candles
    // et que les timestamps chargés changent en conséquence.
    [Fact]
    public void LoadPrevious_NewSequence_Should_Rewind_UiCandleStep_And_Change_LoadedCandles()
    {
        // Arrange
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_InitializeFilesAndCandlesMode();

        // On avance au moins une fois pour éviter d’être collé au tout début
        Assert.True(
            chart.Test_AdvanceCandlesNext(),
            "Impossible d'avancer une première fois pour préparer le test previous.");

        int beforeCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        var beforeTs = chart.Test_GetLoadedTimestamps();

        Assert.True(beforeCurrentIdx >= 0, $"CurrentIdx initial invalide: {beforeCurrentIdx}");
        Assert.NotNull(beforeTs);
        Assert.NotEmpty(beforeTs);

        // Act
        chart.loadPrevious();   // même chemin que le bouton UI

        int afterCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        var afterTs = chart.Test_GetLoadedTimestamps();

        // Assert
        Assert.True(afterCurrentIdx >= 0, $"CurrentIdx après recul invalide: {afterCurrentIdx}");
        Assert.NotNull(afterTs);
        Assert.NotEmpty(afterTs);

        Assert.NotEqual(
            beforeCurrentIdx,
            afterCurrentIdx);

        Assert.False(
            beforeTs.SequenceEqual(afterTs),
            $"Les candles chargées doivent changer. " +
            $"beforeCurrentIdx={beforeCurrentIdx}, afterCurrentIdx={afterCurrentIdx}, " +
            $"beforeFirstTs={beforeTs[0]}, beforeLastTs={beforeTs[^1]}, " +
            $"afterFirstTs={afterTs[0]}, afterLastTs={afterTs[^1]}");

        Assert.True(
            beforeTs[0] != afterTs[0] || beforeTs[^1] != afterTs[^1],
            $"La fenêtre doit changer au minimum en début ou en fin. " +
            $"beforeFirstTs={beforeTs[0]}, beforeLastTs={beforeTs[^1]}, " +
            $"afterFirstTs={afterTs[0]}, afterLastTs={afterTs[^1]}");

        // en previous, le curseur doit reculer
        Assert.True(
            afterCurrentIdx < beforeCurrentIdx || beforeTs[0] != afterTs[0],
            $"Le step ne semble pas avoir reculé correctement. " +
            $"beforeCurrent={beforeCurrentIdx}, afterCurrent={afterCurrentIdx}, " +
            $"beforeFirstTs={beforeTs[0]}, afterFirstTs={afterTs[0]}");
    }
}