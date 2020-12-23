using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JpegTranscoderEncoder
{
    public class JpegCompressor
    {
        private BitOutputStream _outStream;

        private PpmCompressor _ppmCompressor;

        private static int[] YQT =
        {
            16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17,
            22, 29, 51, 87, 80, 62, 18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78,
            87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99
        };

        private static byte[] s_jo_ZigZag =
        {
            0, 1, 5, 6, 14, 15, 27, 28, 2, 4, 7, 13, 16, 26, 29, 42, 3, 8, 12, 17, 25, 30, 41, 43, 9, 11, 18, 24, 31,
            40, 44, 53, 10, 19, 23, 32, 39, 45, 52, 54, 20, 22, 33, 38, 46, 51, 55, 60, 21, 34, 37, 47, 50, 56, 59, 61,
            35, 36, 48, 49, 57, 58, 62, 63
        };

        public JpegCompressor(BitOutputStream outStream)
        {
            _outStream = outStream;
            _ppmCompressor = new PpmCompressor(outStream);
        }

        private static void jo_calcBits(int val, ushort[] bits)
        {
            int tmp1 = val < 0 ? -val : val;
            val = val < 0 ? val - 1 : val;
            bits[1] = 1;
            tmp1 >>= 1;
            while (tmp1 != 0)
            {
                ++bits[1];
                tmp1 >>= 1;
            }

            bits[0] = Convert.ToUInt16(val & ((1 << bits[1]) - 1));
        }

        public List<ElementWithLevel> CompressDcLevel(int[][] DU)
        {
            var dcLevel = new int[DU.Length];
            var prevDc = 0;
            for (var j = 0; j < DU.Length; j++)
            {
                dcLevel[j] = DU[j][0] - prevDc;
                prevDc = DU[j][0];
            }

            var DC = new List<ElementWithLevel>();
            var buf = 0;
            var count = 0;
            foreach (var t in dcLevel)
            {
                if (t == 0)
                {
                    buf = count == 0 ? 0 : buf << 4;
                    count++;
                    DC.Add(new ElementWithLevel(0, 0));
                }
                else
                {
                    ushort[] bits = new ushort[2];
                    jo_calcBits(t, bits);
                    buf = count == 0 ? bits[1] : (buf << 4) | bits[1];
                    count++;
                    DC.Add(new ElementWithLevel(bits[0], bits[1]));
                }

                if (count == 2)
                {
                    _ppmCompressor.Compress(buf);
                    count = 0;
                }
            }

            if (count == 1)
            {
                buf <<= 4;
                _ppmCompressor.Compress(buf);
            }

            _ppmCompressor.Compress(256);
            return DC;
        }

        public void CompressCoefficient(List<ElementWithLevel> element)
        {
            var i = 0;
            foreach (var e in element)
            {
                i++;
                if(i == 151)
                    Console.Write(" ");
                var b = new BitArray(new[] {e.Element});
                b.Cast<bool>().Select(bit => bit ? 1 : 0).Take(e.Level).Reverse().ToList().ForEach(bit => _outStream.Write(bit));
            }
        }

        public List<ElementWithLevel> CompressAcLevel(int[][] DU)
        {
            var ac = new List<ElementWithLevel>();
            for (var j = 0; j < DU.Length; j++)
            {
                // Encode ACs
                int end0pos = 63;
                for (; (end0pos > 0) && (DU[j][end0pos] == 0); --end0pos)
                {
                }

                // end0pos = first element in reverse order !=0
                if (end0pos == 0)
                {
                    _ppmCompressor.Compress(0);
                    continue;
                }

                for (int i = 1; i <= end0pos; ++i)
                {
                    int startpos = i;
                    for (; DU[j][i] == 0 && i <= end0pos; ++i)
                    {
                    }

                    int nrzeroes = i - startpos;
                    if (nrzeroes >= 16)
                    {
                        int lng = nrzeroes >> 4;
                        for (int nrmarker = 1; nrmarker <= lng; ++nrmarker)
                        {
                            _ppmCompressor.Compress(15 << 4);
                            ac.Add(new ElementWithLevel(0, 0));
                        }

                        nrzeroes &= 15;
                    }

                    ushort[] bits = new ushort[2];
                    jo_calcBits(DU[j][i], bits);
                    _ppmCompressor.Compress((nrzeroes << 4) | bits[1]);
                    ac.Add(new ElementWithLevel(bits[0], bits[1]));
                }

                if (end0pos != 63)
                {
                    _ppmCompressor.Compress(0);
                }
            }

            _ppmCompressor.Compress(256);
            return ac;
        }

        public void Compress(string jpegPath)
        {
            var nanoJpeg = new JpegDecoder();
            var bytes = File.ReadAllBytes(jpegPath);
            var tables = nanoJpeg.njDecode(bytes);

            var qFactor = 0;
            var bytes1 = nanoJpeg.nj.qtab[0];
            for (int i = 0; i < 64; ++i)
            {
                if (bytes1[s_jo_ZigZag[i]] != 1 && bytes1[s_jo_ZigZag[i]] != 255)
                {
                    qFactor = (int)Math.Ceiling((bytes1[s_jo_ZigZag[i]] * 100 - 50) * 1.0 / YQT[i]);
                }
            }

            if (qFactor == 0)
            {
                qFactor = bytes1[s_jo_ZigZag[0]] == 1 ? 100 : 1;
            }
            else
            {
                var q1 = 100 - (int)Math.Round(qFactor * 1.0 / 2);
                qFactor = q1 > 50 ? q1 : (int)Math.Round(5000.0 / qFactor);
            }

            var comp = nanoJpeg.nj.ncomp;
            var width = nanoJpeg.njGetWidth();
            var height = nanoJpeg.njGetHeight();

            Compress(tables, width, height, qFactor, comp);
        }

        public void Compress(int[][] DU, int width, int height, int qFactor, int comp)
        {
            _outStream.AddHeader(width, 2);
            _outStream.AddHeader(height, 2);
            _outStream.AddHeader(qFactor, 1);
            _outStream.AddHeader(comp , 1);

            var dcLevel = CompressDcLevel(DU);
            _ppmCompressor.Finish();
            _outStream.Flush();

            var header1 = _outStream.ByteCount;
            _outStream.AddHeader(header1, 4);

            _ppmCompressor = new PpmCompressor(_outStream);
            var acLevel = CompressAcLevel(DU);
            _ppmCompressor.Finish();
            _outStream.Flush();

            var header2 = _outStream.ByteCount - header1;
            _outStream.AddHeader(header2, 4);
            _outStream.SaveHeader();

            CompressCoefficient(dcLevel);
            CompressCoefficient(acLevel);
        }
    }
}