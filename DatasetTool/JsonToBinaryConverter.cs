using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DatasetTool
{
    public readonly record struct Candle1m(long TsNs, long O, long H, long L, long C, long V);

 
}