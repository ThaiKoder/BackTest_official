using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DatasetToolTest.BackTestApp.Controls.CandleChartControl.FileIndex.IntegrityCheck;

public class FileIndexIntegrity
{
    private const string IndexPath = "data/bin/_index.bin";
    private const string BinFolder = "data/bin";
    private const int IndexRecordSize = 8;
    private const int ExpectedFileCount = 817;

    private readonly record struct IndexRecord(int Idx, uint StartYmd, uint EndYmd);

    private static List<IndexRecord> ReadIndexRaw()
    {
        Assert.True(File.Exists(IndexPath), $"Index introuvable: {IndexPath}");

        byte[] bytes = File.ReadAllBytes(IndexPath);

        Assert.True(
            bytes.Length > 0,
            $"L'index ne doit pas être vide: {IndexPath}");

        Assert.True(
            bytes.Length % IndexRecordSize == 0,
            $"La taille de l'index doit être multiple de {IndexRecordSize}. size={bytes.Length}");

        var records = new List<IndexRecord>(bytes.Length / IndexRecordSize);

        for (int offset = 0, idx = 0; offset < bytes.Length; offset += IndexRecordSize, idx++)
        {
            uint startYmd = BitConverter.ToUInt32(bytes, offset);
            uint endYmd = BitConverter.ToUInt32(bytes, offset + 4);

            records.Add(new IndexRecord(idx, startYmd, endYmd));
        }

        return records;
    }

    private static string BuildCandleFilePath(IndexRecord record)
        => Path.Combine(
            BinFolder,
            $"glbx-mdp3-{record.StartYmd}-{record.EndYmd}.ohlcv-1m.bin");

    [Fact]
    public void Index_File_Should_Have_Expected_Quantity()
    {
        var records = ReadIndexRaw();

        Assert.Equal(ExpectedFileCount, records.Count);
    }

    [Fact]
    public void Index_File_Should_Not_Have_Duplicate_Files()
    {
        var records = ReadIndexRaw();

        var uniqueStarts = new HashSet<uint>();
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            Assert.True(
                uniqueStarts.Add(record.StartYmd),
                $"Doublon StartYmd détecté à idx={record.Idx}: {record.StartYmd}");

            string fileName = Path.GetFileName(BuildCandleFilePath(record));

            Assert.True(
                uniqueNames.Add(fileName),
                $"Doublon fichier détecté à idx={record.Idx}: {fileName}");
        }
    }

    [Fact]
    public void Index_File_Should_Be_Strictly_Chronological()
    {
        var records = ReadIndexRaw();

        for (int i = 1; i < records.Count; i++)
        {
            Assert.True(
                records[i].StartYmd > records[i - 1].StartYmd,
                $"Index non chronologique à i={i}. prev={records[i - 1].StartYmd}, cur={records[i].StartYmd}");
        }
    }

    [Fact]
    public void All_Index_Files_Should_Exist()
    {
        var records = ReadIndexRaw();

        foreach (var record in records)
        {
            string path = BuildCandleFilePath(record);

            Assert.True(
                File.Exists(path),
                $"Fichier candle introuvable pour idx={record.Idx}: {path}");
        }
    }

    [Fact]
    public void Full_FileIndex_Integrity_Should_Be_Valid()
    {
        var records = ReadIndexRaw();

        Assert.Equal(ExpectedFileCount, records.Count);

        var uniqueStarts = new HashSet<uint>();
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        uint previousStart = 0;

        foreach (var record in records)
        {
            Assert.True(
                record.StartYmd > previousStart,
                $"Index non chronologique à idx={record.Idx}. prev={previousStart}, cur={record.StartYmd}");

            Assert.True(
                uniqueStarts.Add(record.StartYmd),
                $"Doublon StartYmd détecté à idx={record.Idx}: {record.StartYmd}");

            string path = BuildCandleFilePath(record);
            string fileName = Path.GetFileName(path);

            Assert.True(
                uniqueNames.Add(fileName),
                $"Doublon fichier détecté à idx={record.Idx}: {fileName}");

            Assert.True(
                File.Exists(path),
                $"Fichier candle introuvable pour idx={record.Idx}: {path}");

            previousStart = record.StartYmd;
        }

        Assert.Equal(ExpectedFileCount, uniqueStarts.Count);
        Assert.Equal(ExpectedFileCount, uniqueNames.Count);
    }
}