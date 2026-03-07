using System;
using System.Collections.Generic;
using Xunit;
using BacktestApp.Controls;


namespace DatasetToolTest.BackTestApp.Controls.IndexReader
{

    public class LoadTest
    {
        [Fact]
        public void Test1_indexReaderConstructor()
        {
            var chart = new global::BacktestApp.Controls.CandleChartControl();

            var reader = chart.Test_indexReader();
            Assert.NotNull(reader);
        }
    }
}
