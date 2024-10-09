using System;
using System.IO;
using ImageMagick;
using UnpackMiColorFace.Helpers;
using XiaomiWatch.Common;
using XiaomiWatch.Common.FaceFile;

namespace UnpackMiColorFace.Decompiler
{
    internal class FaceV1Decompiler : IFaceDecompiler
    {
        private readonly FilenameHelper filenameHelper;

        public FaceV1Decompiler(FilenameHelper filename)
        {
            filenameHelper = filename;
        }

        public void Process(WatchType watchType, byte[] data)
        {
            var dir = Directory.CreateDirectory(filenameHelper.NameNoExt);
            var path = dir.FullName; // 修改为直接使用 dir.FullName

            dir = Directory.CreateDirectory(Path.Combine(filenameHelper.NameNoExt, "images")); // 使用 Path.Combine
            var pathImages = dir.FullName; // 使用 Path.Combine

            uint offset = 0x14A;
            var blockCount = data.GetDWord(offset);

            offset += 4;
            var found = 0;

            for (var i = 0; i < blockCount; i++)
            {
                var blockType = data.GetDWord(offset);
                if (blockType == 3)
                {
                    found = found | 0x01;
                    ExtractImages(data, offset, pathImages);
                }
                else if (blockType == 0)
                {
                    found = found | 0x02;
                    BuildFaceFile(data, offset, Path.Combine(path, $"{filenameHelper.NameNoExt}.fprj")); // 修正
                }

                offset += 0x10;
            }

            if (found != 0x03)
                throw new MissingMemberException();
        }

        private void ExtractImages(byte[] data, uint offset, string path)
        {
            offset = data.GetDWord(offset + 0x0C);

            uint size = 0;
            size = data.GetDWord(offset);

            var imgBase = offset;
            offset += 4;

            for (var i = 0; i < size; i++)
            {
                try
                {
                    var idx = data.GetDWord(offset);

                    var dataLen = data.GetDWord(offset + 0x08);
                    var dataOfs = data.GetDWord(offset + 0x0C);

                    var bin = data.GetByteArray(imgBase + dataOfs, dataLen);
                    //File.WriteAllBytes(path + $"img_{idx:D4}.bin", bin);

                    uint width = bin.GetWord(0x09);
                    uint height = bin.GetWord(0x0B);

                    var type = (int)(bin.GetWord(0x05) / width);

                    byte[] clut = null;
                    var pxls = bin.GetByteArray(0x15, (uint)bin.Length - 0x15);

                    if (type == 1)
                    {
                        var pxlsLen = 0x15 + width * height;
                        clut = bin.GetByteArray(pxlsLen + 4, (uint)bin.Length - pxlsLen - 4);
                        pxls = bin.GetByteArray(0x15, pxlsLen - 0x15);
                    }

                    var bmp = BmpHelper.ConvertToBmpGTR(pxls, (int)width, (int)height, type, clut);

                    var bmpFile = Path.Combine(path, $"img_{idx:D4}.bmp"); // 使用 Path.Combine
                    var pngFile = Path.Combine(path, $"img_{idx:D4}.png"); // 使用 Path.Combine
                    File.WriteAllBytes(bmpFile, bmp);

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
                catch (Exception ex)
                {
                    Console.WriteLine("image processing err: " + ex);
                }

                offset += 0x10;
            }
        }

        private void BuildFaceFile(byte[] data, uint offset, string facefile)
        {
            var title = data.GetUnicodeString(0x1A);
            var previewIndex = data.GetDWord(0x46);

            offset = data.GetDWord(offset + 0x0C);

            uint size = 0;
            size = data.GetDWord(offset);

            var imgBase = offset;
            offset += 4;

            var face = new FaceProject();
            face.DeviceType = 1;
            face.Screen.Title = title;
            face.Screen.Bitmap = $"img_{previewIndex:D4}.png";

            for (var i = 0; i < size; i++)
            {
                var idx = data.GetDWord(offset);

                var dataLen = data.GetDWord(offset + 0x08);
                var dataOfs = data.GetDWord(offset + 0x0C);

                var bin = data.GetByteArray(imgBase + dataOfs, dataLen);
                //File.WriteAllBytes(path + $"img_{idx:D4}.bin", bin);

                var itemIndex = bin.GetDWord(0x00);
                var itemType = bin.GetDWord(0x04);

                int x = bin.GetWord(0x08);
                int y = bin.GetWord(0x0C);

                int width = bin.GetWord(0x10);
                int height = bin.GetWord(0x14);

                int alfa = bin.GetByte(0x1C);

                face.Screen.Widgets.Add(WidgetFactory.Get(itemType, bin));

                offset += 0x10;
            }

            var xml = face.Serialize();
            xml = xml.Replace("<FaceProject", "\r\n<FaceProject");
            xml = xml.Replace("<Screen", "\r\n<Screen");
            xml = xml.Replace("</FaceProject", "\r\n</FaceProject");
            xml = xml.Replace("</Screen", "\r\n</Screen");
            xml = xml.Replace("<Widget", "\r\n<Widget");
            File.WriteAllText(Path.Combine(facefile), xml); // 修正
        }
    }
}