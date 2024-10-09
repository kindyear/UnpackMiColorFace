using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ImageMagick;
using UnpackMiColorFace.Helpers;
using XiaomiWatch.Common;
using XiaomiWatch.Common.FaceFile;

namespace UnpackMiColorFace.Decompiler
{
    internal class FaceV2Decompiler : IFaceDecompiler
    {
        private const uint OffsetTitle = 0x68;
        private readonly FilenameHelper filenameHelper;

        public FaceV2Decompiler(FilenameHelper filenameHelper)
        {
            this.filenameHelper = filenameHelper;
        }

        public void Process(WatchType watchType, byte[] data)
        {
            var dir = Directory.CreateDirectory(filenameHelper.NameNoExt);
            var path = Path.Combine(dir.FullName, ""); // 使用 Path.Combine

            if (File.Exists(Path.Combine(path, "source.bin"))) // 使用 Path.Combine
                File.Delete(Path.Combine(path, "source.bin")); // 使用 Path.Combine

            //File.Copy(filename, Path.Combine(path, "source.bin")); // 使用 Path.Combine

            uint offsetPreview = 0x20;
            uint offset = 0xA8;

            int shiftWordsUnk = data.GetByte(0x18);
            int count = data.GetByte(0x1C);
            int shiftWords = data.GetByte(0x1D);
            int subVersion = data.GetWord(0x1E); // S1 Pro subversion or at location 0x04 ??

            ProcessPreview(watchType, data, offsetPreview, path);

            offset += (uint)(shiftWords * 0x04);
            offset += (uint)(shiftWordsUnk * 0x04);

            for (var index = 0; index < count; index++)
            {
                var imagesFolder = filenameHelper.GetFaceSlotImagesFolder(watchType, index, subVersion);

                if (Directory.Exists(imagesFolder))
                    dir = new DirectoryInfo(imagesFolder);
                else
                    dir = Directory.CreateDirectory(imagesFolder);

                var pathImages = Path.Combine(dir.FullName, ""); // 使用 Path.Combine                

                List<FaceElement> lste = null;
                List<FaceWidget> lstw = null;
                List<FaceImage> lsti = null;
                List<FaceImageList> lstil = null;
                List<FaceAction> lsta = null;
                List<FaceAppItem> lstApp = null;

                // get back image + preview
                var backImageId = data.GetDWord(offset);
                var previewImageOffset = data.GetDWord(offset + 4);
                offset += 8;

                for (var i = 0; i < 10; i++)
                {
                    if (i == 0)
                        lste = ProcessElements(data, offset, path);
                    if (i == 2)
                        lsti = ProcessImageSingle(watchType, data, offset, pathImages);
                    if (i == 3)
                        lstil = ProcessImageList(watchType, data, offset, pathImages);
                    if (i == 5) // apps
                        lstApp = ProcessAppData(watchType, data, offset);
                    if (i == 7) // widgets
                        lstw = ProcessWidgets(data, offset, path);
                    if (i == 9) // action buttons
                        lsta = ProcessAction(data, offset, path);

                    offset += 8;
                }

                if (watchType == WatchType.MiWatchS3 || watchType == WatchType.MiBand9)
                {
                    offset += 0x44;
                    var themeData = data.GetDWord(offset);
                    offset += 4;

                    var shiftThemeValue = (int)(themeData & 0xFF) >> 2;
                    if (shiftThemeValue > 1) offset += (uint)(shiftThemeValue * 4);
                }

                if (watchType == WatchType.Gen3 || watchType == WatchType.MiWatchS3 ||
                    watchType == WatchType.RedmiWatch4)
                    lste.Insert(0, new FaceElement(backImageId));

                var facefile = filenameHelper.GetFaceSlotFilename(watchType, index, subVersion);

                var title = GetFaceTitle(data, watchType);

                var face = BuildFaceFile(title, watchType, lste, lsti, lstil, lstw, lstApp, lsta,
                    Path.Combine(path, facefile)); // 使用 Path.Combine
                BuildPreview(face, watchType, imagesFolder, Path.Combine(path, facefile)); // 使用 Path.Combine
            }
        }


        private string GetFaceTitle(byte[] data, WatchType watchType)
        {
            var titleData = data.GetDWord(OffsetTitle);
            if (titleData == 0xFFFFFFFF)
            {
                int titleType = data.GetByte(OffsetTitle + 7);
                var offsetTitle = data.GetDWord(OffsetTitle + 0x0c);
                var dataSize = data.GetDWord(OffsetTitle + 0x10);

                if (titleType == 6)
                {
                    var titleSignature = data.GetDWord(offsetTitle);
                    if (titleSignature == 0x07)
                    {
                        var count = 3; // 预定义的标题数量
                        var offsetCurrText = (uint)(offsetTitle + 8 + count * 4);
                        var titleList = new string[count];
                        for (var i = 0; i < count; i++)
                        {
                            var currLen = data.GetDWord((uint)(offsetTitle + 8 + i * 4));
                            titleList[i] = data.GetUTF8String(offsetCurrText, (int)currLen).Trim();
                            offsetCurrText += currLen; // 更新当前文本的偏移量
                        }

                        return string.Join("|", titleList); // 用 '|' 连接标题
                    }
                }

                return string.Empty; // 返回空字符串
            }

            return data.GetUTF8String(OffsetTitle); // 返回标题
        }

