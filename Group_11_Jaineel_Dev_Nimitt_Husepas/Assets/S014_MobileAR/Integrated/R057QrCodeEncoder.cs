using System;
using System.Collections.Generic;
using System.Text;

namespace R057.Kiosk
{
    internal static class R057QrCodeEncoder
    {
        private readonly struct Block
        {
            public readonly int totalCodewords;
            public readonly int dataCodewords;

            public Block(int totalCodewords, int dataCodewords)
            {
                this.totalCodewords = totalCodewords;
                this.dataCodewords = dataCodewords;
            }
        }

        private static readonly int[][] AlignmentPositions =
        {
            new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 },
            new[] { 6, 30 }, new[] { 6, 34 }, new[] { 6, 22, 38 },
            new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
            new[] { 6, 30, 54 }, new[] { 6, 32, 58 }
        };

        private static readonly byte[] Exponent = new byte[512];
        private static readonly byte[] Logarithm = new byte[256];

        static R057QrCodeEncoder()
        {
            var value = 1;
            for (var i = 0; i < 255; i++)
            {
                Exponent[i] = (byte)value;
                Logarithm[value] = (byte)i;
                value <<= 1;
                if ((value & 0x100) != 0) value ^= 0x11D;
            }
            for (var i = 255; i < Exponent.Length; i++)
                Exponent[i] = Exponent[i - 255];
        }

        public static bool[,] Encode(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var bytes = Encoding.UTF8.GetBytes(text);
            var version = SelectVersion(bytes.Length);
            var codewords = EncodeCodewords(text);
            var size = 17 + version * 4;
            var modules = new bool[size, size];
            var function = new bool[size, size];

            DrawFinder(modules, function, 3, 3);
            DrawFinder(modules, function, size - 4, 3);
            DrawFinder(modules, function, 3, size - 4);
            DrawAlignmentPatterns(modules, function, version);
            DrawTimingPatterns(modules, function);
            if (version >= 7) DrawVersion(modules, function, version);
            DrawFormat(modules, function, 0);

            var bestMask = 0;
            var bestPenalty = int.MaxValue;
            for (var mask = 0; mask < 8; mask++)
            {
                var candidate = (bool[,])modules.Clone();
                DrawCodewords(candidate, function, codewords, mask);
                DrawFormat(candidate, function, mask);
                var penalty = GetPenalty(candidate);
                if (penalty >= bestPenalty) continue;
                bestPenalty = penalty;
                bestMask = mask;
            }

            DrawCodewords(modules, function, codewords, bestMask);
            DrawFormat(modules, function, bestMask);
            return modules;
        }

        internal static byte[] EncodeCodewords(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var input = Encoding.UTF8.GetBytes(text);
            var version = SelectVersion(input.Length);
            var blocks = GetBlocks(version);
            var dataCodewordCount = 0;
            foreach (var block in blocks) dataCodewordCount += block.dataCodewords;
            return AddErrorCorrection(EncodeData(input, version, dataCodewordCount), blocks);
        }

        private static int SelectVersion(int byteCount)
        {
            for (var version = 1; version <= 12; version++)
            {
                var dataCodewords = 0;
                foreach (var block in GetBlocks(version)) dataCodewords += block.dataCodewords;
                var countBits = version <= 9 ? 8 : 16;
                if (4 + countBits + byteCount * 8 <= dataCodewords * 8)
                    return version;
            }
            throw new ArgumentException("The URL is too long for the built-in QR encoder (maximum version 12-L).", nameof(byteCount));
        }

        private static Block[] GetBlocks(int version)
        {
            switch (version)
            {
                case 1: return new[] { new Block(26, 19) };
                case 2: return new[] { new Block(44, 34) };
                case 3: return new[] { new Block(70, 55) };
                case 4: return new[] { new Block(100, 80) };
                case 5: return new[] { new Block(134, 108) };
                case 6: return new[] { new Block(86, 68), new Block(86, 68) };
                case 7: return new[] { new Block(98, 78), new Block(98, 78) };
                case 8: return new[] { new Block(121, 97), new Block(121, 97) };
                case 9: return new[] { new Block(146, 116), new Block(146, 116) };
                case 10: return new[] { new Block(86, 68), new Block(86, 68), new Block(87, 69), new Block(87, 69) };
                case 11: return new[] { new Block(101, 81), new Block(101, 81), new Block(101, 81), new Block(101, 81) };
                case 12: return new[] { new Block(116, 92), new Block(116, 92), new Block(117, 93), new Block(117, 93) };
                default: throw new ArgumentOutOfRangeException(nameof(version));
            }
        }

