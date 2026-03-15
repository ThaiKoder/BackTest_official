using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlMmap;

public class CursorNext
{
    //Teste minimal pour que loadNext() avance correctement le step des candles et que les timestamps chargés changent en conséquence.
    [Fact]
    public void LoadNext_NewSequence_Should_Advance_UiCandleStep_And_Change_LoadedCandles()
    {
        // Arrange
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_InitializeFilesAndCandlesMode();

        int beforeCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        int beforeNextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();
        var beforeTs = chart.Test_GetLoadedTimestamps();

        Assert.True(beforeCurrentIdx >= 0, $"CurrentIdx initial invalide: {beforeCurrentIdx}");
        Assert.True(beforeNextCursorIdx != -1, "Le curseur suivant est déjà à la fin du fichier.");
        Assert.NotNull(beforeTs);
        Assert.NotEmpty(beforeTs);

        // Act
        chart.loadNext();   // même chemin que le bouton UI

        int afterCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        int afterNextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();
        var afterTs = chart.Test_GetLoadedTimestamps();

        // Assert
        Assert.True(afterCurrentIdx >= 0, $"CurrentIdx après avance invalide: {afterCurrentIdx}");
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

        // contrôle bonus : cohérence du curseur suivant
        Assert.True(
            afterNextCursorIdx == -1 ||
            afterNextCursorIdx != beforeNextCursorIdx ||
            afterCurrentIdx != beforeCurrentIdx,
            $"Le step ne semble pas avoir avancé correctement. " +
            $"beforeNext={beforeNextCursorIdx}, afterNext={afterNextCursorIdx}, " +
            $"beforeCurrent={beforeCurrentIdx}, afterCurrent={afterCurrentIdx}");
    }

    [Fact]
    public void LoadNext_Should_Traverse_All_Candles_Of_Current_Index_File()
    {
        // Arrange
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_InitializeFilesAndCandlesMode();

        int initialCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        int initialNextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();
        var initialTs = chart.Test_GetLoadedTimestamps();

        Assert.True(initialCurrentIdx >= 0, $"CurrentIdx initial invalide: {initialCurrentIdx}");
        Assert.NotNull(initialTs);
        Assert.NotEmpty(initialTs);

        long expectedTotalCandles = chart.Test_GetUiFileCount();
        Assert.True(expectedTotalCandles > 0, $"Le fichier courant doit contenir au moins une candle. fileCount={expectedTotalCandles}");

        var allSeenTs = new HashSet<long>();
        int iterations = 0;

        // On compte aussi la fenêtre initiale déjà chargée par InitializeFilesAndCandlesMode()
        foreach (var ts in initialTs)
        {
            Assert.True(allSeenTs.Add(ts), $"Timestamp dupliqué dans la fenêtre initiale: ts={ts}");
        }

        int lastCurrentIdx = initialCurrentIdx;

        // Act
        while (chart.Test_GetUiCandleNextCursorIdx() != -1)
        {
            chart.loadNext(); // même chemin que le bouton UI
            iterations++;

            int currentIdx = chart.Test_GetUiCandleCurrentIdx();
            int nextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();
            var loadedTs = chart.Test_GetLoadedTimestamps();

            Assert.True(currentIdx >= 0, $"CurrentIdx invalide après loadNext: {currentIdx}");
            Assert.NotNull(loadedTs);
            Assert.NotEmpty(loadedTs);

            // La progression doit être strictement croissante
            Assert.True(
                currentIdx > lastCurrentIdx,
                $"Le curseur candle doit avancer strictement. before={lastCurrentIdx}, after={currentIdx}");

            // Les timestamps de la fenêtre doivent être strictement croissants
            for (int i = 1; i < loadedTs.Count; i++)
            {
                Assert.True(
                    loadedTs[i] > loadedTs[i - 1],
                    $"Les timestamps chargés doivent être strictement croissants. " +
                    $"i={i}, prev={loadedTs[i - 1]}, cur={loadedTs[i]}");
            }

            foreach (var ts in loadedTs)
            {
                allSeenTs.Add(ts);
            }

            lastCurrentIdx = currentIdx;

            // sécurité anti boucle infinie
            Assert.True(
                iterations <= expectedTotalCandles,
                $"Boucle suspecte: trop d'itérations. iterations={iterations}, fileCount={expectedTotalCandles}, next={nextCursorIdx}");
        }

        // Assert
        Assert.True(iterations > 0, "Le test doit effectuer au moins un loadNext().");

        var finalCurrentIdx = chart.Test_GetUiCandleCurrentIdx();
        var finalNextCursorIdx = chart.Test_GetUiCandleNextCursorIdx();
        var finalTs = chart.Test_GetLoadedTimestamps();

        Assert.True(finalCurrentIdx >= 0, $"CurrentIdx final invalide: {finalCurrentIdx}");
        Assert.Equal(-1, finalNextCursorIdx);

        Assert.NotNull(finalTs);
        Assert.NotEmpty(finalTs);

        // On doit avoir vu toutes les candles du fichier courant au moins une fois
        Assert.Equal(
            expectedTotalCandles,
            allSeenTs.Count);

        // Appel supplémentaire après fin => no-op
        chart.loadNext();

        Assert.Equal(finalCurrentIdx, chart.Test_GetUiCandleCurrentIdx());
        Assert.Equal(-1, chart.Test_GetUiCandleNextCursorIdx());
        Assert.True(finalTs.SequenceEqual(chart.Test_GetLoadedTimestamps()),
            "Après la fin du fichier, loadNext() ne doit plus modifier la fenêtre.");
    }


    [Fact]
    public void LoadNext_Should_Shift_Left_And_Append_Only_New_Candles()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();
        chart.Test_InitializeFilesAndCandlesMode();

        var before = chart.Test_GetLoadedTimestamps().ToArray();
        Assert.NotEmpty(before);

        chart.loadNext();

        var after = chart.Test_GetLoadedTimestamps().ToArray();
        Assert.NotEmpty(after);

        // Dans ton cas range=3 => step=4, mais au début du fichier
        // Removed peut être plus petit que Added.
        int removed = chart.Test_GetLastRemovedCount();
        int added = chart.Test_GetLastAddedCount();

        Assert.True(removed >= 0);
        Assert.True(added >= 0);

        int remain = before.Length - removed;
        Assert.True(remain >= 0);

        for (int i = 0; i < remain; i++)
        {
            Assert.Equal(before[i + removed], after[i]);
        }
    }
}
