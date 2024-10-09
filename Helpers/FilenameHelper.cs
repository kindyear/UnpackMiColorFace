using System.IO;
using XiaomiWatch.Common;

namespace UnpackMiColorFace.Helpers
{
    internal class FilenameHelper
    {
        private readonly string filename;

        public FilenameHelper(string filename)
        {
            this.filename = filename;
        }

        public string NameNoExt => Path.GetFileNameWithoutExtension(filename);

        public string GetFaceSlotImagesFolder(WatchType watchType, int slotId, int subversion)
        {
            var baseFolder = NameNoExt;

            return watchType switch
            {
                WatchType.Gen3 => Path.Combine(baseFolder, "images"),
                WatchType.MiWatchS3 => Path.Combine(baseFolder, "images"),
                WatchType.MiBand9 => Path.Combine(baseFolder, "images"),
                WatchType.MiBand8Pro => Path.Combine(baseFolder, "images"),
                _ => slotId == 0 ? Path.Combine(baseFolder, "images") :
                    slotId == 1 ? Path.Combine(baseFolder, "AOD", "images") :
                    Path.Combine(baseFolder, $"images_{slotId}")
            };
        }

        internal string GetFaceSlotFilename(WatchType watchType, int slotId, int subversion)
        {
            var facefile = slotId > 0 ? $"{NameNoExt}_{slotId}" : NameNoExt;

            if (watchType == WatchType.Gen3
                || watchType == WatchType.MiWatchS3
                || watchType == WatchType.MiBand9
                || watchType == WatchType.MiBand8Pro)
            {
                if ((subversion & 0x04) > 0 && slotId == 1)
                    facefile = $"{NameNoExt}_AOD";
            }
            else
            {
                if (slotId == 1) facefile = Path.Combine("AOD", NameNoExt); // 使用Path.Combine
            }

            return facefile;
        }
    }
}