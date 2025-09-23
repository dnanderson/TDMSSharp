import numpy as np
from nptdms import TdmsWriter, ChannelObject, GroupObject

with TdmsWriter("interleaved.tdms") as writer:
    group = GroupObject("Group 1")
    # Write data interleaved
    for i in range(10):
        writer.write_segment([
            ChannelObject("Group 1", "Channel 1", [i]),
            ChannelObject("Group 1", "Channel 2", [i * 2.0])
        ])
