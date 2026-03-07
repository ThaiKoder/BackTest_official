using System;
using System.Collections.Generic;
using Xunit;
using BacktestApp.Controls;
using System.Diagnostics;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlMmap;

public class CursorNext
{
    [Fact]
    public void Next_Should_Advance_Across_10_Contracts_With_Many_Presses()
    {
        int nbFile = 800; 
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_LoadIndex();
        int count = chart.Test_ContractsCount;
        Assert.True(count >= 11, $"Pas assez de contrats pour avancer de 10. count={count}");

        // Start contract
        int startIdx = 500;
        chart.Test_LoadByIndexWithNeighbors(startIdx);

        // Place window at start and center properly
        chart.Test_ReloadWindow(0);
        chart.Test_SetCenterToWindowMiddle();

        int targetIdx = startIdx + nbFile;

        int presses = 0;
        int switches = 0;

        // Sécurité : si chaque contrat nécessite beaucoup de pressions,
        // on laisse large mais on évite boucle infinie
        int safetyMaxPresses = 200_000;

        int lastContractIdx = chart.Test_CurrentIdx;

        while (chart.Test_CurrentIdx < targetIdx)
        {
            chart.CursorNext();
            presses++;

            // Après un ReloadWindow/LoadWindow, le centre doit être cohérent
            //chart.Test_SetCenterToWindowMiddle();

            // Compte les changements de contrat
            if (chart.Test_CurrentIdx != lastContractIdx)
            {
                switches++;
                lastContractIdx = chart.Test_CurrentIdx;

            }

            if (lastContractIdx == 100) Debug.WriteLine($"Debug: contractIdx={lastContractIdx}, presses={presses}, switches={switches}");
            if (lastContractIdx == 200) Debug.WriteLine($"Debug: contractIdx={lastContractIdx}, presses={presses}, switches={switches}");
            if (lastContractIdx == 300) Debug.WriteLine($"Debug: contractIdx={lastContractIdx}, presses={presses}, switches={switches}");
            if (lastContractIdx == 400) Debug.WriteLine($"Debug: contractIdx={lastContractIdx}, presses={presses}, switches={switches}");
            if (lastContractIdx == 500) Debug.WriteLine($"Debug: contractIdx={lastContractIdx}, presses={presses}, switches={switches}");
            if (lastContractIdx == 520) Debug.WriteLine($"Debug: contractIdx={lastContractIdx}, presses={presses}, switches={switches}");
            if (lastContractIdx == 524)
            {
                Debug.WriteLine($"Debug: contractIdx={lastContractIdx}, presses={presses}, switches={switches}");
            }
            if (lastContractIdx == 525)
            {
                Debug.WriteLine($"Debug: contractIdx={lastContractIdx}, presses={presses}, switches={switches}");
            }

            if (lastContractIdx == 527)
            {
                Debug.WriteLine($"Debug: contractIdx={lastContractIdx}, presses={presses}, switches={switches}");
            }

        }


        Assert.Equal(targetIdx, chart.Test_CurrentIdx);
        Assert.Equal(nbFile, switches); // on doit avoir switché 10 fois de contrat
    }

    [Fact]
    public void CursorNext_Should_Switch_Contracts_Monotonically_With_UI_Like_EdgeTimer()
    {
        // Arrange
        var chart = new BacktestApp.Controls.CandleChartControl();

        chart.Test_LoadIndex();
        int contracts = chart.Test_ContractsCount;
        Assert.True(contracts > 10, $"Pas assez de contrats. contracts={contracts}");

        int startIdx = 0;
        int nbContractsToAdvance = 800; // comme toi
        int targetIdx = startIdx + nbContractsToAdvance;

        Assert.True(targetIdx < contracts,
            $"Target hors range. targetIdx={targetIdx} contracts={contracts}");

        // Start same as UI (tu peux choisir LoadContractIndex ou LoadByIndexWithNeighbors)
        // Ici je prends ByIndexWithNeighbors car c'est ton chemin de CursorNext.
        chart.Test_LoadByIndexWithNeighbors(startIdx);

        // Simule une surface visible (Bounds) + un "plot rect"
        chart.Test_SetBoundsForTest(1200, 700);

        // Place la fenêtre de départ (comme UI: goToStart false = fin du fichier ; mais ici tu veux être stable)
        chart.Test_ReloadWindow(0);
        chart.Test_SetCenterToWindowMiddle();

        // Tick initial (comme si timer tournait)
        chart.Test_TickEdgeTimerOnce();

        int presses = 0;
        int switches = 0;

        // Sécurité globale
        int safetyMaxPresses = 200_000;

        // Sécurité anti-boucle "un seul contrat" (ex: 525)
        int maxPressesWithoutSwitch = 50_000;
        int pressesSinceLastSwitch = 0;

        int lastIdx = chart.Test_CurrentIdx;

        // Pour diagnostiquer si on repasse toujours par le même idx
        var switchHistory = new List<int>(capacity: 2000);
        switchHistory.Add(lastIdx);

        // Act
        while (chart.Test_CurrentIdx < targetIdx)
        {
            chart.CursorNext();
            presses++;
            pressesSinceLastSwitch++;

            // En UI, le centre bouge selon l'interaction; en test, on stabilise
            // (si tu veux coller encore plus à l'UI, tu peux commenter cette ligne)
            chart.Test_SetCenterToWindowMiddle();

            if (chart.Test_CurrentIdx != lastIdx)
            {
                switches++;
                lastIdx = chart.Test_CurrentIdx;
                pressesSinceLastSwitch = 0;
                switchHistory.Add(lastIdx);

                if (lastIdx == 100 || lastIdx == 200 || lastIdx == 300 || lastIdx == 400 ||
                    lastIdx == 500 || lastIdx == 520 || lastIdx == 525 || lastIdx == 526 ||
                    lastIdx == 527)
                {
                    Debug.WriteLine($"Switch -> contractIdx={lastIdx}, presses={presses}, switches={switches}");
                }
            }


        }

        // Assert
        Assert.Equal(targetIdx, chart.Test_CurrentIdx);
        Assert.Equal(nbContractsToAdvance, switches);
    }

    private static IEnumerable<int> Tail(List<int> list, int n)
    {
        int start = Math.Max(0, list.Count - n);
        for (int i = start; i < list.Count; i++)
            yield return list[i];
    }

}