        private void ProcessPreview(WatchType watchType, byte[] data, uint offset, string path)
        {
            var dataOfs = data.GetDWord(offset);
            var dataLen = data.GetDWord(dataOfs + 8);

            var bin = data.GetByteArray(dataOfs, dataLen + 0x0C);
            //File.WriteAllBytes(path + $"img_{idx:D4}.bin", bin);

            uint width = bin.GetWord(0x04);
            uint height = bin.GetWord(0x06);

            int rle = bin[0];
            int type = bin[1];

            if (bin.GetDWord(0) == 0)
            {
                type = 4;
            }
            else if (watchType == WatchType.RedmiWatch2
                     || watchType == WatchType.RedmiBandPro
                     || watchType == WatchType.MiBand7Pro)
            {
                type = bin[0] & 0xF;
                if (type == 4) type = 2;
            }

            uint magic = 0;
            byte[] clut = null;
            var pxls = bin.GetByteArray(0x0C, (uint)bin.Length - 0x0C);

            if (watchType == WatchType.MiWatchS3
                || (watchType == WatchType.MiBand9
                    && rle == 0x10))
            {
                type = 1;
                clut = pxls.Take(0x400).ToArray();
                pxls = pxls.Skip(0x400).ToArray();
            }

            magic = bin.GetDWord(0x0C);

            if (magic != 0x5AA521E0)
            {
                if (type == 1 && watchType != WatchType.MiWatchS3 && watchType != WatchType.MiBand9)
                {
                    var pxlsLen = 0x15 + width * height;
                    clut = bin.GetByteArray(pxlsLen + 4, (uint)bin.Length - pxlsLen - 4);
                    pxls = bin.GetByteArray(0x0C, pxlsLen - 0x0C);
                }
            }
            else
            {
                var cpr = bin.GetByteArray(0xCu + 8, dataLen - 8);
                type = bin[0x10] & 0x0F;
                var decLen = (int)bin.GetDWord(0x10) >> 4;

                if (rle == 0x10)
                {
                    pxls = BmpHelper.UncompressRLEv20(cpr, decLen);
                }
                else
                {
                    if (watchType == WatchType.RedmiWatch2 || watchType == WatchType.MiBand7Pro)
                        pxls = BmpHelper.UncompressRLEv11(cpr, decLen, (byte)type);
                    else
                        pxls = BmpHelper.UncompressRLEv10(cpr, decLen, (byte)type);
                }

                if (type == 1)
                {
                    pxls = pxls.ConvertToRGBA();
                    type = 4;
                }

                var binFile = Path.Combine(path, "preview_cpr.bin");
                //File.WriteAllBytes(binFile, cpr);

                binFile = Path.Combine(path, "preview_dec.bin");
                //File.WriteAllBytes(binFile, pxls);
            }

            byte[] bmp = null;

            if (magic == 0x5AA521E0)
                bmp = BmpHelper.ConvertToBmpGTRv2(pxls, (int)width, (int)height, type);
            else
                bmp = BmpHelper.ConvertToBmpGTR(pxls, (int)width, (int)height, type, clut);

            var bmpFile = Path.Combine(path, "preview.bmp");
            var pngFile = Path.Combine(path, "preview.png");
            var pngFileCopy = Path.Combine(path, "images", "preview.png");

            if (File.Exists(pngFile))
                File.Delete(pngFile);

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

            if (!Directory.Exists(Path.GetDirectoryName(pngFileCopy)))
                Directory.CreateDirectory(Path.GetDirectoryName(pngFileCopy));

            if (File.Exists(pngFileCopy))
                File.Delete(pngFileCopy);

            File.Copy(pngFile, pngFileCopy);
        }

        private List<FaceAppItem> ProcessAppData(WatchType watchType, byte[] data, uint offset)
        {
            var path = Path.Combine(filenameHelper.NameNoExt, "app"); // 使用 Path.Combine

            var blockCount = data.GetDWord(offset);
            var blockOffset = data.GetDWord(offset + 4);

            var lst = new List<FaceAppItem>();

            if (blockCount == 0)
                return lst;

            offset = blockOffset;
            for (var i = 0; i < blockCount; i++)
            {
                uint idx = data.GetWord(offset);
                var id = data.GetDWord(offset);

                var dataOfs = data.GetDWord(offset + 0x08);
                var dataLen = data.GetDWord(offset + 0x0C);

                var fileSize = data.GetDWord(dataOfs) & 0xFFFFFF;
                uint filenameLen = data[dataOfs + 3];
                dataLen = 0x14 + filenameLen + fileSize;

                var bin = data.GetByteArray(dataOfs, dataLen);

                // string appFile = path + $"app_{idx:D4}.bin";
                // File.WriteAllBytes(appFile, bin);

                var filename = Encoding.ASCII.GetString(bin.GetByteArray(0x14, filenameLen));

                var appFile = Path.Combine(path, filename); // 使用 Path.Combine

                DirectoryInfo dir;

                var appDir = Path.GetDirectoryName(appFile);
                if (Directory.Exists(appDir))
                    dir = new DirectoryInfo(appDir);
                else
                    dir = Directory.CreateDirectory(appDir);

                File.WriteAllBytes(appFile, bin.GetByteArray(0x14 + filenameLen, fileSize));

                lst.Add(new FaceAppItem
                {
                    Id = id,
                    Name = filename
                });

                offset += 0x10;
            }

            return lst;
        }

