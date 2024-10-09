using System;
using XiaomiWatch.Common;

namespace UnpackMiColorFace.FaceFileV3
{
    internal class FaceFileV3DecoderFactory
    {
        public static IFaceFileV3Decoder GetDecoder(WatchType watchType)
        {
            return watchType switch
            {
                WatchType.RedmiWatch3Active => new FaceRedmiWatch3ActiveDecoder(),
                _ => throw new NotSupportedException($"Can't find decoder for watch: {watchType}")
            };
        }
    }
}