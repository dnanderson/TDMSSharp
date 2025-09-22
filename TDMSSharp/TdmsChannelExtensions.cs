using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TDMSSharp
{
    /// <summary>
    /// Extension methods for more ergonomic channel data access
    /// </summary>
    public static class TdmsChannelExtensions
    {
        /// <summary>
        /// Get a view of the channel data for efficient access
        /// </summary>
        public static TdmsChannelView<T> AsView<T>(this TdmsChannel channel) where T : struct
        {
            if (channel.DataType != TdsDataTypeProvider.GetDataType<T>())
                throw new InvalidCastException($"Channel data type {channel.DataType} does not match requested type {typeof(T).Name}");

            T[]? dataArray = null;

            // First, check the base Data property which might already be populated
            if (channel.Data is T[] data)
            {
                dataArray = data;
            }
            // If that's null, check if it's a generic channel and access its Data property.
            // This will trigger the chunk combination logic inside TdmsChannel<T>.
            else if (channel is TdmsChannel<T> typedChannel)
            {
                dataArray = typedChannel.Data;
            }

            // If we now have data, create the view
            if (dataArray != null)
            {
                return new TdmsChannelView<T>(
                    channel,
                    (start, count) => dataArray.Skip((int)start).Take((int)count).ToArray(),
                    dataArray.Length
                );
            }

            // If data is still null, then it's a true lazy-load scenario that this method doesn't support without more info.
            throw new InvalidOperationException("Channel data not loaded. Use lazy loading for view access.");
        }

        /// <summary>
        /// Get typed data with automatic casting
        /// </summary>
        public static T[]? GetTypedData<T>(this TdmsChannel channel) where T : struct
        {
            if (channel is TdmsChannel<T> typedChannel)
                return typedChannel.Data;

            if (channel.Data is T[] data)
                return data;

            return null;
        }
        /// <summary>
        /// Stream channel data in batches
        /// </summary>
        public static IAsyncEnumerable<T[]> StreamData<T>(this TdmsChannel channel, int batchSize = 65536) where T : struct
        {
            return StreamDataImpl<T>(channel, batchSize);
        }

        private static async IAsyncEnumerable<T[]> StreamDataImpl<T>(TdmsChannel channel, int batchSize) where T : struct
        {
            var data = channel.GetTypedData<T>();
            if (data == null) yield break;

            for (int offset = 0; offset < data.Length; offset += batchSize)
            {
                int count = Math.Min(batchSize, data.Length - offset);
                var batch = new T[count];
                Array.Copy(data, offset, batch, 0, count);
                yield return batch;

                // Allow other operations to proceed
                await Task.Yield();
            }
        }

        /// <summary>
        /// Apply a transformation to channel data
        /// </summary>
        public static TdmsChannel<TResult> Transform<TSource, TResult>(
            this TdmsChannel channel,
            Func<TSource, TResult> transformer)
            where TSource : struct
            where TResult : struct
        {
            var sourceData = channel.GetTypedData<TSource>();
            if (sourceData == null)
                throw new InvalidOperationException("No data available in channel");

            var resultChannel = new TdmsChannel<TResult>(channel.Path + "_transformed");
            var resultData = new TResult[sourceData.Length];

            Parallel.For(0, sourceData.Length, i =>
            {
                resultData[i] = transformer(sourceData[i]);
            });

            resultChannel.Data = resultData;
            return resultChannel;
        }

        /// <summary>
        /// Filter channel data
        /// </summary>
        public static TdmsChannel<T> Where<T>(this TdmsChannel channel, Func<T, bool> predicate) where T : struct
        {
            var sourceData = channel.GetTypedData<T>();
            if (sourceData == null)
                throw new InvalidOperationException("No data available in channel");

            var filtered = sourceData.Where(predicate).ToArray();
            var resultChannel = new TdmsChannel<T>(channel.Path + "_filtered")
            {
                Data = filtered
            };

            return resultChannel;
        }

        /// <summary>
        /// Compute windowed statistics
        /// </summary>
        public static IEnumerable<ChannelStatistics> WindowedStatistics<T>(
            this TdmsChannel channel,
            int windowSize,
            int stepSize = 0) where T : struct, IConvertible
        {
            if (stepSize <= 0) stepSize = windowSize;

            var data = channel.GetTypedData<T>();
            if (data == null) yield break;

            for (int start = 0; start < data.Length; start += stepSize)
            {
                int end = Math.Min(start + windowSize, data.Length);
                var window = data.Skip(start).Take(end - start);

                var stats = ComputeStatistics(window);
                yield return stats;

                if (end >= data.Length) break;
            }
        }

        private static ChannelStatistics ComputeStatistics<T>(IEnumerable<T> data) where T : struct, IConvertible
        {
            double sum = 0;
            double sumOfSquares = 0;
            double min = double.MaxValue;
            double max = double.MinValue;
            long count = 0;

            foreach (var value in data)
            {
                double v = value.ToDouble(null);
                sum += v;
                sumOfSquares += v * v;
                if (v < min) min = v;
                if (v > max) max = v;
                count++;
            }

            if (count == 0)
            {
                return new ChannelStatistics
                {
                    Count = 0,
                    Min = 0,
                    Max = 0,
                    Mean = 0,
                    StdDev = 0,
                    Sum = 0
                };
            }

            double mean = sum / count;
            double variance = (sumOfSquares / count) - (mean * mean);
            double stdDev = Math.Sqrt(Math.Max(0, variance)); // Ensure non-negative

            return new ChannelStatistics
            {
                Count = count,
                Min = min,
                Max = max,
                Mean = mean,
                StdDev = stdDev,
                Sum = sum
            };
        }

        /// <summary>
        /// Resample channel data to a new sample rate
        /// </summary>
        public static TdmsChannel<T> Resample<T>(
            this TdmsChannel channel,
            double originalSampleRate,
            double targetSampleRate,
            InterpolationType interpolation = InterpolationType.Linear) where T : struct, IConvertible
        {
            var sourceData = channel.GetTypedData<T>();
            if (sourceData == null)
                throw new InvalidOperationException("No data available in channel");

            double ratio = targetSampleRate / originalSampleRate;
            int targetLength = (int)(sourceData.Length * ratio);
            var result = new T[targetLength];

            if (interpolation == InterpolationType.Linear && IsNumericType<T>())
            {
                for (int i = 0; i < targetLength; i++)
                {
                    double sourceIndex = i / ratio;
                    int index0 = (int)sourceIndex;
                    int index1 = Math.Min(index0 + 1, sourceData.Length - 1);
                    double fraction = sourceIndex - index0;

                    double v0 = sourceData[index0].ToDouble(null);
                    double v1 = sourceData[index1].ToDouble(null);
                    double interpolated = v0 + (v1 - v0) * fraction;

                    result[i] = (T)Convert.ChangeType(interpolated, typeof(T));
                }
            }
            else
            {
                // Nearest neighbor for non-numeric or when specified
                for (int i = 0; i < targetLength; i++)
                {
                    int sourceIndex = (int)Math.Round(i / ratio);
                    sourceIndex = Math.Min(sourceIndex, sourceData.Length - 1);
                    result[i] = sourceData[sourceIndex];
                }
            }

            var resultChannel = new TdmsChannel<T>(channel.Path + "_resampled")
            {
                Data = result
            };

            // Copy properties and update sample rate if present
            foreach (var prop in channel.Properties)
            {
                if (prop.Name == "wf_increment")
                {
                    resultChannel.AddProperty("wf_increment", 1.0 / targetSampleRate);
                }
                else
                {
                    resultChannel.Properties.Add(prop);
                }
            }

            return resultChannel;
        }

        /// <summary>
        /// Apply a sliding window function
        /// </summary>
        public static T[] ApplyWindow<T>(
            this TdmsChannel channel,
            WindowFunction window,
            int windowSize) where T : struct, IConvertible
        {
            var data = channel.GetTypedData<T>();
            if (data == null)
                throw new InvalidOperationException("No data available in channel");

            var result = new T[data.Length];
            var windowCoefficients = GenerateWindow(window, windowSize);

            Parallel.For(0, data.Length, i =>
            {
                if (i < windowSize / 2 || i >= data.Length - windowSize / 2)
                {
                    result[i] = data[i]; // Keep edge values unchanged
                }
                else
                {
                    double sum = 0;
                    for (int j = 0; j < windowSize; j++)
                    {
                        int dataIndex = i - windowSize / 2 + j;
                        sum += data[dataIndex].ToDouble(null) * windowCoefficients[j];
                    }
                    result[i] = (T)Convert.ChangeType(sum, typeof(T));
                }
            });

            return result;
        }

        private static double[] GenerateWindow(WindowFunction window, int size)
        {
            var coefficients = new double[size];

            switch (window)
            {
                case WindowFunction.Hamming:
                    for (int i = 0; i < size; i++)
                    {
                        coefficients[i] = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (size - 1));
                    }
                    break;
                case WindowFunction.Hanning:
                    for (int i = 0; i < size; i++)
                    {
                        coefficients[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (size - 1)));
                    }
                    break;
                case WindowFunction.Blackman:
                    for (int i = 0; i < size; i++)
                    {
                        coefficients[i] = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (size - 1))
                                        + 0.08 * Math.Cos(4 * Math.PI * i / (size - 1));
                    }
                    break;
                default:
                    for (int i = 0; i < size; i++)
                    {
                        coefficients[i] = 1.0; // Rectangular window
                    }
                    break;
            }

            // Normalize
            double sum = coefficients.Sum();
            for (int i = 0; i < size; i++)
            {
                coefficients[i] /= sum;
            }

            return coefficients;
        }

        private static bool IsNumericType<T>()
        {
            var type = typeof(T);
            return type == typeof(sbyte) || type == typeof(byte) ||
                   type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong) ||
                   type == typeof(float) || type == typeof(double);
        }
    }

    public enum InterpolationType
    {
        NearestNeighbor,
        Linear
    }

    public enum WindowFunction
    {
        Rectangular,
        Hamming,
        Hanning,
        Blackman
    }
}