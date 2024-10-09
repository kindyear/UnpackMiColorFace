using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageMagick;
using UnpackMiColorFace.FaceFileV3;
using UnpackMiColorFace.Helpers;
using XiaomiWatch.Common;
using XiaomiWatch.Common.Compress;
using XiaomiWatch.Common.FaceFile;

namespace UnpackMiColorFace.Decompiler
{
    internal class FaceV3Decompiler : IFaceDecompiler
    {
        private readonly FilenameHelper filenameHelper;

        public FaceV3Decompiler(FilenameHelper filenameHelper)
        {
            this.filenameHelper = filenameHelper;
        }

        public void Process(WatchType watchType, byte[] data)
        {
            if (!IsSupported(watchType))
                return;

            var dir = Directory.CreateDirectory(filenameHelper.NameNoExt);
            var path = Path.Combine(dir.FullName, ""); // 使用 Path.Combine 来构建路径
            var pathImages = Path.Combine(dir.FullName, "images");

            uint offsetPreview = 0x59;

            var blockStart = data.GetDWord(offsetPreview);
            var blockLen = data.GetDWord(offsetPreview + 4);

            var faceWidgets = new List<FaceWidget>();

            faceWidgets.AddRange(ExtractImages(watchType, data.GetByteArray(blockStart, blockLen), path));

            offsetPreview -= 0x08;

            blockStart = data.GetDWord(offsetPreview);
            blockLen = data.GetDWord(offsetPreview + 4);

            faceWidgets.AddRange(ExtractImageList(watchType, data.GetByteArray(blockStart, blockLen), pathImages));

            int len = data.GetByte(0x14);
            var title = data.GetUTF8String(0x15, len);

            BuildFaceFile(watchType, title, faceWidgets, Path.Combine(path, filenameHelper.NameNoExt));
        }

        private FaceProject BuildFaceFile(WatchType watchType, string title, IEnumerable<FaceWidget> faceWidgets,
            string faceFileName)
        {
            var face = new FaceProject();
            face.DeviceType = (int)watchType;
            face.Screen.Title = title;
            face.Screen.Bitmap = "preview.png";

            face.Screen.Widgets.AddRange(faceWidgets);

            var xml = face.Serialize();
            xml = xml.Replace("<FaceProject", "\r\n<FaceProject");
            xml = xml.Replace("<Screen", "\r\n<Screen");
            xml = xml.Replace("</FaceProject", "\r\n</FaceProject");
            xml = xml.Replace("</Screen", "\r\n</Screen");
            xml = xml.Replace("<Widget", "\r\n<Widget");
            File.WriteAllText($"{faceFileName}.fprj", xml);

            return face;
        }

        private IEnumerable<FaceWidget> ExtractImages(WatchType watchType, byte[] data, string path)
        {
            uint offset = 0;
            uint blockSize = 0x70;
            uint blockCount = 2;

            var faceWidgets = new List<FaceWidget>();

            string[] filenames = { "preview", "background" };

            var decompressor = ImageCompressFactory.GetDecompressor(watchType);

            for (var i = 0; i < blockCount; i++)
            {
                int sectionId = data.GetByte(offset);
                offset++;
                int posX = data.GetWord(offset + 0x00);
                int posY = data.GetWord(offset + 0x02);

                int width = data.GetWord(offset + 0x04);
                int height = data.GetWord(offset + 0x06);

                int type = data.GetByte(offset + 9);

                var dataOfs = data.GetDWord(offset + 0x0A);
                var dataLen = data.GetDWord(offset + 0x0E);

                Console.WriteLine($"image: {offset:X8}, 0x{sectionId:X2}, compress: {type}");

                if (i > 0) // do not add preview
                    faceWidgets.Add(new FaceWidgetImage
                    {
                        Name = filenames[i],
                        Bitmap = $"{filenames[i]}.png",
                        X = posX,
                        Y = posY,
                        Width = width,
                        Height = height,
                        Alpha = 0xFF
                    });

                var bin = data.GetByteArray(dataOfs, dataLen);
                var dec = decompressor.Decompress(bin, width, height);

                var bitmap = BmpHelper.ConvertToBmpGTR(dec, width, height, 2);

                var bmpFile = Path.Combine(path, $"{filenames[i]}.bmp");
                var pngFile = Path.Combine(path, $"{filenames[i]}.png");
                var pngFileCopy = Path.Combine(path, "images", $"{filenames[i]}.png");

                if (File.Exists(pngFile))
                    File.Delete(pngFile);

                File.WriteAllBytes(bmpFile, bitmap);

                using (var magik = new MagickImage())
                {
                    magik.Read(bmpFile);
                    magik.ColorType = ColorType.TrueColorAlpha;
                    magik.Transparent(MagickColor.FromRgba(0, 0, 0, 0));
                    magik.Format = MagickFormat.Png32;
                    magik.Write(pngFile);
                }

                File.Delete(bmpFile);

                if (!Directory.Exists(Path.GetDirectoryName(pngFileCopy)))
                    Directory.CreateDirectory(Path.GetDirectoryName(pngFileCopy));

                if (File.Exists(pngFileCopy))
                    File.Delete(pngFileCopy);

                File.Copy(pngFile, pngFileCopy);
                if (i > 0) File.Delete(pngFile);

                offset += blockSize - 1;
            }

            return faceWidgets;
        }

