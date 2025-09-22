# -*- coding: utf-8 -*-
"""
This script demonstrates how to create a large, well-formed TDMS file
using the nptdms library. It writes data in chunks to be memory-efficient.
"""
import numpy as np
from nptdms import TdmsWriter, ChannelObject, GroupObject, RootObject
from datetime import datetime
import os

# --- Configuration ---
# Define the path for the output TDMS file
FILE_PATH = "large_example.tdms"
# Number of data chunks to write
NUM_CHUNKS = 100
# Number of data points in each chunk
CHUNK_SIZE = 1_000_000
# Total samples will be NUM_CHUNKS * CHUNK_SIZE (100 million in this case)
# ---

# Use a TdmsWriter to create the file
with TdmsWriter(FILE_PATH) as writer:
    print(f"Creating large TDMS file at: {FILE_PATH}")
    print(f"Total samples per channel: {NUM_CHUNKS * CHUNK_SIZE:,}")
    print("-" * 30)

    # 1. Define Root and Group Objects with Properties
    # These objects define the file's and groups' metadata. We write them first.
    root_properties = {
        "author": "Gemini",
        "description": f"A large example TDMS file ({NUM_CHUNKS * CHUNK_SIZE:,} samples/channel) created with nptdms.",
        "creation_time": np.datetime64(datetime.now()),
        "version": 2.0
    }
    root = RootObject(properties=root_properties)

    group1_name = "SensorReadings"
    group1_properties = {
        "group_description": "Contains simulated sensor readings written in chunks.",
        "sensor_id": 12345
    }
    group1 = GroupObject(group1_name, properties=group1_properties)

    group2_name = "Waveforms"
    group2_properties = {
        "group_description": "Contains a generated waveform written in chunks.",
        "waveform_type": "Sine"
    }
    group2 = GroupObject(group2_name, properties=group2_properties)

    # Write the metadata-only objects to the first segment of the file
    writer.write_segment([root, group1, group2])
    print("Wrote initial file metadata.")

    # 2. Loop to Write Data in Chunks
    # This avoids holding all the data in memory at once.
    for i in range(NUM_CHUNKS):
        print(f"Writing chunk {i + 1} of {NUM_CHUNKS}...")

        # -- Generate data for the current chunk --

        # Channel 1.1: A simple counter
        # The starting value is offset by the chunk index.
        start_count = i * CHUNK_SIZE
        counter_data = np.arange(start_count, start_count + CHUNK_SIZE, dtype=np.int32)
        channel1_1 = ChannelObject(
            group1_name, "Counter", counter_data,
            properties={
                "unit_string": "counts",
                "description": "A simple integer counter.",
            }
        )

        # Channel 1.2: Random floating-point data
        random_data = np.random.rand(CHUNK_SIZE).astype(np.float64) * 10
        channel1_2 = ChannelObject(
            group1_name, "Random", random_data,
            properties={"unit_string": "V", "description": "Random voltage readings."}
        )

        # Channel 2.1 & 2.2: A sine wave and its time vector
        # Calculate time vector for this specific chunk to ensure continuity
        time_start = i * 2 * np.pi
        time_end = (i + 1) * 2 * np.pi
        time_data = np.linspace(time_start, time_end, CHUNK_SIZE)
        sine_data = 5 * np.sin(time_data)

        channel2_1 = ChannelObject(
            group2_name, "SineWave", sine_data,
            properties={"unit_string": "Amplitude", "description": "A generated sine wave."}
        )
        
        channel2_2 = ChannelObject(
            group2_name, "Time", time_data,
            properties={"unit_string": "s", "description": "Time vector for the sine wave."}
        )

        # 4. Write the data for the current chunk as a new segment
        # nptdms automatically appends the data to the correct channels.
        writer.write_segment([channel1_1, channel1_2, channel2_1, channel2_2])

    print("-" * 30)
    print("Successfully wrote all data chunks to the TDMS file.")

    # Get final file size for verification
    try:
        file_size_bytes = os.path.getsize(FILE_PATH)
        file_size_mb = file_size_bytes / (1024 * 1024)
        file_size_gb = file_size_bytes / (1024 * 1024 * 1024)
        print(f"Final file size: {file_size_bytes:,} bytes (~{file_size_mb:.2f} MB / ~{file_size_gb:.2f} GB)")
    except OSError as e:
        print(f"Could not determine file size: {e}")
        
    print("\nTo verify the file, you can open it with NI TDMS compatible software")
    print("or use the nptdms library to read it back in another script.")
