namespace TDMSSharp;
/// <summary>
/// Represents the bit mask for the Table of Contents (ToC)
/// in a TDMS file segment's lead-in.
/// </summary>
[Flags]
public enum TocFlags : uint
{
    /// <summary>
    /// No flags set.
    /// </summary>
    None = 0,

    /// <summary>
    /// Segment contains meta data. (Bit 1)
    /// </summary>
    MetaData = 1 << 1, // 0x2

    /// <summary>
    /// Segment contains a new list of objects. (Bit 2)
    /// </summary>
    NewObjList = 1 << 2, // 0x4

    /// <summary>
    /// Segment contains raw data. (Bit 3)
    /// </summary>
    RawData = 1 << 3, // 0x8

    /// <summary>
    /// Raw data in the segment is interleaved. (Bit 5)
    /// </summary>
    InterleavedData = 1 << 5, // 0x20

    /// <summary>
    /// Numeric values are in big-endian format. (Bit 6)
    /// </summary>
    BigEndian = 1 << 6, // 0x40

    /// <summary>
    /// Segment contains DAQmx raw data. (Bit 7)
    /// </summary>
    DAQmxRawData = 1 << 7 // 0x80
}