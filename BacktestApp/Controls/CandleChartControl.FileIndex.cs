using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



namespace BacktestApp.Controls;


public sealed partial class CandleChartControl
{
    public sealed class FileIndex : IDisposable
    {
        public const int IndexSize = 8;

        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private long _fileSize;
        private long _count;

        // Buffer réutilisé pour éviter les allocations
        private readonly byte[] _buffer = new byte[IndexSize];

        public long Count => _count;


        //Constructor
        public FileIndex()
        {
            Debug.WriteLine("IndexReader Constructor");
        }

        public void Load(string fileNamePath)
        {
            Dispose();

            var info = new FileInfo(fileNamePath);
            _fileSize = info.Length;

            if (_fileSize % IndexSize != 0)
                throw new InvalidDataException($"Le fichier ne contient pas des records de {IndexSize} bytes.");

            _count = _fileSize / IndexSize;

            _mmf = MemoryMappedFile.CreateFromFile(
                fileNamePath,
                FileMode.Open,
                null,
                0,
                MemoryMappedFileAccess.Read);

            _accessor = _mmf.CreateViewAccessor(
                0,
                _fileSize,
                MemoryMappedFileAccess.Read);
        }

        public (uint StartYmd, uint EndYmd) Read(long index)
        {
            if (_accessor == null)
                throw new InvalidOperationException("Index non chargé.");

            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));

            long offset = index * IndexSize;

            uint startYmd = _accessor.ReadUInt32(offset);
            uint endYmd = _accessor.ReadUInt32(offset + 4);

            return (startYmd, endYmd);
        }


        public void Dispose()
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
            _accessor = null;
            _mmf = null;
            _fileSize = 0;
            _count = 0;
        }
    }

}
