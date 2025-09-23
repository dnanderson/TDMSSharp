using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TDMSSharp
{
    /// <summary>
    /// Provides efficient, memory-mapped style access to channel data
    /// </summary>
    public class TdmsChannelView<T> : IEnumerable<T> where T : struct
    {
        private readonly TdmsChannel _channel;
        private readonly Func<long, long, T[]> _dataLoader;
        private readonly long _totalSamples;
        private readonly int _chunkSize;

        // LRU cache for loaded chunks
        private readonly Dictionary<long, T[]> _cache = new();
        private readonly LinkedList<long> _lruList = new();
        private readonly int _maxCachedChunks;

        /// <summary>
        /// Gets the total number of samples in the channel.
        /// </summary>
        public long Length => _totalSamples;

        /// <summary>
        /// Gets the underlying <see cref="TdmsChannel"/> for this view.
        /// </summary>
        public TdmsChannel Channel => _channel;

        internal TdmsChannelView(TdmsChannel channel, Func<long, long, T[]> dataLoader, long totalSamples, int chunkSize = 65536)
        {
            _channel = channel;
            _dataLoader = dataLoader;
            _totalSamples = totalSamples;
            _chunkSize = chunkSize;
            _maxCachedChunks = (int)Math.Max(10, 1_073_741_824 / (chunkSize * System.Runtime.InteropServices.Marshal.SizeOf<T>()));
        }

        /// <summary>
        /// Gets the sample at the specified index. This operation may be slow if the data is not already cached.
        /// </summary>
        /// <param name="index">The zero-based index of the sample to get.</param>
        /// <returns>The sample at the specified index.</returns>
        public T this[long index]
        {
            get
            {
                if (index < 0 || index >= _totalSamples)
                    throw new ArgumentOutOfRangeException(nameof(index));

                long chunkIndex = index / _chunkSize;
                int offsetInChunk = (int)(index % _chunkSize);

                var chunk = GetOrLoadChunk(chunkIndex);
                return chunk[offsetInChunk];
            }
        }

        /// <summary>
        /// Gets a range of samples from the channel. This is more efficient than accessing samples one by one.
        /// </summary>
        /// <param name="start">The zero-based starting index of the range.</param>
        /// <param name="count">The number of samples to get.</param>
        /// <returns>An array containing the requested range of samples.</returns>
        public T[] GetRange(long start, long count)
        {
            if (start < 0 || start + count > _totalSamples)
                throw new ArgumentOutOfRangeException();

            if (count > int.MaxValue)
                throw new ArgumentException("Count too large for single array");

            return _dataLoader(start, count);
        }

        /// <summary>
        /// Asynchronously gets a range of samples from the channel.
        /// </summary>
        /// <param name="start">The zero-based starting index of the range.</param>
        /// <param name="count">The number of samples to get.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an array with the requested range of samples.</returns>
        public Task<T[]> GetRangeAsync(long start, long count)
        {
            return Task.Run(() => GetRange(start, count));
        }

        /// <summary>
        /// Processes the channel data in chunks, which is useful for large datasets that do not fit into memory.
        /// </summary>
        /// <param name="processor">The action to perform on each chunk of data. The second parameter of the action is the offset of the chunk.</param>
        /// <param name="chunkSize">The size of the chunks to process. If 0, the default chunk size is used.</param>
        public void ProcessInChunks(Action<T[], long> processor, int chunkSize = 0)
        {
            if (chunkSize <= 0) chunkSize = _chunkSize;

            for (long offset = 0; offset < _totalSamples; offset += chunkSize)
            {
                long count = Math.Min(chunkSize, _totalSamples - offset);
                var data = _dataLoader(offset, count);
                processor(data, offset);
            }
        }

        /// <summary>
        /// Asynchronously processes the channel data in chunks.
        /// </summary>
        /// <param name="processor">The asynchronous action to perform on each chunk of data. The second parameter of the action is the offset of the chunk.</param>
        /// <param name="chunkSize">The size of the chunks to process. If 0, the default chunk size is used.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task ProcessInChunksAsync(Func<T[], long, Task> processor, int chunkSize = 0)
        {
            if (chunkSize <= 0) chunkSize = _chunkSize;

            for (long offset = 0; offset < _totalSamples; offset += chunkSize)
            {
                long count = Math.Min(chunkSize, _totalSamples - offset);
                var data = await Task.Run(() => _dataLoader(offset, count));
                await processor(data, offset);
            }
        }

        /// <summary>
        /// Processes the channel data in parallel, which can significantly speed up processing on multi-core systems.
        /// </summary>
        /// <param name="processor">The action to perform on each chunk of data. The second parameter of the action is the offset of the chunk.</param>
        /// <param name="degreeOfParallelism">The number of parallel tasks to use. If -1, the number of available processors is used.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public Task ProcessInParallel(Action<T[], long> processor, int degreeOfParallelism = -1)
        {
            if (degreeOfParallelism <= 0)
                degreeOfParallelism = Environment.ProcessorCount;

            var chunks = new List<(long start, long count)>();
            long chunkSize = (_totalSamples + degreeOfParallelism - 1) / degreeOfParallelism;

            for (long offset = 0; offset < _totalSamples; offset += chunkSize)
            {
                chunks.Add((offset, Math.Min(chunkSize, _totalSamples - offset)));
            }

            return Task.Run(() =>
            {
                var parallelOptions = new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = degreeOfParallelism 
                };

                Parallel.ForEach(chunks, parallelOptions, chunk =>
                {
                    var data = _dataLoader(chunk.start, chunk.count);
                    processor(data, chunk.start);
                });
            });
        }

        /// <summary>
        /// Computes statistics for the channel data without loading the entire dataset into memory.
        /// </summary>
        /// <returns>A <see cref="ChannelStatistics"/> object containing the computed statistics.</returns>
        /// <exception cref="InvalidOperationException">Thrown when statistics are computed for a non-numeric data type.</exception>
        public ChannelStatistics ComputeStatistics()
        {
            if (!IsNumericType())
                throw new InvalidOperationException("Statistics can only be computed for numeric types");

            double sum = 0;
            double sumOfSquares = 0;
            double min = double.MaxValue;
            double max = double.MinValue;
            long count = 0;

            ProcessInChunks((chunk, offset) =>
            {
                foreach (var value in chunk)
                {
                    double v = Convert.ToDouble(value);
                    sum += v;
                    sumOfSquares += v * v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                    count++;
                }
            });

            double mean = sum / count;
            double variance = (sumOfSquares / count) - (mean * mean);
            double stdDev = Math.Sqrt(variance);

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
        /// Decimates the data to a specified number of samples, which is useful for creating visualizations of large datasets.
        /// </summary>
        /// <param name="targetSamples">The target number of samples.</param>
        /// <returns>An array containing the decimated data.</returns>
        public T[] Decimate(int targetSamples)
        {
            if (targetSamples >= _totalSamples)
                return GetRange(0, _totalSamples);

            var result = new T[targetSamples];
            double step = (double)_totalSamples / targetSamples;

            for (int i = 0; i < targetSamples; i++)
            {
                long index = (long)(i * step);
                result[i] = this[index];
            }

            return result;
        }

        /// <summary>
        /// Decimates the data by calculating the minimum and maximum values for each block of samples. This is particularly useful for visualizing waveforms.
        /// </summary>
        /// <param name="targetPairs">The target number of min/max pairs.</param>
        /// <returns>A tuple containing two arrays: one for the minimum values and one for the maximum values.</returns>
        /// <exception cref="InvalidOperationException">Thrown when min/max decimation is attempted on a non-numeric data type.</exception>
        public (T[] mins, T[] maxs) DecimateMinMax(int targetPairs)
        {
            if (!IsNumericType())
                throw new InvalidOperationException("Min/max decimation requires numeric type");

            var mins = new T[targetPairs];
            var maxs = new T[targetPairs];
            long samplesPerPair = _totalSamples / targetPairs;

            Parallel.For(0, targetPairs, i =>
            {
                long start = i * samplesPerPair;
                long end = Math.Min(start + samplesPerPair, _totalSamples);
                var data = _dataLoader(start, end - start);

                T min = data[0];
                T max = data[0];

                for (int j = 1; j < data.Length; j++)
                {
                    if (Comparer<T>.Default.Compare(data[j], min) < 0) min = data[j];
                    if (Comparer<T>.Default.Compare(data[j], max) > 0) max = data[j];
                }

                mins[i] = min;
                maxs[i] = max;
            });

            return (mins, maxs);
        }

        private T[] GetOrLoadChunk(long chunkIndex)
        {
            // Check cache
            if (_cache.TryGetValue(chunkIndex, out var cached))
            {
                // Move to front of LRU list
                _lruList.Remove(chunkIndex);
                _lruList.AddFirst(chunkIndex);
                return cached;
            }

            // Load chunk
            long start = chunkIndex * _chunkSize;
            long count = Math.Min(_chunkSize, _totalSamples - start);
            var chunk = _dataLoader(start, count);

            // Add to cache
            _cache[chunkIndex] = chunk;
            _lruList.AddFirst(chunkIndex);

            // Evict if necessary
            while (_lruList.Count > _maxCachedChunks)
            {
                var toEvict = _lruList.Last!.Value;
                _lruList.RemoveLast();
                _cache.Remove(toEvict);
            }

            return chunk;
        }

        private bool IsNumericType()
        {
            var type = typeof(T);
            return type == typeof(sbyte) || type == typeof(byte) ||
                   type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong) ||
                   type == typeof(float) || type == typeof(double);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the samples in the channel.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the samples.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            for (long i = 0; i < _totalSamples; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Statistics computed from channel data
    /// </summary>
    public class ChannelStatistics
    {
        /// <summary>
        /// Gets or sets the total number of samples.
        /// </summary>
        public long Count { get; set; }

        /// <summary>
        /// Gets or sets the minimum value.
        /// </summary>
        public double Min { get; set; }

        /// <summary>
        /// Gets or sets the maximum value.
        /// </summary>
        public double Max { get; set; }

        /// <summary>
        /// Gets or sets the mean (average) value.
        /// </summary>
        public double Mean { get; set; }

        /// <summary>
        /// Gets or sets the standard deviation.
        /// </summary>
        public double StdDev { get; set; }

        /// <summary>
        /// Gets or sets the sum of all values.
        /// </summary>
        public double Sum { get; set; }
    }
}