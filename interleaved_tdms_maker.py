from nptdms import TdmsWriter, ChannelObject
import numpy as np

with TdmsWriter("interleaved.tdms") as tdms_writer:
    group_name = "Group 1"
    channel1_name = "Channel 1"
    channel2_name = "Channel 2"

    data1 = np.arange(10, dtype=np.int32)
    data2 = np.arange(10, dtype=np.float64) * 2.0

    channel1 = ChannelObject(group_name, channel1_name, data1)
    channel2 = ChannelObject(group_name, channel2_name, data2)

    tdms_writer.write_segment([channel1, channel2])
