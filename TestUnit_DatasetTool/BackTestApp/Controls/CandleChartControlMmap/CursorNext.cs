using System;
using System.Collections.Generic;
using Xunit;
using BacktestApp.Controls;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlMmap;

public class CursorNext
{
    [Fact]
    public void Next_Should_Advance_Across_10_Contracts_Using_Real_DataBin()
    {
        // Arrange
        var chart = new CandleChartControl();

        // Charge l'index réel: data/bin/_index.bin
        chart.Test_LoadIndex();

        // On démarre sur l'index 0 (ou un autre si tu veux)
        chart.Test_LoadByIndexWithNeighbors(0);

        // Act: on veut faire 9 "next" pour passer de 0 -> 9
        for (int step = 0; step < 9; step++)
        {
            // Force "on est au bout du fichier" pour déclencher le switch vers fichier suivant.
            // maxStart = fileCount - WindowCount (si fileCount < WindowCount => 0)
            long maxStart = Math.Max(0, chart.Test_FileCount - chart.Test_WindowCount);

            // Recharge la fenêtre tout au bout => IsAtEndOfFile() devient vrai
            chart.Test_ReloadWindow(maxStart);

            // Appel réel
            chart.CursorNext();

            // Assert progress immédiat (doit avancer d'un contrat)
            Assert.Equal(step + 1, chart.Test_CurrentIdx);
        }

        // Final assert: on doit être sur le 10e contrat (index 9)
        Assert.Equal(9, chart.Test_CurrentIdx);
    }

    [Fact]
    public void Next_On_Last_Contract_Should_Not_Go_Past_End()
    {
        int nbContractsToTest = 3;
        var chart = new CandleChartControl();
        chart.Test_LoadIndex();

        // On se met sur le dernier contrat
        // Comme on n'a pas l'accès direct à _starts.Length, on fait simple :
        // on avance jusqu'à 9 (si ton dataset a au moins 10 contrats)
        chart.Test_LoadByIndexWithNeighbors(0);
        for (int i = 0; i < nbContractsToTest; i++)
        {
            long maxStart = Math.Max(0, chart.Test_FileCount - chart.Test_WindowCount);
            chart.Test_ReloadWindow(maxStart);
            chart.CursorNext();
        }
        Assert.Equal(nbContractsToTest, chart.Test_CurrentIdx);

        // On force encore "fin de fichier" et on tente Next
        long maxStartLast = Math.Max(0, chart.Test_FileCount - chart.Test_WindowCount);
        chart.Test_ReloadWindow(maxStartLast);
        chart.CursorNext();

        Assert.Equal(nbContractsToTest, chart.Test_CurrentIdx);
    }


    [Fact]
    public void Next_On_Real_Last_Contract_Should_Not_Go_Past_End()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_LoadIndex();

        int count = chart.Test_ContractsCount;
        Assert.True(count > 0, "Index vide: _starts.Length == 0");

        int lastIdx = count - 1;

        // on charge le dernier contrat réel
        chart.Test_LoadByIndexWithNeighbors(lastIdx);

        // force fin de fichier pour déclencher le switch
        long maxStart = System.Math.Max(0, chart.Test_FileCount - chart.Test_WindowCount);
        chart.Test_ReloadWindow(maxStart);

        chart.CursorNext();

        // doit rester sur le dernier
        Assert.Equal(lastIdx, chart.Test_CurrentIdx);
    }

    [Fact]
    public void Next_Should_Advance_By_10_Contracts_Even_If_Cache_Is_5()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        chart.Test_LoadIndex();
        int count = chart.Test_ContractsCount;

        Assert.True(count >= 11, $"Pas assez de contrats pour avancer de 10. count={count}");

        // Start at 0
        chart.Test_LoadByIndexWithNeighbors(0);

        for (int step = 0; step < 10; step++)
        {
            // Force "fin de fichier" sur le contrat courant
            long maxStart = Math.Max(0, chart.Test_FileCount - chart.Test_WindowCount);
            chart.Test_ReloadWindow(maxStart);

            chart.CursorNext();

            // On doit être passé au contrat suivant
            Assert.Equal(step + 1, chart.Test_CurrentIdx);
        }
    }
}