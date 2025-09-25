from nptdms import TdmsFile
import argparse
import os

def print_tdms_channels(file_path):
    """
    Parses a TDMS file and prints all its groups and channels in a structured format.

    Args:
        file_path (str): The path to the .tdms file.
    """
    if not os.path.exists(file_path):
        print(f"Error: File not found at '{file_path}'")
        return

    try:
        print(f"Reading file: {os.path.basename(file_path)}\n" + "="*40)
        
        # Open the TDMS file using a context manager
        with TdmsFile.open(file_path) as tdms_file:
            
            # Check if there are any groups
            if not tdms_file.groups():
                print("The TDMS file contains no groups.")
                return

            # Iterate over all groups in the file
            for group in tdms_file.groups():
                print(f"\n[GROUP] '{group.name}'")
                
                # Check for channels within the group
                if not group.channels():
                    print("  -> This group has no channels.")
                    continue
                
                # Iterate over all channels in the group
                for channel in group.channels():
                    # Get the data type of the channel's data
                    data_type = channel.dtype
                    # Get the number of data points
                    num_values = len(channel)
                    breakpoint()
                    
                    print(f"  [CHANNEL] '{channel.name}'")
                    print(f"    - Data Type: {data_type}")
                    print(f"    - Number of Values: {num_values}")
                    print(channel[:])

                    # You could optionally print channel properties like this:
                    if channel.properties:
                        print("    - Properties:")
                        for key, value in channel.properties.items():
                            print(f"      - {key}: {value}")
            
            print("\n" + "="*40)
            print("Finished parsing all groups and channels.")

    except (IOError, ValueError) as e:
        print(f"Error: Could not read or parse the TDMS file. It may be corrupted or not a valid TDMS file.")
        print(f"Details: {e}")
    except Exception as e:
        print(f"An unexpected error occurred: {e}")


if __name__ == "__main__":
    # Set up the command-line argument parser
    parser = argparse.ArgumentParser(
        description="A Python script to parse and print all channels from a TDMS file.",
        formatter_class=argparse.RawTextHelpFormatter
    )
    
    parser.add_argument(
        "tdms_file",
        type=str,
        help="Path to the .tdms file you want to inspect."
    )
    
    args = parser.parse_args()
    
    # Call the function with the provided file path
    print_tdms_channels(args.tdms_file)
