# -*- coding: utf-8 -*-
"""
This script demonstrates how to create a well-formed TDMS file using the nptdms library.
It includes multiple groups, channels, various data types for properties, and sample data.
"""
import numpy as np
from nptdms import TdmsWriter, ChannelObject, GroupObject, RootObject
from datetime import datetime

# Define the path for the output TDMS file
FILE_PATH = "example.tdms"

# Use a TdmsWriter to create the file
with TdmsWriter(FILE_PATH) as writer:
    print(f"Creating TDMS file at: {FILE_PATH}")

    # 1. Define Root Object with Properties
    # The root object represents the file itself.
    root_properties = {
        "author": "Gemini",
        "description": "An example TDMS file created with nptdms.",
        # Use a numpy datetime64 object for timestamp properties
        "creation_time": np.datetime64(datetime.now()),
        "version": 1.0
    }
    root = RootObject(properties=root_properties)

    # 2. Define First Group and its Channels
    group1_name = "Group 1"
    group1_properties = {
        "group_description": "This group contains simulated sensor readings.",
        "sensor_id": 12345
    }
    group1 = GroupObject(group1_name, properties=group1_properties)

    # -- Channel 1.1: A simple counter
    # Generate some data for the first channel
    counter_data = np.arange(0, 100, dtype=np.int32)
    channel1_1 = ChannelObject(
        group1_name,
        "Counter",
        counter_data,
        properties={
            "unit_string": "counts",
            "description": "A simple integer counter.",
            "is_calibrated": False,
            "slope": 1.0,
        }
    )

    # -- Channel 1.2: Random floating-point data
    random_data = np.random.rand(100).astype(np.float64) * 10
    channel1_2 = ChannelObject(
        group1_name,
        "Random",
        random_data,
        properties={
            "unit_string": "V",
            "description": "Random voltage readings.",
            "noise_level_db": -80.5,
        }
    )

    # 3. Define Second Group and its Channels
    group2_name = "Group 2"
    group2_properties = {
        "group_description": "This group contains a generated waveform.",
        "waveform_type": "Sine"
    }
    group2 = GroupObject(group2_name, properties=group2_properties)

    # -- Channel 2.1: A sine wave
    time_data = np.linspace(0, 2 * np.pi, 100)
    sine_data = 5 * np.sin(time_data)
    channel2_1 = ChannelObject(
        group2_name,
        "SineWave",
        sine_data,
        properties={
            "unit_string": "Amplitude",
            "description": "A generated sine wave.",
            "frequency_hz": 1.0,
            "amplitude": 5.0,
            "offset": 0.0
        }
    )
    
    # -- Channel 2.2: The corresponding time data for the sine wave
    channel2_2 = ChannelObject(
        group2_name,
        "Time",
        time_data,
        properties={
            "unit_string": "s",
            "description": "Time vector for the sine wave.",
        }
    )

    # 4. Write all objects to a single segment in the TDMS file
    # The write_segment method can take any number of objects.
    writer.write_segment([
        root,
        group1,
        channel1_1,
        channel1_2,
        group2,
        channel2_1,
        channel2_2
    ])

    print("Successfully wrote data to TDMS file.")
    print("\nTo verify the file, you can open it with NI TDMS compatible software")
    print("or use the nptdms library to read it back in another script.")