        private void BuildPreview(FaceProject face, WatchType watchType, string imagesFolder, string facefile)
        {
            var index = 0;
            var displayWidth = WatchScreen.GetScreenWidth(watchType);
            var displayHeight = WatchScreen.GetScreenHeight(watchType);

            using (var preview = new MagickImage(MagickColor.FromRgb(0, 0, 0), (uint)displayWidth, (uint)displayHeight))
            {
                foreach (var widget in face.Screen.Widgets)
                {
                    switch (widget.Shape)
                    {
                        case 30: // image
                            var widgetImage = widget as FaceWidgetImage;
                            var pathImage = Path.Combine(imagesFolder, widgetImage.Bitmap); // 使用 Path.Combine
                            using (var overlay = new MagickImage())
                            {
                                overlay.Read(pathImage);
                                preview.Composite(overlay, widget.X, widget.Y, CompositeOperator.Over);
                            }

                            break;
                        case 43: // CircularGauge
                            var widgetCirc = widget as FaceWidgetCircularGauge;
                            if (!string.IsNullOrEmpty(widgetCirc.PointerImage))
                            {
                                var pathCirc = Path.Combine(imagesFolder, widgetCirc.PointerImage); // 使用 Path.Combine
                                ApplyCircleImage(preview, widgetCirc, pathCirc, 150);
                            }

                            break;
                        case 31: // imageList
                            var wImgList = widget as FaceWidgetImageList;
                            var imgList = wImgList.BitmapList.Split('|');
                            var digitIndex = GetStringSource(wImgList.DataSrcIndex);
                            var digitInt = int.Parse(digitIndex);
                            if (digitInt >= imgList.Length) // 修复索引越界
                                digitInt = imgList.Length - 1; // 改为最后一个索引
                            var pathFirst = imgList[digitInt].Split(':')[1];
                            var pathList = Path.Combine(imagesFolder, pathFirst); // 使用 Path.Combine
                            using (var overlay = new MagickImage())
                            {
                                overlay.Read(pathList);
                                preview.Composite(overlay, widget.X, widget.Y, CompositeOperator.Over);
                            }

                            break;
                        case 32: // image
                            var widgetNum = widget as FaceWidgetDigitalNum;
                            using (var overlay = new MagickImage())
                            {
                                var width = widgetNum.Width;
                                var spacing = widgetNum.Spacing;

                                var digit = GetStringSource(widgetNum.DataSrcValue);
                                var posX = widget.X;
                                var posY = widget.Y;
                                var maxLen = Math.Min(widgetNum.Digits, digit.Length); // 确保 maxLen 不超过 digit.Length

                                if (widgetNum.Alignment == 0 // center
                                    && (watchType == WatchType.Gen2
                                        || watchType == WatchType.Gen3
                                        || watchType == WatchType.MiWatchS3
                                        || watchType == WatchType.MiBand8
                                        || watchType == WatchType.MiBand9
                                        || watchType == WatchType.RedmiWatch2
                                        || watchType == WatchType.RedmiWatch3
                                        || watchType == WatchType.RedmiWatch4
                                        || watchType == WatchType.MiBand8Pro
                                        || watchType == WatchType.MiBand7Pro))
                                    posX -= (width + spacing) * maxLen / 2;

                                var bitmaps = widgetNum.BitmapList.Split('|');

                                for (var i = 0; i < maxLen; i++)
                                {
                                    var pathNum = Path.Combine(imagesFolder,
                                        bitmaps[int.Parse(digit[i].ToString())]); // 使用 Path.Combine
                                    overlay.Read(pathNum);
                                    preview.Composite(overlay, posX, posY, CompositeOperator.Over);
                                    posX += width + spacing;
                                }
                            }

                            break;
                        case 27: // analog clock
                            var clock = widget as FaceWidgetAnalogClock;
                            if (!string.IsNullOrEmpty(clock.HourHandImage))
                            {
                                var pathHand = Path.Combine(imagesFolder, clock.HourHandImage); // 使用 Path.Combine
                                ApplyClockImage(preview, clock, clock.HourImageRotateX, clock.HourImageRotateY,
                                    pathHand, -60);
                            }

                            if (!string.IsNullOrEmpty(clock.MinuteHandImage))
                            {
                                var pathHand = Path.Combine(imagesFolder, clock.MinuteHandImage); // 使用 Path.Combine
                                ApplyClockImage(preview, clock, clock.MinuteImageRotateX, clock.MinuteImageRotateY,
                                    pathHand, 50);
                            }

                            if (!string.IsNullOrEmpty(clock.SecondHandImage))
                            {
                                var pathHand = Path.Combine(imagesFolder, clock.SecondHandImage); // 使用 Path.Combine
                                ApplyClockImage(preview, clock, clock.SecondImageRotateX, clock.SecondImageRotateY,
                                    pathHand, 120);
                            }

                            break;
                    }

                    index++;
                }

                if (watchType == WatchType.Gen1
                    || watchType == WatchType.Gen2
                    || watchType == WatchType.Gen3
                    || watchType == WatchType.MiWatchS3)
                {
                    // cut image by circle
                    preview.Alpha(AlphaOption.Set);
                    using (var copy = preview.Clone())
                    {
                        copy.Distort(DistortMethod.DePolar, 0);
                        copy.VirtualPixelMethod = VirtualPixelMethod.HorizontalTile;
                        copy.BackgroundColor = MagickColors.None;
                        copy.Distort(DistortMethod.Polar, 0);

                        preview.Compose = CompositeOperator.DstIn;
                        preview.Composite(copy, CompositeOperator.CopyAlpha);
                    }
                }

                preview.Write($"{facefile}_preview.png");
            }
        }

        private void ApplyCircleImage(MagickImage preview, FaceWidgetCircularGauge gauge, string pathImage, int angle)
        {
            using (var overlay =
                   new MagickImage(MagickColor.FromRgba(0, 0, 0, 0), (uint)gauge.Width, (uint)gauge.Height))
            {
                var posX = (gauge.Width - gauge.X) / 2 - gauge.PointerRotateX;
                var posY = (gauge.Height - gauge.Y) / 2 - gauge.PointerRotateY;

                var img = new MagickImage(pathImage);
                overlay.Composite(img, posX, posY, CompositeOperator.Over);
                overlay.Distort(DistortMethod.ScaleRotateTranslate, gauge.Width / 2, gauge.Height / 2, 1, angle);
                preview.Composite(overlay, 0, 0, CompositeOperator.Over);
            }
        }

