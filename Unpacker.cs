using System;
using System.IO;
using UnpackMiColorFace.Decompiler;
using XiaomiWatch.Common;

namespace UnpackMiColorFace
{
    internal class Unpacker
    {
        private const uint magic_v1_1 = 0x46616365;
        private const uint magic_v1_2 = 0x46696C65;

        private const uint magic_v2 = 0x5AA53412;

        private const uint magic_v3_1 = 0x00000607;
        private const uint magic_v3_2 = 0x00020001;

        private static WatchType watchType;

        internal static void Exec(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentNullException("filename");

            var data = File.ReadAllBytes(filename);

            var version = 0;

            if (data.GetDWord(0, 1) == magic_v1_1 && data.GetDWord(4, 1) == magic_v1_2)
                version = 1;
            if (data.GetDWord(0, 1) == magic_v2)
                version = 2;
            if (data.GetDWord(0, 1) == magic_v3_1 && data.GetDWord(4, 1) == magic_v3_2)
                version = 3;

            if (version == 0)
                throw new MissingFieldException();

            watchType = WatchDetector.GetWatchType(data, version);

            Console.WriteLine($"Watch detected: {watchType}");

            var decompiler = DecompilerFactory.GetDecompiler(version, filename);
            decompiler.Process(watchType, data);
        }
    }
}