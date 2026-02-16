using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Tmds.DBus.Protocol;
using BacktestApp.Controls;

namespace BacktestApp.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
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

        public void ClickHandler(object sender, RoutedEventArgs args)
        {
            var Text = "Button clicked!";
            DebugMessage.Write(Text);
        }
        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}