using TDMSSharp;

namespace TDMSReader
{
    class Program
    {
        static void Main(string[] args)
        {
            string filePath = "example.tdms";

            Console.WriteLine($"Attempting to read TDMS file at: {Path.GetFullPath(filePath)}");

            if (!File.Exists(filePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nError: The file '{filePath}' was not found.");
                Console.WriteLine("Please ensure 'example.tdms' from the root of the project is copied to the output directory.");
                Console.ResetColor();
                return;
            }

            try
            {
                // 1. Open the TDMS file using the TDMSSharp library
                var tdmsFile = TdmsFile.Open(filePath);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\nSuccessfully opened example.tdms");
                Console.ResetColor();
                Console.WriteLine("=================================\n");

                // 2. Display File-Level Properties
                Console.WriteLine("File Properties:");
                if (tdmsFile.Properties.Any())
                {
                    foreach (var prop in tdmsFile.Properties)
                    {
                        Console.WriteLine($"  - {prop.Name}: {prop.Value}");
                    }
                }
                else
                {
                    Console.WriteLine("  (No file properties found)");
                }
                Console.WriteLine();

                // 3. Iterate over each Channel Group and its Channels
                foreach (var group in tdmsFile.ChannelGroups)
                {
                    Console.WriteLine("---------------------------------");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Channel Group: {group.Path}");
                    Console.ResetColor();

                    // Display Group Properties
                    if (group.Properties.Any())
                    {
                        Console.WriteLine("  Group Properties:");
                        foreach (var prop in group.Properties)
                        {
                            Console.WriteLine($"    - {prop.Name}: {prop.Value}");
                        }
                    }

                    // Display Channels within the group
                    foreach (var channel in group.Channels)
                    {
                        Console.WriteLine($"\n  Channel: {channel.Path}");
                        Console.WriteLine($"    - Data Type: {channel.DataType}");
                        Console.WriteLine($"    - Number of Values: {channel.NumberOfValues}");

                        // Display Channel Properties
                        if (channel.Properties.Any())
                        {
                            Console.WriteLine("    - Channel Properties:");
                            foreach (var prop in channel.Properties)
                            {
                                Console.WriteLine($"        - {prop.Name}: {prop.Value}");
                            }
                        }

                        // 4. Display a sample of the raw data from the channel
                        // Use GetDataAsArray() method to access the data
                        var dataArray = channel.GetDataAsArray();
                        if (dataArray != null && dataArray.Length > 0)
                        {
                            Console.WriteLine("    - Sample Data:");
                            int count = Math.Min(5, dataArray.Length);
                            for (int i = 0; i < count; i++)
                            {
                                Console.WriteLine($"        [{i}]: {dataArray.GetValue(i)}");
                            }
                            if (dataArray.Length > count)
                            {
                                Console.WriteLine($"        ... ({dataArray.Length - count} more values)");
                            }
                        }
                    }
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"An error occurred while reading the TDMS file: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine(ex.StackTrace);
            }

        }
    }
}