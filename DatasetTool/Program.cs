using System;
using System.Threading.Tasks;
using DatasetTool;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("JSON → BIN conversion started");

        await JsonToBinaryConverter.ConvertJsonAsync(
            jsonPath: "data/glbx-mdp3-20100606-20100612.ohlcv-1m.json",
            binPath: "data/glbx-mdp3-20100606-20100612.ohlcv-1m.bin"
        );

        Console.WriteLine("Done.");
    }
}