using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlMmap;

public class CursorNext
{
    [Fact]
    public void Next_Button_Path_Should_Advance_Across_10_Contracts()
    {
        // Arrange
        const int nbContractsToAdvance = 10;
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_LoadIndex();
        int contracts = chart.Test_ContractsCount;

        int startIdx = 500;
        int targetIdx = startIdx + nbContractsToAdvance;

        Assert.True(targetIdx < contracts,
            $"Pas assez de contrats pour avancer de {nbContractsToAdvance}. startIdx={startIdx}, contracts={contracts}");

        // Même initialisation que le flux UI actuel : contrat chargé + voisins.
        chart.Test_LoadByIndexWithNeighbors(startIdx);

        // On force une surface visible de test pour que le calcul du plot soit stable.
        chart.Test_SetBoundsForTest(1200, 700);

        // On démarre depuis le début du fichier pour compter proprement les pressions.
        chart.Test_ReloadWindow(0);
        chart.Test_SetCenterToWindowMiddle();
        chart.Test_TickEdgeTimerOnce();

        int presses = 0;
        int switches = 0;
        int lastContractIdx = chart.Test_CurrentIdx;

        // Large sécurité pour éviter une boucle infinie en cas de régression.
        const int safetyMaxPresses = 200_000;

        // Act
        while (chart.Test_CurrentIdx < targetIdx && presses < safetyMaxPresses)
        {
            // Même chemin que le bouton Next dans l'UI.
            chart.loadNext();
            presses++;

            // En test, on recale le centre pour garder un comportement stable.
            chart.Test_SetCenterToWindowMiddle();
            chart.Test_TickEdgeTimerOnce();

            if (chart.Test_CurrentIdx != lastContractIdx)
            {
                switches++;
                lastContractIdx = chart.Test_CurrentIdx;
                Debug.WriteLine($"[NEXT] switch -> contractIdx={lastContractIdx}, presses={presses}, switches={switches}");
            }
        }

        // Assert
        Assert.True(presses < safetyMaxPresses,
            $"Boucle de sécurité atteinte. currentIdx={chart.Test_CurrentIdx}, targetIdx={targetIdx}");

        Assert.Equal(targetIdx, chart.Test_CurrentIdx);
        Assert.Equal(nbContractsToAdvance, switches);
    }

    [Fact]
    public void Next_Button_Path_Should_Switch_Contracts_Monotonically()
    {
        // Arrange
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_LoadIndex();
        int contracts = chart.Test_ContractsCount;
        Assert.True(contracts > 10, $"Pas assez de contrats. contracts={contracts}");

        int startIdx = 0;
        int nbContractsToAdvance = 10;
        int targetIdx = startIdx + nbContractsToAdvance;

        Assert.True(targetIdx < contracts,
            $"Target hors range. targetIdx={targetIdx}, contracts={contracts}");

        chart.Test_LoadByIndexWithNeighbors(startIdx);
        chart.Test_SetBoundsForTest(1200, 700);
        chart.Test_ReloadWindow(0);
        chart.Test_SetCenterToWindowMiddle();
        chart.Test_TickEdgeTimerOnce();

        int presses = 0;
        int switches = 0;
        int lastIdx = chart.Test_CurrentIdx;

        const int safetyMaxPresses = 200_000;
        var switchHistory = new List<int> { lastIdx };

        // Act
        while (chart.Test_CurrentIdx < targetIdx && presses < safetyMaxPresses)
        {
            // Même chemin que MainWindow.Click_Next -> loadNext().
            chart.loadNext();
            presses++;

            chart.Test_SetCenterToWindowMiddle();
            chart.Test_TickEdgeTimerOnce();

            if (chart.Test_CurrentIdx != lastIdx)
            {
                int previousIdx = lastIdx;
                lastIdx = chart.Test_CurrentIdx;
                switches++;
                switchHistory.Add(lastIdx);

                Debug.WriteLine($"[NEXT] switch {previousIdx} -> {lastIdx}, presses={presses}, switches={switches}");

                Assert.True(lastIdx > previousIdx,
                    $"La navigation doit être strictement croissante. previousIdx={previousIdx}, currentIdx={lastIdx}");
                Assert.Equal(previousIdx + 1, lastIdx);
            }
        }

        // Assert
        Assert.True(presses < safetyMaxPresses,
            $"Boucle de sécurité atteinte. history={string.Join(",", switchHistory)}");

        Assert.Equal(targetIdx, chart.Test_CurrentIdx);
        Assert.Equal(nbContractsToAdvance, switches);
        Assert.Equal(nbContractsToAdvance + 1, switchHistory.Count);
    }

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
}