        private string GetStringSource(string dataSrcValue)
        {
            switch (dataSrcValue)
            {
                case FaceItemDataSrc.Month: return "10";
                case FaceItemDataSrc.Day: return "21";
                case FaceItemDataSrc.Weekday: return "3";

                case FaceItemDataSrc.Hour: return "12";
                case FaceItemDataSrc.HourHigh: return "1";
                case FaceItemDataSrc.HourLow: return "2";
                case FaceItemDataSrc.Minute: return "45";
                case FaceItemDataSrc.MinuteHigh: return "4";
                case FaceItemDataSrc.MinuteLow: return "5";
                case FaceItemDataSrc.Second: return "28";
                case FaceItemDataSrc.SecondHigh: return "2";
                case FaceItemDataSrc.SecondLow: return "8";

                case FaceItemDataSrc.Steps: return "15652";
                case FaceItemDataSrc.Calories: return "12175";
                case FaceItemDataSrc.Hrm: return "174";

                case FaceItemDataSrc.ActivityCount: return "10";
                case FaceItemDataSrc.Stress: return "42";
                case FaceItemDataSrc.Sleep: return "60";

                case FaceItemDataSrc.Battery: return "100";
                case FaceItemDataSrc.WeatherTemp: return "23";
                case FaceItemDataSrc.WeatherType: return "1";
                case FaceItemDataSrc.WeatherAir: return "2";

                default: return "120000";
            }
        }

        private void ApplyClockImage(MagickImage preview, FaceWidgetAnalogClock clock, int rotateX, int rotateY,
            string pathImage, int angle)
        {
            using (var overlay =
                   new MagickImage(MagickColor.FromRgba(0, 0, 0, 0), (uint)clock.Width, (uint)clock.Height))
            {
                var posX = (clock.Width - clock.X) / 2 - rotateX;
                var posY = (clock.Height - clock.Y) / 2 - rotateY;

                var img = new MagickImage(pathImage);
                overlay.Composite(img, posX, posY, CompositeOperator.Over);
                overlay.Distort(DistortMethod.ScaleRotateTranslate, clock.Width / 2, clock.Height / 2, 1, angle);
                preview.Composite(overlay, 0, 0, CompositeOperator.Over);
            }
        }

