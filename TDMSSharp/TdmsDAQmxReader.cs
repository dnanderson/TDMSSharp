using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;

namespace TDMSSharp
{
    /// <summary>
    /// Specialized reader for DAQmx raw data format
    /// </summary>
    public static class TdmsDAQmxReader
    {
        /// <summary>
        /// Read DAQmx raw data and extract values based on scalers
        /// </summary>
        public static void ReadDAQmxData(BinaryReader reader, TdmsChannel channel, TdmsDAQmxRawDataIndex daqmxIndex)
        {
            if (daqmxIndex.NumberOfValues == 0 || daqmxIndex.Scalers.Length == 0)
                return;

            var primaryDataType = daqmxIndex.GetPrimaryDataType();
            var strideSize = daqmxIndex.GetStrideSize();
            var valueCount = (int)daqmxIndex.NumberOfValues;

            // For simple cases with single scaler, use optimized path
            if (daqmxIndex.Scalers.Length == 1 && strideSize == daqmxIndex.Scalers[0].ByteSize)
            {
                ReadSimpleDAQmxData(reader, channel, primaryDataType, valueCount);
                return;
            }

            // Complex case: multiple scalers or interleaved data
            ReadInterleavedDAQmxData(reader, channel, daqmxIndex, strideSize, valueCount);
        }

        /// <summary>
        /// Optimized path for simple DAQmx data (single scaler, contiguous)
        /// </summary>
        private static void ReadSimpleDAQmxData(BinaryReader reader, TdmsChannel channel, TdsDataType dataType, int valueCount)
        {
            switch (dataType)
            {
                case TdsDataType.I32:
                    ReadPrimitiveDAQmx<int>(reader, channel, valueCount);
                    break;
                case TdsDataType.DoubleFloat:
                    ReadPrimitiveDAQmx<double>(reader, channel, valueCount);
                    break;
                case TdsDataType.SingleFloat:
                    ReadPrimitiveDAQmx<float>(reader, channel, valueCount);
                    break;
                case TdsDataType.I16:
                    ReadPrimitiveDAQmx<short>(reader, channel, valueCount);
                    break;
                case TdsDataType.U16:
                    ReadPrimitiveDAQmx<ushort>(reader, channel, valueCount);
                    break;
                case TdsDataType.I8:
                    ReadPrimitiveDAQmx<sbyte>(reader, channel, valueCount);
                    break;
                case TdsDataType.U8:
                    ReadPrimitiveDAQmx<byte>(reader, channel, valueCount);
                    break;
                case TdsDataType.I64:
                    ReadPrimitiveDAQmx<long>(reader, channel, valueCount);
                    break;
                case TdsDataType.U64:
                    ReadPrimitiveDAQmx<ulong>(reader, channel, valueCount);
                    break;
                default:
                    throw new NotSupportedException($"DAQmx data type {dataType} not supported for simple read");
            }
        }

