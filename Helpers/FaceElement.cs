namespace UnpackMiColorFace
{
    internal class FaceElement
    {
        public FaceElement()
        {
        }

        public FaceElement(uint targetId, int posX = 0, int posY = 0)
        {
            TargetId = targetId;
            PosX = posX;
            PosY = posY;
        }

        public uint TargetId { get; internal set; }
        public int PosX { get; internal set; }
        public int PosY { get; internal set; }
    }
}