        private IEnumerable<FaceWidget> ExtractImageList(WatchType watchType, byte[] data, string path)
        {
            uint offset = 0;
            uint blockSize = 0x70;
            uint headerSize = 0x1E;

            var faceWidgets = new List<FaceWidget>();

            var header = data.GetByteArray(0, headerSize);
            offset += headerSize;

            var decompressor = ImageCompressFactory.GetDecompressor(watchType);

            while (data.GetByte(offset) > 0)
            {
                int sectionId = data.GetByte(offset);
                offset++;

                int posX = data.GetWord(offset);
                int posY = data.GetWord(offset + 2);
                int width = data.GetWord(offset + 4);
                int height = data.GetWord(offset + 6);
                int count = data.GetByte(offset + 8);
                int type = data.GetByte(offset + 9);

                if (count > 0)
                    Console.WriteLine($"imageList: {offset:X8}/{count:X2}, 0x{sectionId:X2}, compress: {type}");

                if (type == 0)
                {
                    var imageNameList = new List<string>();

                    for (uint i = 0; i < count; i++)
                    {
                        var dataOfs = data.GetDWord(offset + 0x0A + i * 8);
                        var dataLen = data.GetDWord(offset + 0x0E + i * 8);

                        var bin = data.GetByteArray(dataOfs, dataLen);
                        var dec = decompressor.Decompress(bin, width, height);

                        var bitmap = BmpHelper.ConvertToBmpGTR(dec, width, height, 2);

                        var bmpFile = Path.Combine(path, $"image_{sectionId:D2}_{i:D4}.bmp");
                        var pngFile = Path.Combine(path, $"image_{sectionId:D2}_{i:D4}.png");

                        imageNameList.Add($"{Path.GetFileName(pngFile)}");

                        if (File.Exists(pngFile))
                            File.Delete(pngFile);

                        File.WriteAllBytes(bmpFile, bitmap);

                        using (var magik = new MagickImage())
                        {
                            magik.Read(bmpFile);
                            magik.ColorType = ColorType.TrueColorAlpha;
                            magik.Transparent(MagickColor.FromRgba(0, 0, 0, 0));
                            magik.Format = MagickFormat.Png32;
                            magik.Write(pngFile);
                        }

                        File.Delete(bmpFile);
                    }

                    if (imageNameList.Count > 0)
                    {
                        var decoder = FaceFileV3DecoderFactory.GetDecoder(watchType);
                        faceWidgets.Add(decoder.GetWidget(sectionId, imageNameList, posX, posY, width, height));
                    }
                }
                else if (type == 2)
                {
                    // image
                    // 3 bytes per pixel, 2 bytes are - RGB 565, 3rd byte is alpha channel

                    var imageNameList = new List<string>();

                    var dataOfs = data.GetDWord(offset + 0x0A);
                    var dataLen = data.GetDWord(offset + 0x0E);

                    for (uint i = 0; i < count; i++)
                    {
                        var imageHeader = data.GetByteArray(dataOfs + i * dataLen, dataLen);
                        var posLastElement = (imageHeader.GetWord(0) << 2) + 0x0c - 2;
                        var dataSize = imageHeader.GetWord(posLastElement) * 3;
                        var pixelData = data.GetByteArray(imageHeader.GetDWord(2), dataSize);

                        var pos = imageHeader.Length;
                        var pixelLen = pixelData.Length.GetDWordAligned();
                        var bin = imageHeader.AppendZero(pixelLen);
                        bin.SetByteArray(pos, pixelData);
                        var dec = decompressor.Decompress(bin, width, height, (dataLen << 8) | (byte)type);

                        //string binFile = $"{path}\\image_{sectionId:D2}_{i:D4}.bin";
                        //File.WriteAllBytes(binFile, dec);

                        var decBin = dec.Rgb565AlphaToRGBA();

                        var bitmap = BmpHelper.ConvertToBmpGTR(decBin, width, height, 4);

                        var bmpFile = Path.Combine(path, $"image_{sectionId:D2}_{i:D4}.bmp");
                        var pngFile = Path.Combine(path, $"image_{sectionId:D2}_{i:D4}.png");

                        imageNameList.Add($"{Path.GetFileName(pngFile)}");

                        if (File.Exists(pngFile))
                            File.Delete(pngFile);

                        File.WriteAllBytes(bmpFile, bitmap);

                        using (var magik = new MagickImage())
                        {
                            magik.Read(bmpFile);
                            magik.ColorType = ColorType.TrueColorAlpha;
                            magik.Transparent(MagickColor.FromRgba(0, 0, 0, 0xFF));
                            magik.Format = MagickFormat.Png32;
                            magik.Write(pngFile);
                        }

                        File.Delete(bmpFile);
                    }

                    if (imageNameList.Count > 0)
                    {
                        var decoder = FaceFileV3DecoderFactory.GetDecoder(watchType);
                        faceWidgets.Add(decoder.GetWidget(sectionId, imageNameList, posX, posY, width, height));
                    }
                }
                else if (type == 3)
                {
                    // monocolor image
                    // image has 1 color only,
                    // with alpha map channel as image control over a color (1 byte map)

                    var imageNameList = new List<string>();

                    var dataOfs = data.GetDWord(offset + 0x0A);
                    var dataLen = data.GetDWord(offset + 0x0E);

                    var color = data.GetDWord(offset + 0x6A, 1);

                    for (uint i = 0; i < count; i++)
                    {
                        var imageHeader = data.GetByteArray(dataOfs + i * dataLen, dataLen);
                        var posLastElement = (imageHeader.GetWord(0) << 2) + 0x0c - 2;
                        int dataSize = imageHeader.GetWord(posLastElement);
                        var pixelData = data.GetByteArray(imageHeader.GetDWord(2), dataSize);

                        var pos = imageHeader.Length;
                        var pixelLen = pixelData.Length.GetDWordAligned();
                        var bin = imageHeader.AppendZero(pixelLen);
                        bin.SetByteArray(pos, pixelData);
                        var dec = decompressor.Decompress(bin, width, height, (dataLen << 8) | (byte)type);

                        // concat color and alpha channel from decompressed data
                        dec = dec.Select(i => (uint)((color >> 8) | (i << 24))).SelectMany(BitConverter.GetBytes)
                            .ToArray();

                        //string binFile = $"{path}\\image_{sectionId:D2}_{i:D4}.bin";
                        //File.WriteAllBytes(binFile, dec);

                        var bitmap = BmpHelper.ConvertToBmpGTR(dec, width, height, 4);

                        var bmpFile = Path.Combine(path, $"image_{sectionId:D2}_{i:D4}.bmp");
                        var pngFile = Path.Combine(path, $"image_{sectionId:D2}_{i:D4}.png");

                        imageNameList.Add($"{Path.GetFileName(pngFile)}");

                        if (File.Exists(pngFile))
                            File.Delete(pngFile);

                        File.WriteAllBytes(bmpFile, bitmap);

                        using (var magik = new MagickImage())
                        {
                            magik.Read(bmpFile);
                            magik.ColorType = ColorType.TrueColorAlpha;
                            magik.Transparent(MagickColor.FromRgba(0, 0, 0, 0));
                            magik.Format = MagickFormat.Png32;
                            magik.Write(pngFile);
                        }

                        File.Delete(bmpFile);
                    }

                    if (imageNameList.Count > 0)
                    {
                        var decoder = FaceFileV3DecoderFactory.GetDecoder(watchType);
                        faceWidgets.Add(decoder.GetWidget(sectionId, imageNameList, posX, posY, width, height,
                            color >> 8));
                    }
                }

                offset += blockSize - 1;
            }

            return faceWidgets;
        }

        private bool IsSupported(WatchType watchType)
        {
            switch (watchType)
            {
                case WatchType.RedmiWatch3Active:
                    return true;
                default:
                    return false;
            }
        }
    }
}