using System.Diagnostics;

namespace BacktestApp.Controls;

internal static class DebugMessage
{
    public static void Write(string message)
    {
#if DEBUG
        Debug.WriteLine(">>>>>>>>>> " + message);
#endif
    }
}