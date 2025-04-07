namespace Poc.BufferCircularVideo
{
    /// <summary>
    /// Utilizado para salvar os Nal Unitys em um buffer circular.
    /// </summary>
    public class H264Packet
    {
        public byte[] Data { get; set; }
        public DateTime Timestamp { get; set; }
        // public bool IsKeyFrame => Data.Length > 4 && (Data[0] & 0x1F) == 5; // NAL unit type 5 (IDR)
        public bool IsKeyFrame { get; set; }
    }
}