        private static byte[] EncodeData(byte[] input, int version, int dataCodewordCount)
        {
            var bits = new List<bool>();
            AppendBits(bits, 0x4, 4);
            AppendBits(bits, input.Length, version <= 9 ? 8 : 16);
            foreach (var value in input) AppendBits(bits, value, 8);
            var capacity = dataCodewordCount * 8;
            for (var i = 0; i < Math.Min(4, capacity - bits.Count); i++) bits.Add(false);
            while (bits.Count % 8 != 0) bits.Add(false);

            var result = new List<byte>(dataCodewordCount);
            for (var i = 0; i < bits.Count; i += 8)
            {
                var value = 0;
                for (var j = 0; j < 8; j++) value = (value << 1) | (bits[i + j] ? 1 : 0);
                result.Add((byte)value);
            }
            var pad = true;
            while (result.Count < dataCodewordCount)
            {
                result.Add(pad ? (byte)0xEC : (byte)0x11);
                pad = !pad;
            }
            return result.ToArray();
        }

        private static void AppendBits(List<bool> bits, int value, int count)
        {
            for (var i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        private static byte[] AddErrorCorrection(byte[] data, Block[] blocks)
        {
            var dataBlocks = new List<byte[]>();
            var errorBlocks = new List<byte[]>();
            var offset = 0;
            var errorCodewordsPerBlock = blocks[0].totalCodewords - blocks[0].dataCodewords;
            foreach (var block in blocks)
            {
                var blockData = new byte[block.dataCodewords];
                Array.Copy(data, offset, blockData, 0, block.dataCodewords);
                offset += block.dataCodewords;
                dataBlocks.Add(blockData);
                errorBlocks.Add(CalculateErrorCorrection(blockData, errorCodewordsPerBlock));
            }

            var result = new List<byte>();
            var maxDataCodewords = 0;
            foreach (var block in dataBlocks) maxDataCodewords = Math.Max(maxDataCodewords, block.Length);
            for (var i = 0; i < maxDataCodewords; i++)
                foreach (var block in dataBlocks)
                    if (i < block.Length) result.Add(block[i]);
            for (var i = 0; i < errorCodewordsPerBlock; i++)
                foreach (var block in errorBlocks) result.Add(block[i]);
            return result.ToArray();
        }

        private static byte[] CalculateErrorCorrection(byte[] data, int degree)
        {
            var generator = new byte[] { 1 };
            for (var i = 0; i < degree; i++)
            {
                var next = new byte[generator.Length + 1];
                for (var j = 0; j < generator.Length; j++)
                {
                    next[j] ^= generator[j];
                    next[j + 1] ^= Multiply(generator[j], Exponent[i]);
                }
                generator = next;
            }

            var remainder = new byte[degree];
            foreach (var value in data)
            {
                var factor = (byte)(value ^ remainder[0]);
                for (var i = 0; i < degree - 1; i++)
                    remainder[i] = (byte)(remainder[i + 1] ^ Multiply(generator[i + 1], factor));
                remainder[degree - 1] = Multiply(generator[degree], factor);
            }
            return remainder;
        }

        private static byte Multiply(byte x, byte y)
        {
            if (x == 0 || y == 0) return 0;
            return Exponent[Logarithm[x] + Logarithm[y]];
        }

        private static void DrawFinder(bool[,] modules, bool[,] function, int centerX, int centerY)
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var x = centerX + dx;
                    var y = centerY + dy;
                    if (x < 0 || y < 0 || x >= modules.GetLength(0) || y >= modules.GetLength(1)) continue;
                    var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    modules[y, x] = distance != 2 && distance != 4;
                    function[y, x] = true;
                }
            }
        }

        private static void DrawTimingPatterns(bool[,] modules, bool[,] function)
        {
            var size = modules.GetLength(0);
            for (var i = 0; i < size; i++)
            {
                if (!function[6, i])
                {
                    modules[6, i] = i % 2 == 0;
                    function[6, i] = true;
                }
                if (!function[i, 6])
                {
                    modules[i, 6] = i % 2 == 0;
                    function[i, 6] = true;
                }
            }
        }

        private static void DrawAlignmentPatterns(bool[,] modules, bool[,] function, int version)
        {
            var positions = AlignmentPositions[version - 1];
            foreach (var centerY in positions)
            {
                foreach (var centerX in positions)
                {
                    if (function[centerY, centerX]) continue;
                    for (var dy = -2; dy <= 2; dy++)
                    {
                        for (var dx = -2; dx <= 2; dx++)
                        {
                            var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                            modules[centerY + dy, centerX + dx] = distance != 1;
                            function[centerY + dy, centerX + dx] = true;
                        }
                    }
                }
            }
        }

        private static void DrawVersion(bool[,] modules, bool[,] function, int version)
        {
            var remainder = version << 12;
            for (var i = 17; i >= 12; i--)
                if (((remainder >> i) & 1) != 0) remainder ^= 0x1F25 << (i - 12);
            var bits = (version << 12) | remainder;
            var size = modules.GetLength(0);
            for (var i = 0; i < 18; i++)
            {
                var value = ((bits >> i) & 1) != 0;
                var a = size - 11 + i % 3;
                var b = i / 3;
                modules[b, a] = value;
                modules[a, b] = value;
                function[b, a] = true;
                function[a, b] = true;
            }
        }

        private static void DrawFormat(bool[,] modules, bool[,] function, int mask)
        {
            const int errorCorrectionLevelL = 1;
            var data = (errorCorrectionLevelL << 3) | mask;
            var remainder = data << 10;
            for (var i = 14; i >= 10; i--)
                if (((remainder >> i) & 1) != 0) remainder ^= 0x537 << (i - 10);
            var bits = ((data << 10) | remainder) ^ 0x5412;
            var size = modules.GetLength(0);
            for (var i = 0; i <= 5; i++) SetFunction(modules, function, 8, i, ((bits >> i) & 1) != 0);
            SetFunction(modules, function, 8, 7, ((bits >> 6) & 1) != 0);
            SetFunction(modules, function, 8, 8, ((bits >> 7) & 1) != 0);
            SetFunction(modules, function, 7, 8, ((bits >> 8) & 1) != 0);
            for (var i = 9; i < 15; i++) SetFunction(modules, function, 14 - i, 8, ((bits >> i) & 1) != 0);
            for (var i = 0; i < 8; i++) SetFunction(modules, function, size - 1 - i, 8, ((bits >> i) & 1) != 0);
            for (var i = 8; i < 15; i++) SetFunction(modules, function, 8, size - 15 + i, ((bits >> i) & 1) != 0);
            SetFunction(modules, function, 8, size - 8, true);
        }

        private static void SetFunction(bool[,] modules, bool[,] function, int x, int y, bool value)
        {
            modules[y, x] = value;
            function[y, x] = true;
        }

        private static void DrawCodewords(bool[,] modules, bool[,] function, byte[] codewords, int mask)
        {
            var size = modules.GetLength(0);
            var bitIndex = 0;
            for (var right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (var vertical = 0; vertical < size; vertical++)
                {
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? size - 1 - vertical : vertical;
                    for (var offset = 0; offset < 2; offset++)
                    {
                        var x = right - offset;
                        if (function[y, x]) continue;
                        var value = bitIndex < codewords.Length * 8 &&
                                    ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                        if (Mask(mask, x, y)) value = !value;
                        modules[y, x] = value;
                        bitIndex++;
                    }
                }
            }
        }

        private static bool Mask(int mask, int x, int y)
        {
            switch (mask)
            {
                case 0: return (x + y) % 2 == 0;
                case 1: return y % 2 == 0;
                case 2: return x % 3 == 0;
                case 3: return (x + y) % 3 == 0;
                case 4: return (x / 3 + y / 2) % 2 == 0;
                case 5: return x * y % 2 + x * y % 3 == 0;
                case 6: return (x * y % 2 + x * y % 3) % 2 == 0;
                case 7: return ((x + y) % 2 + x * y % 3) % 2 == 0;
                default: throw new ArgumentOutOfRangeException(nameof(mask));
            }
        }

        private static int GetPenalty(bool[,] modules)
        {
            var size = modules.GetLength(0);
            var score = 0;
            var darkCount = 0;
            for (var y = 0; y < size; y++)
            {
                var runColor = modules[y, 0];
                var runLength = 1;
                for (var x = 0; x < size; x++)
                {
                    if (modules[y, x]) darkCount++;
                    if (x == 0) continue;
                    if (modules[y, x] == runColor) runLength++;
                    else
                    {
                        if (runLength >= 5) score += 3 + runLength - 5;
                        runColor = modules[y, x];
                        runLength = 1;
                    }
                }
                if (runLength >= 5) score += 3 + runLength - 5;
            }

            for (var x = 0; x < size; x++)
            {
                var runColor = modules[0, x];
                var runLength = 1;
                for (var y = 1; y < size; y++)
                {
                    if (modules[y, x] == runColor) runLength++;
                    else
                    {
                        if (runLength >= 5) score += 3 + runLength - 5;
                        runColor = modules[y, x];
                        runLength = 1;
                    }
                }
                if (runLength >= 5) score += 3 + runLength - 5;
            }

            for (var y = 0; y < size - 1; y++)
                for (var x = 0; x < size - 1; x++)
                    if (modules[y, x] == modules[y, x + 1] &&
                        modules[y, x] == modules[y + 1, x] &&
                        modules[y, x] == modules[y + 1, x + 1])
                        score += 3;

            for (var y = 0; y < size; y++)
                for (var x = 0; x <= size - 11; x++)
                    if (MatchesFinderPenalty(modules, x, y, true)) score += 40;
            for (var x = 0; x < size; x++)
                for (var y = 0; y <= size - 11; y++)
                    if (MatchesFinderPenalty(modules, y, x, false)) score += 40;

            var balance = Math.Abs(darkCount * 20 - size * size * 10) / (size * size);
            score += balance * 10;
            return score;
        }

        private static bool MatchesFinderPenalty(bool[,] modules, int start, int fixedCoordinate, bool horizontal)
        {
            const int firstPattern = 0x5D0;
            const int secondPattern = 0x05D;
            var value = 0;
            for (var i = 0; i < 11; i++)
            {
                var bit = horizontal ? modules[fixedCoordinate, start + i] : modules[start + i, fixedCoordinate];
                value = (value << 1) | (bit ? 1 : 0);
            }
            return value == firstPattern || value == secondPattern;
        }
    }
}
