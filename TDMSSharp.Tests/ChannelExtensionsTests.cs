using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TDMSSharp;
using Xunit;

namespace TDMSSharp.Tests
{
    public class ChannelExtensionsTests
    {
        [Fact]
        public void AsView_ShouldReturnCorrectViewForNumericTypes()
        {
            var channel = new TdmsChannel<int>("TestChannel");
            channel.AddDataChunk(new int[] { 1, 2, 3 });
            var view = channel.AsView<int>();
            Assert.NotNull(view);
            Assert.Equal(3, view.Length);
        }

        [Fact]
        public void AsView_ThrowsInvalidCastException()
        {
            var channel = new TdmsChannel<int>("TestChannel");
            channel.AddDataChunk(new int[] { 1, 2, 3 });
            Assert.Throws<InvalidCastException>(() => channel.AsView<double>());
        }

        [Fact]
        public void AsView_ThrowsInvalidOperationException()
        {
            var channel = new TdmsChannel("TestChannel") { DataType = TdsDataType.I32 };
            Assert.Throws<InvalidOperationException>(() => channel.AsView<int>());
        }

        [Fact]
        public void GetTypedData_ShouldReturnCorrectData()
        {
            var channel = new TdmsChannel<double>("TestChannel");
            channel.AddDataChunk(new double[] { 1.0, 2.0, 3.0 });
            var data = channel.GetTypedData<double>();
            Assert.Equal(new double[] { 1.0, 2.0, 3.0 }, data);
        }

        [Fact]
        public void GetTypedData_FromBaseChannel()
        {
            var channel = new TdmsChannel("TestChannel") { DataType = TdsDataType.I32 };
            channel.Data = new int[] { 1, 2, 3 };
            var data = channel.GetTypedData<int>();
            Assert.Equal(new int[] { 1, 2, 3 }, data);
        }

        [Fact]
        public async Task StreamData_ShouldStreamDataInChunks()
        {
            var channel = new TdmsChannel<float>("TestChannel");
            channel.AddDataChunk(Enumerable.Range(0, 100).Select(x => (float)x).ToArray());
            var chunks = await channel.StreamData<float>(10).ToListAsync();
            Assert.Equal(10, chunks.Count);
            Assert.All(chunks, chunk => Assert.Equal(10, chunk.Length));
        }

        [Fact]
        public void Transform_ShouldApplyFunctionToData()
        {
            var channel = new TdmsChannel<int>("TestChannel");
            channel.AddDataChunk(new int[] { 1, 2, 3 });
            var newChannel = channel.Transform<int, int>(x => x * 2);
            Assert.Equal(new int[] { 2, 4, 6 }, newChannel.GetData<int>());
        }

        [Fact]
        public void Transform_ThrowsInvalidOperationException_WhenNoData()
        {
            var channel = new TdmsChannel<int>("TestChannel");
            Assert.Throws<InvalidOperationException>(() => channel.Transform<int, int>(x => x * 2));
        }

        [Fact]
        public void Where_ShouldFilterData()
        {
            var channel = new TdmsChannel<int>("TestChannel");
            channel.AddDataChunk(new int[] { 1, 2, 3, 4, 5 });
            var newChannel = channel.Where<int>(x => x % 2 == 0);
            Assert.Equal(new int[] { 2, 4 }, newChannel.GetData<int>());
        }

        [Fact]
        public void Where_ThrowsInvalidOperationException_WhenNoData()
        {
            var channel = new TdmsChannel<int>("TestChannel");
            Assert.Throws<InvalidOperationException>(() => channel.Where<int>(x => x % 2 == 0));
        }

        [Fact]
        public void WindowedStatistics_ShouldReturnCorrectStatistics()
        {
            var channel = new TdmsChannel<double>("TestChannel");
            channel.AddDataChunk(new double[] { 1, 2, 3, 4, 5 });
            var stats = channel.WindowedStatistics<double>(5).First();
            Assert.Equal(5, stats.Count);
            Assert.Equal(1, stats.Min);
            Assert.Equal(5, stats.Max);
            Assert.Equal(3, stats.Mean);
            Assert.Equal(15, stats.Sum);
            Assert.Equal(Math.Sqrt(2), stats.StdDev, 5);
        }

        [Fact]
        public void Resample_ShouldResampleData()
        {
            var channel = new TdmsChannel<double>("TestChannel");
            channel.AddDataChunk(new double[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            var resampled = channel.Resample<double>(10, 5);
            Assert.Equal(5, resampled.GetData<double>().Length);
        }

        [Fact]
        public void Resample_WithNearestNeighbor()
        {
            var channel = new TdmsChannel<double>("TestChannel");
            channel.AddDataChunk(new double[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            var resampled = channel.Resample<double>(10, 5, InterpolationType.NearestNeighbor);
            Assert.Equal(5, resampled.GetData<double>().Length);
        }

        [Theory]
        [InlineData(WindowFunction.Hamming, 10)]
        [InlineData(WindowFunction.Hanning, 10)]
        [InlineData(WindowFunction.Blackman, 10)]
        [InlineData(WindowFunction.Rectangular, 10)]
        public void ApplyWindow_ShouldApplySelectedWindow(WindowFunction window, int size)
        {
            var channel = new TdmsChannel<double>("TestChannel");
            channel.AddDataChunk(Enumerable.Repeat(1.0, size).ToArray());
            var windowed = channel.ApplyWindow<double>(window, size);
            Assert.Equal(size, windowed.Length);
        }
    }
}