        /// <summary>
        /// Read primitive type DAQmx data with optimizations
        /// </summary>
        private static void ReadPrimitiveDAQmx<T>(BinaryReader reader, TdmsChannel channel, int valueCount) where T : struct
        {
            var typedChannel = channel as TdmsChannel<T>;
            if (typedChannel == null)
            {
                // Create typed channel if needed
                typedChannel = new TdmsChannel<T>(channel.Path)
                {
                    DataType = channel.DataType,
                    NumberOfValues = channel.NumberOfValues
                };
                // Copy properties
                foreach (var prop in channel.Properties)
                    typedChannel.Properties.Add(prop);
            }

            var existingLength = typedChannel.Data?.Length ?? 0;
            var newArray = new T[existingLength + valueCount];
            
            if (existingLength > 0)
            {
                Array.Copy(typedChannel.Data, newArray, existingLength);
            }

            var span = newArray.AsSpan(existingLength, valueCount);
            var byteCount = System.Runtime.InteropServices.Marshal.SizeOf<T>() * valueCount;
            
            // Use pooled buffer for reading
            var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(byteCount, 81920));
            try
            {
                var byteSpan = System.Runtime.InteropServices.MemoryMarshal.AsBytes(span);
                int totalRead = 0;
                
                while (totalRead < byteCount)
                {
                    int toRead = Math.Min(byteCount - totalRead, buffer.Length);
                    int read = reader.BaseStream.Read(buffer, 0, toRead);
                    if (read == 0) break;
                    
                    buffer.AsSpan(0, read).CopyTo(byteSpan.Slice(totalRead));
                    totalRead += read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            typedChannel.Data = newArray;
            
            // Update the original channel reference
            if (channel != typedChannel)
            {
                channel.Data = newArray;
            }
        }

        /// <summary>
        /// Read interleaved DAQmx data with multiple scalers
        /// </summary>
        private static void ReadInterleavedDAQmxData(BinaryReader reader, TdmsChannel channel, 
            TdmsDAQmxRawDataIndex daqmxIndex, int strideSize, int valueCount)
        {
            // Use the first scaler to determine the output data type
            var primaryScaler = daqmxIndex.Scalers[0];
            var outputDataType = primaryScaler.DataType;
            
            // Read all raw data into buffer
            var totalBytes = strideSize * valueCount;
            var rawBuffer = ArrayPool<byte>.Shared.Rent(totalBytes);
            
            try
            {
                int totalRead = 0;
                while (totalRead < totalBytes)
                {
                    int read = reader.BaseStream.Read(rawBuffer, totalRead, totalBytes - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }

                // Extract values based on scaler configuration
                ExtractScalerValues(channel, rawBuffer, daqmxIndex, strideSize, valueCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rawBuffer);
            }
        }

        /// <summary>
        /// Extract values from raw buffer based on scaler configuration
        /// </summary>
        private static void ExtractScalerValues(TdmsChannel channel, byte[] rawBuffer, 
            TdmsDAQmxRawDataIndex daqmxIndex, int strideSize, int valueCount)
        {
            var primaryScaler = daqmxIndex.Scalers[0];
            
            switch (primaryScaler.DataType)
            {
                case TdsDataType.DoubleFloat:
                    ExtractValues<double>(channel, rawBuffer, daqmxIndex, strideSize, valueCount);
                    break;
                case TdsDataType.SingleFloat:
                    ExtractValues<float>(channel, rawBuffer, daqmxIndex, strideSize, valueCount);
                    break;
                case TdsDataType.I32:
                    ExtractValues<int>(channel, rawBuffer, daqmxIndex, strideSize, valueCount);
                    break;
                case TdsDataType.I16:
                    ExtractValues<short>(channel, rawBuffer, daqmxIndex, strideSize, valueCount);
                    break;
                case TdsDataType.U16:
                    ExtractValues<ushort>(channel, rawBuffer, daqmxIndex, strideSize, valueCount);
                    break;
                case TdsDataType.I8:
                    ExtractValues<sbyte>(channel, rawBuffer, daqmxIndex, strideSize, valueCount);
                    break;
                case TdsDataType.U8:
                    ExtractValues<byte>(channel, rawBuffer, daqmxIndex, strideSize, valueCount);
                    break;
                default:
                    throw new NotSupportedException($"DAQmx extraction for type {primaryScaler.DataType} not supported");
            }
        }

        /// <summary>
        /// Generic extraction of values from interleaved buffer
        /// </summary>
        private static void ExtractValues<T>(TdmsChannel channel, byte[] rawBuffer, 
            TdmsDAQmxRawDataIndex daqmxIndex, int strideSize, int valueCount) where T : struct
        {
            var typedChannel = channel as TdmsChannel<T>;
            if (typedChannel == null)
            {
                typedChannel = new TdmsChannel<T>(channel.Path)
                {
                    DataType = channel.DataType,
                    NumberOfValues = channel.NumberOfValues
                };
                foreach (var prop in channel.Properties)
                    typedChannel.Properties.Add(prop);
            }

            var existingLength = typedChannel.Data?.Length ?? 0;
            var newArray = new T[existingLength + valueCount];
            
            if (existingLength > 0)
            {
                Array.Copy(typedChannel.Data, newArray, existingLength);
            }

            // Extract values based on scaler offset
            var primaryScaler = daqmxIndex.Scalers[0];
            var offset = (int)primaryScaler.RawByteOffsetWithinStride;
            var elementSize = primaryScaler.ByteSize;

            for (int i = 0; i < valueCount; i++)
            {
                int bufferPos = i * strideSize + offset;
                newArray[existingLength + i] = ReadValueFromBuffer<T>(rawBuffer, bufferPos);
            }

            typedChannel.Data = newArray;
            channel.Data = newArray;
        }

        /// <summary>
        /// Read a single value from buffer at specified position
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T ReadValueFromBuffer<T>(byte[] buffer, int offset) where T : struct
        {
            return typeof(T) switch
            {
                Type t when t == typeof(double) => (T)(object)BitConverter.ToDouble(buffer, offset),
                Type t when t == typeof(float) => (T)(object)BitConverter.ToSingle(buffer, offset),
                Type t when t == typeof(int) => (T)(object)BitConverter.ToInt32(buffer, offset),
                Type t when t == typeof(short) => (T)(object)BitConverter.ToInt16(buffer, offset),
                Type t when t == typeof(ushort) => (T)(object)BitConverter.ToUInt16(buffer, offset),
                Type t when t == typeof(byte) => (T)(object)buffer[offset],
                Type t when t == typeof(sbyte) => (T)(object)(sbyte)buffer[offset],
                Type t when t == typeof(long) => (T)(object)BitConverter.ToInt64(buffer, offset),
                Type t when t == typeof(ulong) => (T)(object)BitConverter.ToUInt64(buffer, offset),
                _ => throw new NotSupportedException($"Type {typeof(T)} not supported")
            };
        }
    }
}