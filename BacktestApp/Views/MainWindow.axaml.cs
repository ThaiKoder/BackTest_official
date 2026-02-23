using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BacktestApp.Controls;
using DatasetTool;
using System;
using Tmds.DBus.Protocol;

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

            uint targetYmd = 20090610;

            int idx = _chart.FindFileIndex(targetYmd);

            _chart.OpenBinByIndex(idx);
        }


        public void showGraph(object sender, RoutedEventArgs args)
        {
            if (_chart == null)
            {
                DebugMessage.Write("Chart pas encore attaché (dock/onglet pas actif / recréation)");
                return;
            }

            uint targetYmd = 20090610;

            int idx = _chart.FindFileIndex(targetYmd);

            var result = _chart.OpenBinByIndex(idx);
            if (result != null)
            {
                var (start, end) = result.Value;
                DebugMessage.Write($"glbx-mdp3-{start}-{end}.ohlcv-1m.bin");
            }


            
            //LoadBinFile(filePath);
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