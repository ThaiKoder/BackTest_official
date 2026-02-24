using System.Diagnostics;



namespace BacktestApp.Controls;

internal static class DebugMessage
{
    private static bool show  = false;
    public static void Write(string message)
    {
#if DEBUG
        if (show) Debug.WriteLine(">>>>>>>>>> " + message);
#endif
    }
}