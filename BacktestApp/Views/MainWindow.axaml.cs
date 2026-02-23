using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BacktestApp.Controls;
using DatasetTool;
using System;
using System.Diagnostics;
using Tmds.DBus.Protocol;
using System.IO;

namespace BacktestApp.Views
{
    public partial class MainWindow : Window
    {
        private CandleChartControl? _chart;
        public MainWindow()
        {
            InitializeComponent();

        }


        private void Chart_Attached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _chart = (CandleChartControl)sender!;
            _chart.loadIndex();
            DebugMessage.Write($"Attached: Name = {_chart.Name}");
        }


        private void Chart_Detached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (ReferenceEquals(_chart, sender)) _chart = null;
            DebugMessage.Write("Chart detached");
        }


        public void Click_Previous(object sender, RoutedEventArgs args)
        {
            if (_chart == null)
            {
                DebugMessage.Write("Chart pas encore attaché (dock/onglet pas actif / recréation)");
                return;
            }

            _chart.loadPrevious();
        }


        public void Click_Next(object sender, RoutedEventArgs args)
        {
            if (_chart == null)
            {
                DebugMessage.Write("Chart pas encore attaché (dock/onglet pas actif / recréation)");
                return;
            }

            _chart.loadNext();
        }


        public void jumpToDate(object sender, RoutedEventArgs args)
        {
            DebugMessage.Write("jumpToDate clicked");

            if (_chart == null)
            {
                DebugMessage.Write("Chart pas encore attaché (dock/onglet pas actif / recréation)");
                return;
            }

            //uint targetYmd = 20090610;
            uint targetYmd = 20100620;

            int idx = _chart.FindFileIndex(targetYmd);





            var current = _chart.OpenBinByIndex(idx);
            if (current != null)
            {

                // Deux suivant et précédents
                var (l1, l2, cur, r1, r2) = _chart.GetNeighborIndexes(idx);


                //cur
                uint start = _chart.getStart(cur);
                uint end = _chart.getEnd(cur);

                string filePath1 = Path.Combine("data", "bin",
                    $"glbx-mdp3-{start}-{end}.ohlcv-1m.bin");

                DebugMessage.Write(filePath1);


                //l2
                if (l2 > -1)
                {
                    uint start2 = _chart.getStart(l2);
                    uint end2 = _chart.getEnd(l2);
                    string filePath2 = Path.Combine("data", "bin", $"glbx-mdp3-{start2}-{end2}.ohlcv-1m.bin");
                    DebugMessage.Write(filePath2);
                }




                // Charger le fichier ciblé
                string filePath = Path.Combine("data", "bin", $"glbx-mdp3-{start}-{end}.ohlcv-1m.bin");
                _chart.LoadBinFile(filePath);


                    
            }

        }


        private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            BeginMoveDrag(e);
        }


        private void Minimize_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }


        private void Maximize_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }


        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}