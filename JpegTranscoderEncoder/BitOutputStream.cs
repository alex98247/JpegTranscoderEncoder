using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JpegTranscoderEncoder
{
    public class BitOutputStream
    {
        private readonly Stream _output;

        private int _currentByte;

        private int _numBitsFilled;

        private List<byte> buffer;

        private List<Tuple<int, int>> header = new List<Tuple<int, int>>();

        public int ByteCount = 0;

        public BitOutputStream(Stream outStream)
        {
            buffer = new List<byte>();
            this._output = outStream;
            _currentByte = 0;
            _numBitsFilled = 0;
        }

        public void Write(int b)
        {
            if (b != 0 && b != 1)
                throw new ArgumentException("Argument must be 0 or 1");
            _currentByte = (_currentByte << 1) | b;
            _numBitsFilled++;
            if (_numBitsFilled == 8)
            {
                buffer.Add((byte) _currentByte);
                ByteCount++;
                _currentByte = 0;
                _numBitsFilled = 0;
            }
        }

        public void SaveHeader()
        {
            foreach (var h in header)
            {
                var hBytes = BitConverter.GetBytes(h.Item1).Take(h.Item2).Reverse().ToArray();
                _output.Write(hBytes, 0, h.Item2);
            }
        }

        public void AddHeader(int header, int byteCount) => this.header.Add(Tuple.Create(header, byteCount));

        public void Flush()
        {
            while (_numBitsFilled != 0)
                Write(0);
        }

        public void Close()
        {
            Flush();

            _output.Write(buffer.ToArray(), 0, buffer.Count);
            _output.Close();
        }
    }
}