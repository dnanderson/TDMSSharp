// TdmsDataTypeReader.cs
using System;
using System.IO;
using System.Text;

namespace TdmsSharp
{
    /// <summary>
    /// Helper class to read TDMS data types from a stream with correct endianness.
    /// </summary>
    public static class TdmsDataTypeReader
    {
        private static byte[] ReadBytes(Stream stream, int count)
        {
            var buffer = new byte[count];
            int read = stream.Read(buffer, 0, count);
            if (read < count) throw new EndOfStreamException();
            return buffer;
        }

        public static T ReadData<T>(Stream stream, bool isBigEndian) where T : unmanaged
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            byte[] bytes = ReadBytes(stream, size);
            // On a little-endian system, we only need to swap if the file is big-endian.
            if (isBigEndian && size > 1)
            {
                Array.Reverse(bytes);
            }

            var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                return (T)System.Runtime.InteropServices.Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }

        public static T[] ReadArray<T>(Stream stream, int count, bool isBigEndian) where T : unmanaged
        {
            if (count == 0) return Array.Empty<T>();

            var array = new T[count];
            int typeSize = System.Runtime.InteropServices.Marshal.SizeOf<T>();

            var byteCount = count * typeSize;
            byte[] bytes = ReadBytes(stream, byteCount);

            // On a little-endian system, we only need to swap if the file is big-endian.
            if (isBigEndian && typeSize > 1)
            {
                for (int i = 0; i < count; i++)
                {
                    Array.Reverse(bytes, i * typeSize, typeSize);
                }
            }

            Buffer.BlockCopy(bytes, 0, array, 0, byteCount);
            return array;
        }

        public static string[] ReadStringArray(Stream stream, ulong numValues, bool isBigEndian)
        {
            if (numValues == 0) return Array.Empty<string>();

            uint[] endOffsets = new uint[numValues];
            for (ulong i = 0; i < numValues; i++)
            {
                byte[] offsetBytes = ReadBytes(stream, 4);
                if (isBigEndian) Array.Reverse(offsetBytes);
                endOffsets[i] = BitConverter.ToUInt32(offsetBytes, 0);
            }

            uint totalStringBytes = endOffsets.Length > 0 ? endOffsets[endOffsets.Length - 1] : 0;
            if (totalStringBytes == 0) return new string[numValues];

            byte[] stringData = ReadBytes(stream, (int)totalStringBytes);

            string[] result = new string[numValues];
            uint startOffset = 0;
            var encodingWithFallback = (Encoding)Encoding.UTF8.Clone();
            encodingWithFallback.DecoderFallback = new DecoderReplacementFallback("�");

            for (ulong i = 0; i < numValues; i++)
            {
                uint endOffset = endOffsets[i];
                if (endOffset < startOffset) throw new InvalidDataException("String offsets are not monotonically increasing.");

                uint length = endOffset - startOffset;
                if (length > 0)
                {
                    result[i] = encodingWithFallback.GetString(stringData, (int)startOffset, (int)length);
                }
                else
                {
                    result[i] = string.Empty;
                }
                startOffset = endOffset;
            }

            return result;
        }
    }
}