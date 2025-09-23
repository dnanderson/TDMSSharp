using System.IO;
using System.Linq;
using TDMSSharp;
using Xunit;

namespace TDMSSharp.Tests
{
    public class ReaderTests
    {
        [Fact]
        public void ReadExampleFile()
        {
            var file = TdmsFile.Open("example.tdms");

            Assert.NotNull(file);
            Assert.Equal(2, file.ChannelGroups.Count);

            var group1 = file.ChannelGroups.FirstOrDefault(g => g.Path == "/'Group 1'");
            Assert.NotNull(group1);
            Assert.Equal(2, group1.Channels.Count);

            var counterChannel = group1.Channels.FirstOrDefault(c => c.Path.EndsWith("'Counter'"));
            Assert.NotNull(counterChannel);
            Assert.Equal(1000u, counterChannel.NumberOfValues);

            var randomChannel = group1.Channels.FirstOrDefault(c => c.Path.EndsWith("'Random'"));
            Assert.NotNull(randomChannel);
            Assert.Equal(1000u, randomChannel.NumberOfValues);

            var group2 = file.ChannelGroups.FirstOrDefault(g => g.Path == "/'Group 2'");
            Assert.NotNull(group2);
            Assert.Equal(2, group2.Channels.Count);

            var sineChannel = group2.Channels.FirstOrDefault(c => c.Path.EndsWith("'SineWave'"));
            Assert.NotNull(sineChannel);
            Assert.Equal(1000u, sineChannel.NumberOfValues);

            var timeChannel = group2.Channels.FirstOrDefault(c => c.Path.EndsWith("'Time'"));
            Assert.NotNull(timeChannel);
            Assert.Equal(1000u, timeChannel.NumberOfValues);
        }

        [Fact]
        public void ReadInterleavedFile()
        {
            var file = TdmsFile.Open("interleaved.tdms");

            Assert.Single(file.ChannelGroups);
            var group = file.ChannelGroups[0];
            Assert.Equal(2, group.Channels.Count);

            var channel1 = group.Channels.FirstOrDefault(c => c.Path == "/'Group 1'/'Channel 1'") as TdmsChannel<int>;
            Assert.NotNull(channel1);
            Assert.Equal(10u, channel1.NumberOfValues);
            Assert.Equal(Enumerable.Range(0, 10).ToArray(), channel1.GetData<int>());

            var channel2 = group.Channels.FirstOrDefault(c => c.Path == "/'Group 1'/'Channel 2'") as TdmsChannel<double>;
            Assert.NotNull(channel2);
            Assert.Equal(10u, channel2.NumberOfValues);
            Assert.Equal(Enumerable.Range(0, 10).Select(x => x * 2.0).ToArray(), channel2.GetData<double>());
        }
    }
}
