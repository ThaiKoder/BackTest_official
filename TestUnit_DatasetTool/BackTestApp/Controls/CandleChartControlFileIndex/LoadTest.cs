using System;
using System.Collections.Generic;
using Xunit;
using BacktestApp.Controls;


namespace DatasetToolTest.BackTestApp.Controls.CandleChartControlFileIndex;


public class LoadTest
{
    [Fact]
    public void Test1_indexReaderConstructor()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        var reader = chart.Test_indexReader();
        Assert.NotNull(reader);
    }

    [Fact]
    public void Test2_loadValidFile()
    {
        var chart = new global::BacktestApp.Controls.CandleChartControl();

        try
        {
            chart.Test_LoadIndexFile("data/bin/_index.bin");
            Assert.True(true, "Le fichier index doit contenir au moins un record.");
        }
        catch (Exception ex)
        {
            Assert.True(false, $"Le chargement du fichier index a échoué: {ex.Message}");
        }
    }

}