        private FaceProject BuildFaceFile(string title, WatchType watchType,
            List<FaceElement> lste, List<FaceImage> lsti,
            List<FaceImageList> lstil, List<FaceWidget> lstw,
            List<FaceAppItem> lstApp, List<FaceAction> lsta,
            string facefile)
        {
            var face = new FaceProject();
            face.DeviceType = (int)watchType;
            face.Screen.Title = title;
            face.Screen.Bitmap = "preview.png"; // 如果需要，可以通过 Path.Combine 处理这个路径

            var idx = 0;
            foreach (var e in lste)
            {
                try
                {
                    if (e.TargetId >> 24 == 02)
                    {
                        var img = lsti.Find(c => (c.Id & 0xFF00FFFF) == e.TargetId);

                        face.Screen.Widgets.Add(new FaceWidgetImage
                        {
                            Name = $"image_{idx:D2}",
                            X = e.PosX,
                            Y = e.PosY,
                            Width = img.Width,
                            Height = img.Height,
                            Alpha = 0xFF,
                            Bitmap = img.Name // 如果需要，可以通过 Path.Combine 处理这个路径
                        });
                    }
                    else if (e.TargetId >> 24 == 07)
                    {
                        var wdgt = lstw.Find(c => c.Id == e.TargetId);
                        var imgl = lstil.Find(c => c.Id == wdgt.TargetId);
                        var imgi = lsti.Find(c => c.Id == wdgt.TargetId);
                        if (imgl == null && imgi == null) continue;

                        if (wdgt.TypeId == 0x01)
                        {
                            if (imgl.NameList.Count() == 10)
                            {
                                var _list = imgl.NameList.ToList();
                                _list.Add(imgl.NameList[0]);
                                imgl.NameList = _list.ToArray();
                            }

                            var digits = wdgt.Digits;
                            var digit = GetStringSource($"{wdgt.Shape:X2}{wdgt.DataSrcDisplay:X2}");
                            var maxLen = digits > digit.Length ? digit.Length : digits;

                            var posX = e.PosX;
                            var width = imgl.Width;
                            var spacing = 0;
                            if (wdgt.Align == 2 // center
                                && (watchType == WatchType.Gen2
                                    || watchType == WatchType.Gen3
                                    || watchType == WatchType.MiWatchS3
                                    || watchType == WatchType.RedmiBandPro
                                    || watchType == WatchType.RedmiWatch2
                                    || watchType == WatchType.RedmiWatch3
                                    || watchType == WatchType.RedmiWatch4
                                    || watchType == WatchType.MiBand8
                                    || watchType == WatchType.MiBand9
                                    || watchType == WatchType.MiBand8Pro
                                    || watchType == WatchType.MiBand7Pro))
                                posX -= (width + spacing) * maxLen / 2;
                            else if (wdgt.Align == 0 // right
                                     && (watchType == WatchType.Gen2
                                         || watchType == WatchType.Gen3
                                         || watchType == WatchType.MiWatchS3
                                         || watchType == WatchType.RedmiBandPro
                                         || watchType == WatchType.RedmiWatch2
                                         || watchType == WatchType.RedmiWatch3
                                         || watchType == WatchType.RedmiWatch4
                                         || watchType == WatchType.MiBand8
                                         || watchType == WatchType.MiBand9
                                         || watchType == WatchType.MiBand8Pro
                                         || watchType == WatchType.MiBand7Pro))
                                posX -= (width + spacing) * maxLen;

                            face.Screen.Widgets.Add(new FaceWidgetDigitalNum
                            {
                                Name = $"widget_{idx:D2}",
                                X = posX,
                                Y = e.PosY,
                                Width = imgl.Width,
                                Height = imgl.Height,
                                Digits = digits,
                                Alignment = GetTextAlignment(wdgt.Align),
                                Alpha = 0xFF,
                                DataSrcValue = $"{wdgt.Shape:X2}{wdgt.DataSrcDisplay:X2}",
                                BitmapList = string.Join("|", imgl.NameList) // 如果需要，可以通过 Path.Combine 处理这些路径
                            });
                        }
                        else if (wdgt.TypeId == 0x02)
                        {
                            var hasCustomValues = wdgt.RawData.GetWord(0x0c) == 0x200;
                            var bmpList = "";
                            for (var x = 0; x < imgl.NameList.Length; x++)
                            {
                                var value = 0;
                                try
                                {
                                    if (hasCustomValues)
                                        value = (int)wdgt.RawData.GetDWord(0x10 + x * 4);
                                }
                                catch
                                {
                                }

                                if (bmpList.Length > 0)
                                    bmpList += "|";
                                bmpList +=
                                    $"({(hasCustomValues ? value : x)}):{imgl.NameList[x]}"; // 如果需要，可以通过 Path.Combine 处理这些路径
                            }

                            face.Screen.Widgets.Add(new FaceWidgetImageList
                            {
                                Name = $"widget_{idx:D2}",
                                X = e.PosX,
                                Y = e.PosY,
                                Width = imgl.Width,
                                Height = imgl.Height,
                                Alpha = 0xFF,
                                DataSrcIndex = $"{wdgt.Shape:X2}{wdgt.DataSrcDisplay:X2}",
                                BitmapList = bmpList // 如果需要，可以通过 Path.Combine 处理这些路径
                            });
                        }
                        else if (wdgt.TypeId == 0x03)
                        {
                            if (wdgt.DataSrcDisplay == 0x11)
                            {
                                if (!face.Screen.Widgets.Any(w => w.Shape == 27))
                                    face.Screen.Widgets.Add(new FaceWidgetAnalogClock
                                    {
                                        Name = $"analogClock_{idx:D2}",
                                        Width = WatchScreen.GetScreenWidth(watchType),
                                        Height = WatchScreen.GetScreenHeight(watchType),
                                        Alpha = 0xFF
                                    });

                                var clock = face.Screen.Widgets.First(w => w.Shape == 27) as FaceWidgetAnalogClock;

                                if (wdgt.Shape == 0x08)
                                {
                                    clock.HourHandImage = imgi.Name; // 如果需要，可以通过 Path.Combine 处理这个路径
                                    clock.HourImageRotateX = wdgt.X;
                                    clock.HourImageRotateY = wdgt.Y;
                                }
                                else if (wdgt.Shape == 0x10)
                                {
                                    clock.MinuteHandImage = imgi.Name; // 如果需要，可以通过 Path.Combine 处理这个路径
                                    clock.MinuteImageRotateX = wdgt.X;
                                    clock.MinuteImageRotateY = wdgt.Y;
                                }
                                else if (wdgt.Shape == 0x18)
                                {
                                    clock.SecondHandImage = imgi.Name; // 如果需要，可以通过 Path.Combine 处理这个路径
                                    clock.SecondImageRotateX = wdgt.X;
                                    clock.SecondImageRotateY = wdgt.Y;
                                }
                            }
                            else
                            {
                                face.Screen.Widgets.Add(new FaceWidgetCircularGauge
                                {
                                    Name = $"widget_{idx:D2}",
                                    X = e.PosX,
                                    Y = e.PosY,
                                    Width = imgi.Width,
                                    Height = imgi.Height,
                                    Alpha = 0xFF,
                                    DataSrcVal = $"{wdgt.Shape:X2}{wdgt.DataSrcDisplay:X2}",
                                    PointerImage = imgi.Name // 如果需要，可以通过 Path.Combine 处理这个路径
                                });
                            }
                        }
                    }
                    else if (e.TargetId >> 24 == 09)
                    {
                        var action = lsta.Find(c => c.Id == e.TargetId);
                        if (action != null)
                        {
                            var nameAction = "";
                            if (action.ActionId == 0x23721400)
                            {
                                var appItem = lstApp.Find(c => c.Id == action.AppId);
                                if (appItem != null)
                                    nameAction = $"_[{appItem.Name}]";
                            }

                            face.Screen.Widgets.Add(new FaceWidgetImage
                            {
                                Name = $"btn_{idx:D2}{nameAction}",
                                X = e.PosX,
                                Y = e.PosY,
                                Alpha = 0xFF,
                                Bitmap = action.ImageName // 如果需要，可以通过 Path.Combine 处理这个路径
                            });
                        }
                    }
                    else if (e.TargetId >> 24 == 05)
                    {
                        var app = lstApp.Find(c => c.Id == e.TargetId);
                        face.Screen.Widgets.Add(new FaceWidgetContainer
                        {
                            Name = $"app_{Uri.EscapeDataString(app.Name)}",
                            X = 0,
                            Y = 0,
                            Width = WatchScreen.GetScreenWidth(watchType),
                            Height = WatchScreen.GetScreenHeight(watchType)
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.GotError = true;
                    Console.WriteLine($"Failed to build watchface item: {ex}");
                }

                idx++;
            }

            var xml = face.Serialize();
            xml = xml.Replace("<FaceProject", "\r\n<FaceProject");
            xml = xml.Replace("<Screen", "\r\n<Screen");
            xml = xml.Replace("</FaceProject", "\r\n</FaceProject");
            xml = xml.Replace("</Screen", "\r\n</Screen");
            xml = xml.Replace("<Widget", "\r\n<Widget");

            // 使用 Path.Combine 来处理文件路径
            File.WriteAllText($"{Path.Combine(facefile)}.fprj", xml); // 确保在写入时使用 Path.Combine

            return face;
        }

        private static int GetTextAlignment(byte align)
        {
            return align switch
            {
                2 => 1,
                1 => 2,
                _ => 0
            };
        }

        private List<FaceAction> ProcessAction(byte[] data, uint offset, string path)
        {
            var blockCount = data.GetDWord(offset);
            var blockOffset = data.GetDWord(offset + 4);

            var lst = new List<FaceAction>();
            offset = blockOffset;

            for (var i = 0; i < blockCount; i++)
            {
                try
                {
                    uint idx = data.GetWord(offset);
                    var id = data.GetDWord(offset);

                    var dataOfs = data.GetDWord(offset + 0x08);
                    var dataLen = data.GetDWord(offset + 0x0C);

                    var bin = data.GetByteArray(dataOfs, dataLen);
                    // 使用 Path.Combine 确保路径兼容性
                    var filePath = Path.Combine(path, $"img_{idx:D4}.bin");
                    // File.WriteAllBytes(filePath, bin); // 如果需要，可以取消注释以保存文件

                    var imageId = bin.GetDWord(0x20) & 0xFF;

                    if (imageId == 0) continue;

                    lst.Add(new FaceAction
                    {
                        RawData = bin,
                        Id = id,
                        // 这里使用 Path.Combine 来生成图片文件名
                        ImageName = Path.Combine(path, $"img_{imageId:D4}.png"),
                        ActionId = bin.GetDWord(0x28),
                        AppId = bin.GetDWord(0x2C)
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine("action processing err: " + ex);
                }

                offset += 0x10;
            }

            return lst;
        }

        private List<FaceElement> ProcessElements(byte[] data, uint offset, string path)
        {
            var blockCount = data.GetDWord(offset);
            var blockOffset = data.GetDWord(offset + 4);

            var lst = new List<FaceElement>();
            offset = blockOffset;

            for (var i = 0; i < blockCount; i++)
            {
                try
                {
                    uint idx = data.GetWord(offset);

                    var dataOfs = data.GetDWord(offset + 0x08);
                    var dataLen = data.GetDWord(offset + 0x0C);

                    var bin = data.GetByteArray(dataOfs, dataLen);
                    // 使用 Path.Combine 确保路径兼容性
                    var filePath = Path.Combine(path, $"img_{idx:D4}.bin");
                    // File.WriteAllBytes(filePath, bin); // 如果需要，可以取消注释以保存文件

                    lst.Add(new FaceElement
                    {
                        TargetId = bin.GetDWord(0),
                        PosX = bin.GetWord(0x04),
                        PosY = bin.GetWord(0x06)
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine("image processing err: " + ex);
                }

                offset += 0x10;
            }

            return lst;
        }

        private List<FaceWidget> ProcessWidgets(byte[] data, uint offset, string path)
        {
            var blockCount = data.GetDWord(offset);
            var blockOffset = data.GetDWord(offset + 4);

            var lst = new List<FaceWidget>();
            offset = blockOffset;

            for (var i = 0; i < blockCount; i++)
            {
                try
                {
                    uint idx = data.GetWord(offset);
                    var id = data.GetDWord(offset);

                    var dataOfs = data.GetDWord(offset + 0x08);
                    var dataLen = data.GetDWord(offset + 0x0C);

                    var bin = data.GetByteArray(dataOfs, dataLen);
                    // 使用 Path.Combine 确保路径兼容性
                    var filePath = Path.Combine(path, $"img_{idx:D4}.bin");
                    // File.WriteAllBytes(filePath, bin); // 如果需要，可以取消注释以保存文件

                    var digits = bin[2];
                    if (bin.GetWord(0) == 0x1109
                        || bin.GetWord(0) == 0x110A
                        || bin.GetWord(0) == 0x1111
                        || bin.GetWord(0) == 0x1112
                        || bin.GetWord(0) == 0x1119
                        || bin.GetWord(0) == 0x111A)
                        digits = 1;

                    lst.Add(new FaceWidget
                    {
                        RawData = bin,
                        Shape = bin[0],
                        DataSrcDisplay = bin[1],
                        X = bin.Length >= 0x20 ? bin.GetWord(0x14) : 0,
                        Y = bin.Length >= 0x20 ? bin.GetWord(0x16) : 0,
                        Width = 0,
                        Height = 0,
                        Id = id,
                        Digits = digits,
                        Align = (byte)(bin[3] & 0x03),
                        TypeId = bin[3] >> 4,
                        TargetId = bin.GetDWord(0x08)
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine("image processing err: " + ex);
                }

                offset += 0x10;
            }

            return lst;
        }

        private List<FaceImage> ProcessImageSingle(WatchType watchType, byte[] data, uint offset, string path)
        {
            var version = data.GetDWord(0x10);

            var blockCount = data.GetDWord(offset);
            var blockOffset = data.GetDWord(offset + 4);

            var lst = new List<FaceImage>();

            offset = blockOffset;
            for (var i = 0; i < blockCount; i++)
            {
                try
                {
                    uint idx = data.GetWord(offset);
                    var id = data.GetDWord(offset);

                    var dataOfs = data.GetDWord(offset + 0x08);
                    var dataLen = data.GetDWord(offset + 0x0C);

                    var bin = data.GetByteArray(dataOfs, dataLen);
                    // 使用 Path.Combine 确保路径兼容性
                    var binFilePath = Path.Combine(path, $"img_{idx:D4}.bin");
                    // File.WriteAllBytes(binFilePath, bin); // 如果需要，可以取消注释以保存文件

                    int width = bin.GetWord(0x04);
                    int height = bin.GetWord(0x06);

                    int rle = bin[0];
                    int type = bin[1];

                    if (bin.GetDWord(0) == 0) type = 4;

                    if (watchType == WatchType.Gen2 && version == 0x800)
                    {
                        if (bin.GetByte(0) == 0) type = 4;
                        if (bin.GetByte(0) == 3) type = 2;
                    }

                    var magic = bin.GetDWord(0x0C);
                    byte[] clut = null;
                    byte[] alfa = null;
                    var pxls = bin.GetByteArray(0x0C, (uint)bin.Length - 0x0C);

                    if (watchType == WatchType.RedmiWatch2
                        || watchType == WatchType.RedmiBandPro
                        || watchType == WatchType.MiBand7Pro)
                        if ((rle & 0x0F) == 0x04)
                        {
                            type = 2;
                            var pxlLen = (uint)(width * 2 * height);
                            alfa = pxls.GetByteArray(pxlLen, (uint)(width * height));
                            pxls = pxls.GetByteArray(0, pxlLen);
                        }

                    if (watchType == WatchType.MiWatchS3
                        || (watchType == WatchType.MiBand9
                            && rle == 0x10))
                    {
                        //type = 1;
                        //clut = pxls.Take(0x400).ToArray();
                        //pxls = pxls.Skip(0x400).ToArray();

                        pxls = pxls.ConvertToRGBA();
                        type = 4;
                    }

                    if (magic != 0x5AA521E0)
                    {
                        if (type == 1 && watchType != WatchType.MiWatchS3 && watchType != WatchType.MiBand9)
                        {
                            var pxlsLen = 0x15 + (uint)width * (uint)height;
                            clut = bin.GetByteArray(pxlsLen + 4, (uint)bin.Length - pxlsLen - 4);
                            pxls = bin.GetByteArray(0x0C, pxlsLen - 0x0C);
                        }
                    }
                    else
                    {
                        // error in data structure size
                        // reloading binary for correct size

                        bin = data.GetByteArray(dataOfs, dataLen + 4);
                        var cprLen = bin.GetDWord(0x08);
                        var cpr = bin.GetByteArray(0xCu + 8, cprLen - 8);
                        type = bin[0x10] & 0x0F;
                        var decLen = (int)bin.GetDWord(0x10) >> 4;

                        Console.WriteLine(
                            $"got compressed image[{Path.GetFileName(path)}:{idx}]: {dataOfs:X8}, type:{type:X2}, rle:{rle:X2}");

                        if (rle == 0x10)
                        {
                            pxls = BmpHelper.UncompressRLEv20(cpr, decLen);
                        }
                        else
                        {
                            if (watchType == WatchType.RedmiWatch2 || watchType == WatchType.MiBand7Pro)
                                pxls = BmpHelper.UncompressRLEv11(cpr, decLen, (byte)type);
                            else
                                pxls = BmpHelper.UncompressRLEv10(cpr, decLen, (byte)type);
                        }

                        if (type == 1)
                        {
                            pxls = pxls.ConvertToRGBA();
                            type = 4;
                        }

                        //string binFile = path + $"img_{idx:D4}.bin";
                        //File.WriteAllBytes(binFile, pxls);
                    }

                    byte[] bmp = null;
                    if (magic == 0x5AA521E0)
                        bmp = BmpHelper.ConvertToBmpGTRv2(pxls, width, height, type);
                    else
                        bmp = BmpHelper.ConvertToBmpGTR(pxls, width, height, type, clut);

                    var bmpFile = Path.Combine(path, $"img_{idx:D4}_{type}_{clut?.Length ?? 0}.bmp");
                    var pngFile = Path.Combine(path, $"img_{idx:D4}.png");
                    File.WriteAllBytes(bmpFile, bmp);

                    using (var magik = new MagickImage())
                    {
                        magik.Read(bmpFile);
                        magik.ColorType = ColorType.TrueColorAlpha;
                        if (magic == 0x5AA521E0
                            && watchType != WatchType.RedmiWatch3
                            && watchType != WatchType.RedmiWatch4
                            && watchType != WatchType.MiBand8Pro)
                            magik.Transparent(MagickColor.FromRgba(0, 0, 0, 0xFF));
                        else
                            magik.Transparent(MagickColor.FromRgba(0, 0, 0, 0));
                        magik.Format = MagickFormat.Png32;

                        if (type == 2 &&
                            (
                                watchType == WatchType.RedmiWatch2 || watchType == WatchType.MiBand7Pro
                                //|| (watchType == WatchType.Gen2 && version == 0x800)
                            )
                           )
                        {
                            var pixelIndex = 0;
                            var rowIndex = 0;
                            foreach (var px in magik.GetPixels())
                                if (px.Channels == 4)
                                {
                                    if (rowIndex >= width)
                                        px.SetChannel(3, 0);
                                    else
                                        px.SetChannel(3, (ushort)((alfa[pixelIndex++] << 8) | 0xFF));

                                    rowIndex++;
                                    if (rowIndex >= magik.Width)
                                        rowIndex = 0;
                                }
                        }

                        magik.Write(pngFile);
                    }

                    File.Delete(bmpFile);

                    lst.Add(new FaceImage
                    {
                        Id = id,
                        Width = width,
                        Height = height,
                        Name = Path.GetFileName(pngFile)
                    });
                }
                catch (Exception ex)
                {
                    LogHelper.GotError = true;
                    Console.WriteLine("image processing err: " + ex);
                }

                offset += 0x10;
            }

            return lst;
        }

        private List<FaceImageList> ProcessImageList(WatchType watchType, byte[] data, uint offset, string path)
        {
            var blockCount = data.GetDWord(offset);
            var blockOffset = data.GetDWord(offset + 4);

            var lst = new List<FaceImageList>();

            offset = blockOffset;
            for (var i = 0; i < blockCount; i++)
            {
                try
                {
                    uint idx = data.GetWord(offset);
                    var id = data.GetDWord(offset);

                    var dataOfs = data.GetDWord(offset + 0x08);
                    var dataLen = data.GetDWord(offset + 0x0C);

                    var bin = data.GetByteArray(dataOfs, dataLen);

                    int width = bin.GetWord(0x04);
                    int height = bin.GetWord(0x06);

                    int rle = bin[0];
                    int cprType = bin[2];

                    var type = cprType;

                    if (rle == 0 && type == 0) type = 4;

                    var imgSize = (uint)width * 4 * (uint)height;
                    uint alfSize = 0;

                    if (watchType == WatchType.RedmiWatch2
                        || watchType == WatchType.RedmiBandPro
                        || watchType == WatchType.RedmiWatch3
                        || watchType == WatchType.RedmiWatch4
                        || watchType == WatchType.MiBand8Pro
                        || watchType == WatchType.MiWatchS3
                        || watchType == WatchType.MiBand9
                        || watchType == WatchType.MiBand7Pro)
                    {
                        if ((rle & 0x0F) == 0x04)
                        {
                            type = 2;
                            imgSize = (uint)width * 2 * (uint)height;
                            alfSize = (uint)width * (uint)height;
                        }

                        if ((watchType == WatchType.RedmiWatch3
                             || watchType == WatchType.RedmiWatch4
                             || watchType == WatchType.MiBand8Pro) && (rle & 0x0F) == 0x03)
                        {
                            type = 2;
                            imgSize = (uint)width * 2 * (uint)height;
                            alfSize = 0;
                        }

                        if (cprType == 0 && (watchType == WatchType.MiWatchS3 ||
                                             watchType == WatchType.MiBand9) && (rle & 0xFF) == 0x10)
                        {
                            // indexed image with color table
                            type = 1;
                            imgSize = (uint)width * (uint)height + 0x400;
                            alfSize = 0;
                        }
                    }

                    uint arrCount = bin[1];
                    var maxSize = bin.GetDWord(8) + 0x0C;
                    var magic = bin.GetDWord(0x0C + 4 * arrCount);

                    Console.WriteLine(
                        $"got compressed imageList[{path.GetLastDirectory()}:{idx}]: {dataOfs:X8}, type:{type:X2}, rle:{rle:X2}");

                    var nameList = new List<string>();

                    for (var j = 0; j < arrCount; j++)
                    {
                        var startOffset = 0x0C + (uint)(j * (imgSize + alfSize));
                        var leftSize = maxSize - startOffset >= imgSize
                            ? imgSize
                            : maxSize - startOffset;

                        byte[] pxls = null;
                        byte[] pxlsFull = null;
                        byte[] alfa = null;
                        byte[] clut = null;

                        if (magic == 0x5AA521E0)
                        {
                            var arrOffset = 0x0C + 4 * arrCount;
                            startOffset = arrOffset;
                            for (var x = 0; x < j; x++)
                                startOffset += bin.GetDWord(0x0C + (uint)(x * 4));

                            leftSize = maxSize - startOffset >= imgSize
                                ? imgSize
                                : maxSize - startOffset;

                            var cprLen = bin.GetDWord(0x0C + (uint)(j * 4));
                            var cpr = bin.GetByteArray(startOffset + 8, cprLen - 8);
                            type = bin[startOffset + 4] & 0x0F;
                            var decLen = (int)bin.GetDWord(startOffset + 4) >> 4;

                            if (rle == 0x10)
                            {
                                pxls = BmpHelper.UncompressRLEv20(cpr, decLen);
                            }
                            else
                            {
                                if (watchType == WatchType.RedmiWatch2 || watchType == WatchType.MiBand7Pro)
                                    pxls = BmpHelper.UncompressRLEv11(cpr, decLen, (byte)type);
                                else
                                    pxls = BmpHelper.UncompressRLEv10(cpr, decLen, (byte)type);
                            }

                            if (type == 1)
                            {
                                pxls = pxls.ConvertToRGBA();
                                type = 4;
                            }

                            pxlsFull = pxls;
                        }
                        else
                        {
                            pxls = bin.GetByteArray(startOffset, leftSize);
                            pxlsFull = new byte[imgSize];
                            Array.Copy(pxls, pxlsFull, pxls.Length);

                            if (type == 2 &&
                                (watchType == WatchType.RedmiWatch2 || watchType == WatchType.MiBand7Pro))
                            {
                                alfa = new byte[alfSize];
                                Array.Copy(bin.GetByteArray(startOffset + imgSize, alfSize), alfa, alfSize);
                            }

                            if (type == 1 && (watchType == WatchType.MiWatchS3 || watchType == WatchType.MiBand9))
                                pxlsFull = pxls.ConvertToRGBA();
                        }

                        byte[] bmp = null;
                        if (magic == 0x5AA521E0)
                            bmp = BmpHelper.ConvertToBmpGTRv2(pxls, width, height, type);
                        else
                            bmp = BmpHelper.ConvertToBmpGTR(pxlsFull, width, height,
                                type == 1 && clut == null ? 4 : type, clut);

                        var bmpFile = Path.Combine(path, $"img_arr_{idx:D4}_{j:D2}.bmp");
                        var pngFile = Path.Combine(path, $"img_arr_{idx:D4}_{j:D2}.png");
                        File.WriteAllBytes(bmpFile, bmp);

                        nameList.Add(Path.GetFileName(pngFile));

                        using (var magik = new MagickImage())
                        {
                            magik.Read(bmpFile);
                            magik.ColorType = ColorType.TrueColorAlpha;
                            if (magic == 0x5AA521E0)
                                magik.Transparent(MagickColor.FromRgba(0, 0, 0, 0xFF));
                            else
                                magik.Transparent(MagickColor.FromRgba(0, 0, 0, 0));
                            magik.Format = MagickFormat.Png32;

                            if (type == 2 &&
                                (watchType == WatchType.RedmiWatch2
                                 || watchType == WatchType.MiBand7Pro))
                            {
                                var pixelIndex = 0;
                                var rowIndex = 0;
                                foreach (var px in magik.GetPixels())
                                    if (px.Channels == 4)
                                    {
                                        if (rowIndex >= width)
                                            px.SetChannel(3, 0);
                                        else
                                            px.SetChannel(3, (ushort)((alfa[pixelIndex++] << 8) | 0xFF));

                                        rowIndex++;
                                        if (rowIndex >= magik.Width)
                                            rowIndex = 0;
                                    }
                            }

                            magik.Write(pngFile);
                        }

                        File.Delete(bmpFile);
                    }

                    lst.Add(new FaceImageList
                    {
                        Id = id,
                        Width = width,
                        Height = height,
                        NameList = nameList.ToArray()
                    });
                }
                catch (Exception ex)
                {
                    LogHelper.GotError = true;
                    Console.WriteLine("image processing err: " + ex);
                }

                offset += 0x10;
            }

            return lst;
        }
    }
}