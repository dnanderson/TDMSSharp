# TDMS Specification

This document contains two parts:

1. Implementation-oriented TDMS specification used by this repository.
2. Migrated legacy TDMS format reference that was previously the project README.

---

## Part 1: Implementation Specification

# TDMS File Format Specification
## Based on npTDMS Reader Implementation

---

## 1. Overview

TDMS (Technical Data Management Streaming) is a binary file format created by National Instruments for storing measurement data. Files consist of one or more **segments**, each containing optional metadata and raw data sections.

### 1.1 File Structure

```
TDMS File = Segment₁ + Segment₂ + ... + Segmentₙ
```

Each segment contains:
1. **Lead In** (28 bytes) - Segment header with metadata
2. **Meta Data** (variable) - Object descriptions and properties (optional)
3. **Raw Data** (variable) - Actual measurement data (optional)

### 1.2 Companion Index Files

TDMS files may have an associated **index file** (`.tdms_index`) that stores only metadata for faster access. Index files:
- Start with `TDSh` instead of `TDSm`
- Contain only segment lead-ins and metadata sections
- Share the same segment structure as data files
- Must match the corresponding TDMS file exactly

---

## 2. Segment Structure

### 2.1 Segment Lead In (28 bytes)

| Offset | Size | Type | Description |
|--------|------|------|-------------|
| 0 | 4 | Char[4] | Tag: `TDSm` (data file) or `TDSh` (index file) |
| 4 | 4 | Int32 | Table of Contents (ToC) mask |
| 8 | 4 | Int32 | TDMS version number (typically 4712 or 4713) |
| 12 | 8 | Int64 | Next segment offset (bytes from this segment start to next) |
| 20 | 8 | Int64 | Raw data offset (bytes from this segment start to raw data) |

**Important Notes:**
- All integer values use endianness specified by ToC mask
- If `next_segment_offset == 0xFFFFFFFFFFFFFFFF`, segment is incomplete (e.g., LabVIEW crash)
- Lead-in size (28 bytes) is included when calculating absolute positions

### 2.2 Table of Contents (ToC) Flags

The ToC mask is a bitfield indicating segment properties:

| Bit | Mask | Flag Name | Description |
|-----|------|-----------|-------------|
| 1 | `1 << 1` | `kTocMetaData` | Segment contains metadata |
| 2 | `1 << 2` | `kTocNewObjList` | Segment contains new object list |
| 3 | `1 << 3` | `kTocRawData` | Segment contains raw data |
| 5 | `1 << 5` | `kTocInterleavedData` | Raw data is interleaved |
| 6 | `1 << 6` | `kTocBigEndian` | Data is big-endian (default: little-endian) |
| 7 | `1 << 7` | `kTocDAQmxRawData` | Segment contains DAQmx format data |

---

## 3. Metadata Section

### 3.1 Metadata Structure

When `kTocMetaData` flag is set:

```
MetaData = NumObjects(4 bytes) + Object₁ + Object₂ + ... + Objectₙ
```

### 3.2 Object Metadata

Each object in metadata:

```
Object = PathLength(4) + Path(variable) + RawDataIndex(4) + [DataTypeInfo] + NumProperties(4) + Properties
```

#### 3.2.1 Object Path Format

Object paths use the format: `/'Group'/'Channel'` or `/'Group'` or `/`

**Path Encoding Rules:**
1. Root object: `'/'` (length = 1)
2. Group object: `/'GroupName'` 
3. Channel object: `/'GroupName'/'ChannelName'`
4. Single quotes in names are escaped: `''` (e.g., `/'Group''s Name'/'Channel'`)
5. Path is UTF-8 encoded string, prefixed with 4-byte length

**Examples:**
- Root: `/` (1 byte)
- Group: `/'Temperature Data'` (19 bytes)  
- Channel: `/'Group'/'Channel''s Name'` (27 bytes)

#### 3.2.2 Raw Data Index Header

4-byte value indicating data structure:

| Value | Meaning | Following Data |
|-------|---------|----------------|
| `0xFFFFFFFF` | No raw data for this object | None |
| `0x00000000` | Reuse previous object structure | None |
| `0x00001269` | DAQmx Format Changing Scaler | DAQmx metadata |
| `0x0000126A` | DAQmx Digital Line Scaler | DAQmx metadata |
| `0x00000014` | Standard TDMS format | Type, dimension, count, [size] |

#### 3.2.3 Standard Data Type Information

For standard raw data index (0x00000014 or other positive values):

```
DataTypeInfo = DataType(4) + Dimension(4) + NumberOfValues(8) + [TotalSize(8)]
```

**Fields:**
- `DataType` (4 bytes): Data type enumeration (see section 4)
- `Dimension` (4 bytes): Always 1 for TDMS 2.0
- `NumberOfValues` (8 bytes): Number of values in raw data for this object
- `TotalSize` (8 bytes): Only for variable-length types (String type 0x20)

### 3.3 Property Data

```
Properties = Property₁ + Property₂ + ... + Propertyₙ

Property = NameLength(4) + Name(UTF-8) + DataType(4) + Value
```

Properties store metadata like:
- `wf_start_time` - Waveform start timestamp
- `wf_increment` - Time between samples
- `NI_Number_Of_Scales` - Number of scaling operations
- Custom user properties

---

## 4. Data Types

### 4.1 Basic Type Enumeration

| Code | Name | Size (bytes) | NumPy Type | Notes |
|------|------|--------------|------------|-------|
| 0x00 | Void | 0 | - | Empty/placeholder |
| 0x01 | Int8 | 1 | int8 | Signed byte |
| 0x02 | Int16 | 2 | int16 | Signed short |
| 0x03 | Int32 | 4 | int32 | Signed integer |
| 0x04 | Int64 | 8 | int64 | Signed long |
| 0x05 | Uint8 | 1 | uint8 | Unsigned byte |
| 0x06 | Uint16 | 2 | uint16 | Unsigned short |
| 0x07 | Uint32 | 4 | uint32 | Unsigned integer |
| 0x08 | Uint64 | 8 | uint64 | Unsigned long |
| 0x09 | SingleFloat | 4 | float32 | Single precision float |
| 0x0A | DoubleFloat | 8 | float64 | Double precision float |
| 0x19 | SingleFloatWithUnit | 4 | float32 | Float with unit string property |
| 0x1A | DoubleFloatWithUnit | 8 | float64 | Double with unit string property |
| 0x20 | String | Variable | object | UTF-8 string |
| 0x21 | Boolean | 1 | bool | 0=False, 1=True |
| 0x44 | TimeStamp | 16 | - | TDMS timestamp format |
| 0x08000c | ComplexSingleFloat | 8 | complex64 | Real + Imaginary floats |
| 0x10000d | ComplexDoubleFloat | 16 | complex128 | Real + Imaginary doubles |
| 0xFFFFFFFF | DAQmxRawData | Variable | - | DAQmx scaler data |

### 4.2 Timestamp Format (Type 0x44)

16-byte structure:
```
Timestamp = SecondFractions(8) + Seconds(8)
```

- `SecondFractions` (Uint64): Positive fractions of a second (units of 2⁻⁶⁴)
- `Seconds` (Int64): Signed seconds since epoch (1904-01-01 00:00:00 UTC)

**Important:** For big-endian data, field order is reversed (Seconds then SecondFractions)

### 4.3 String Type Format (Type 0x20)

**Critical: Strings have two completely different storage formats depending on context**

#### 4.3.1 Single String Values (Properties, Metadata)

```
String = Length(4) + UTF8Data(Length bytes)
```

#### 4.3.2 String Arrays (Channel Raw Data)

**Storage Format:**
```
StringArray = Offset₁(4) + Offset₂(4) + ... + Offsetₙ(4) + StringData
```

**Where:**
- Each offset is a Uint32 indicating the byte position AFTER the corresponding string
- Offsets are cumulative (each offset = sum of all previous string lengths)
- StringData contains all strings concatenated (no delimiters)

**Example:** Array `["Hello", "World", "!"]`
```
Offsets:   [5, 10, 11]    (4 bytes each = 12 bytes total)
Data:      "HelloWorld!"  (11 bytes)
Total:     23 bytes
```

**Critical Implementation Details:**

1. **Total Size in Metadata:**
   ```
   TotalSize = (NumStrings × 4) + TotalCharacterBytes
   ```

2. **Reading Algorithm:**
   ```python
   offsets = [0]  # Implicit first offset
   for i in range(num_strings):
       offsets.append(read_uint32())
   
   strings = []
   for i in range(num_strings):
       start = offsets[i]
       end = offsets[i + 1]
       string_bytes = read_bytes(end - start)
       strings.append(string_bytes.decode('utf-8'))
   ```

3. **UTF-8 Encoding:**
   - All strings are UTF-8 encoded
   - Invalid UTF-8 sequences should be replaced with � (U+FFFD)
   - Handle incomplete multi-byte sequences gracefully

---

## 5. Raw Data Section

### 5.1 Data Organization

When `kTocRawData` flag is set, segment contains raw data organized as:

**Contiguous Data (default):**
```
RawData = Object₁Data + Object₂Data + ... + ObjectₙData
```

**Interleaved Data (`kTocInterleavedData` set):**
```
RawData = [Value₁Obj₁ + Value₁Obj₂ + ... + Value₁Objₙ] + 
          [Value₂Obj₁ + Value₂Obj₂ + ... + Value₂Objₙ] + ...
```

### 5.2 Chunking

Data is organized in **chunks**, where each chunk contains one value per channel (or `NumberOfValues` from metadata).

**Chunk Size Calculation:**
- Contiguous: `sum(object.number_values × object.data_size for each object)`
- Interleaved: `sum(object.data_size for each object) × first_object.number_values`

**Number of Chunks:**
```
num_chunks = total_data_size / chunk_size
```

**Partial Final Chunk:**
- If data doesn't evenly divide, final chunk may be truncated
- Calculate actual values read based on remaining bytes
- For contiguous: assign bytes to objects in order
- For interleaved: all channels truncated equally

### 5.3 String Channel Raw Data - DETAILED

String channels require special handling in raw data:

#### 5.3.1 Metadata Declaration

```
PathLength: 18 00 00 00                    # Length of "/'Group'/'Channel'"
Path: 2F 27 47 72 6F 75 70 27 2F ...       # "/'Group'/'Channel'"
RawDataIndex: 14 00 00 00                  # Standard format
DataType: 20 00 00 00                      # String type (0x20)
Dimension: 01 00 00 00                     # Always 1
NumberOfValues: 03 00 00 00 00 00 00 00    # 3 strings
TotalSize: 17 00 00 00 00 00 00 00         # 23 bytes total (12 offsets + 11 chars)
NumProperties: 00 00 00 00                 # 0 properties
```

#### 5.3.2 Raw Data Layout

For 3 strings `["Hello", "World", "!"]`:

```
Offset 0-3:   05 00 00 00    # After "Hello" (5 bytes)
Offset 4-7:   0A 00 00 00    # After "World" (5+5=10 bytes) 
Offset 8-11:  0B 00 00 00    # After "!" (10+1=11 bytes)
Offset 12-16: 48 65 6C 6C 6F # "Hello"
Offset 17-21: 57 6F 72 6C 64 # "World"
Offset 22:    21             # "!"
```

#### 5.3.3 Empty Strings

Empty strings are represented by consecutive equal offsets:

```
Strings: ["", "Hello", "", "World"]
Offsets: [0, 0, 5, 5, 10]
Data: "HelloWorld"
```

#### 5.3.4 Interleaved String Channels - CRITICAL LIMITATION

**Rule: String channels CANNOT be properly interleaved with other channels**

- If `kTocInterleavedData` is set AND segment has multiple channels
- At least one channel is String type
- **Reader MUST reject this as invalid**

**Exception:** Single string channel with interleaved flag set
- Some files incorrectly set this flag
- Should be read as contiguous data (ignore interleaved flag)

### 5.4 Complex Number Storage

Complex numbers are stored as consecutive real and imaginary parts:

**ComplexSingleFloat (8 bytes):**
```
Complex64 = Real(float32) + Imaginary(float32)
```

**ComplexDoubleFloat (16 bytes):**
```
Complex128 = Real(float64) + Imaginary(float64)
```

---

## 6. Incremental Metadata and Optimizations

### 6.1 Metadata Reuse Strategies

TDMS supports multiple optimization strategies to reduce file size:

#### 6.1.1 Complete Metadata Omission

When structure hasn't changed between segments:

```
ToC Flags: kTocRawData (NO kTocMetaData)
```

**Behavior:**
- Reuse previous segment's complete object list
- Reuse all data type information
- Read data with same structure as previous segment

**Use Case:** High-speed continuous acquisition with unchanging channel configuration

#### 6.1.2 Partial Metadata Updates

When only some objects change:

```
ToC Flags: kTocMetaData + kTocRawData (NO kTocNewObjList)
```

**Behavior:**
- Start with previous segment's object list
- Objects in metadata section UPDATE or ADD to existing list
- Objects not mentioned remain unchanged from previous segment

**Example Scenario:**
```
Segment 1 (kTocNewObjList):
  - Channel1: Int32, 100 values
  - Channel2: Int32, 100 values

Segment 2 (NO kTocNewObjList):
  - Channel2: Int32, 200 values  # Only Channel2 metadata present
  
Result: Channel1 keeps 100 values, Channel2 now has 200 values
```

#### 6.1.3 New Object List

When object structure fundamentally changes:

```
ToC Flags: kTocMetaData + kTocRawData + kTocNewObjList
```

**Behavior:**
- Completely replace previous object list
- All active objects must be redeclared
- Objects from previous segments are forgotten

**Use Case:** Adding/removing channels, changing acquisition configuration

### 6.2 Raw Data Index Optimization

Within a metadata section, individual objects can optimize further:

#### 6.2.1 No Data for Object

```
RawDataIndex: FF FF FF FF (0xFFFFFFFF)
```

**Behavior:**
- Object exists but has no data in this segment
- Metadata/properties may be updated
- No raw data allocated for this object

**Example:** Temperature channel that only updates once per second in a 1 kHz acquisition

#### 6.2.2 Reuse Previous Structure

```
RawDataIndex: 00 00 00 00 (0x00000000)
```

**Behavior:**
- Reuse this object's data type and size from previous segment
- Object WILL have data in this segment
- No DataTypeInfo section follows

**Critical Rule:** Can only be used if object appeared in a previous segment with data

**Use Case:**
```
Segment 1:
  Channel1: [RawDataIndex: 0x14, Type: Int32, Count: 100]
  
Segment 2 (metadata present, updates properties):
  Channel1: [RawDataIndex: 0x00000000]  # Reuse Int32, 100 values
  Properties: [NewProperty: Value]       # Update properties
```

### 6.3 Complete Optimization Scenarios

#### Scenario A: High-Speed Continuous Acquisition

```
Segment 1: kTocMetaData + kTocRawData + kTocNewObjList
  - Define all channels with full metadata
  - Include 1000 values per channel

Segment 2: kTocRawData ONLY
  - No metadata section at all
  - 1000 more values per channel with same structure

Segment 3: kTocRawData ONLY
  - Continues pattern
```

**File Size Savings:** Massive - metadata written once, not repeated

#### Scenario B: Changing Channel Properties

```
Segment 1: kTocMetaData + kTocRawData + kTocNewObjList
  Channel1: Int32, 100 values, Property[Gain=1.0]

Segment 2: kTocMetaData + kTocRawData (NO kTocNewObjList)
  Channel1: 0x00000000 (reuse structure), Property[Gain=2.0]
  
Result: Same data structure, updated Gain property
```

#### Scenario C: Adding a Channel Mid-Acquisition

```
Segment 1: kTocMetaData + kTocRawData + kTocNewObjList
  - Channel1: Int32, 100 values
  - Channel2: Float64, 100 values

Segment 2: kTocMetaData + kTocRawData (NO kTocNewObjList)
  - Channel1: 0x00000000 (reuse)
  - Channel2: 0x00000000 (reuse)  
  - Channel3: Int32, 100 values (NEW)
  
Result: All three channels have data in segment 2
```

#### Scenario D: Alternating Active Channels

```
Segment 1: kTocMetaData + kTocRawData + kTocNewObjList
  - Channel1: Int32, 100 values
  - Channel2: No data (0xFFFFFFFF)

Segment 2: kTocMetaData + kTocRawData + kTocNewObjList
  - Channel1: No data (0xFFFFFFFF)
  - Channel2: Int32, 100 values
```

### 6.4 Metadata Update Rules - CRITICAL

**Rule 1:** First segment SHOULD have `kTocNewObjList` (though readers must handle if missing)

**Rule 2:** Data type changes require full redefinition:
```
# INVALID:
Segment 1: Channel1 = Int32
Segment 2: Channel1 = Float64  # MUST use kTocNewObjList

# VALID:
Segment 1: Channel1 = Int32
Segment 2 (kTocNewObjList): Channel1 = Float64
```

**Rule 3:** `NumberOfValues` can change with 0x00000000:
```
Segment 1: Channel1 = Int32, 100 values
Segment 2: Channel1 = 0x00000000  # Inherits Int32, keeps 100 values
Segment 3: Channel1 = Full metadata, 200 values  # Change count
```

**Rule 4:** Object order in metadata doesn't affect raw data order (uses previous segment's order)

### 6.5 Property Accumulation

Properties accumulate across segments unless `kTocNewObjList` is set:

```
Segment 1 (kTocNewObjList):
  Channel1: Property[A=1, B=2]

Segment 2 (no kTocNewObjList):
  Channel1: Property[B=3, C=4]
  
Final Properties: {A=1, B=3, C=4}  # B updated, C added, A kept
```

---

## 7. DAQmx Raw Data Format

### 7.1 DAQmx Metadata

When RawDataIndex is `0x00001269` (Format Changing) or `0x0000126A` (Digital Line):

```
DAQmxMetadata = DataType(4) + Dimension(4) + ChunkSize(8) + 
                ScalerCount(4) + Scalers + RawDataWidthCount(4) + RawDataWidths
```

**Fields:**
- `DataType`: Type code (often 0xFFFFFFFF for raw DAQmx)
- `Dimension`: Always 1
- `ChunkSize`: Number of values per chunk
- `ScalerCount`: Number of scaler descriptors
- `RawDataWidthCount`: Number of raw data buffers

### 7.2 Scaler Descriptors

**Format Changing Scaler (0x00001269):**
```
Scaler = DataType(4) + RawBufferIndex(4) + RawByteOffset(4) + 
         SampleFormatBitmap(4) + ScaleID(4)
```

**Digital Line Scaler (0x0000126A):**
```
DigitalScaler = DataType(4) + RawBufferIndex(4) + RawBitOffset(4) + 
                SampleFormatBitmap(1) + ScaleID(4)
```

### 7.3 DAQmx Data Organization

Data is organized in **raw buffers** (one per acquisition card):

```
Chunk = RawBuffer₁ + RawBuffer₂ + ... + RawBufferₙ
```

Each scaler extracts values from its designated buffer at specified offset.

---

## 8. Incomplete Segments

### 8.1 Detection

A segment is incomplete when:
1. `NextSegmentOffset == 0xFFFFFFFFFFFFFFFF`, OR
2. Calculated next segment position exceeds file size

### 8.2 Handling Incomplete Segments

**Case 1: Incomplete Metadata**
- If data position > file size: Skip segment, no data available
- Otherwise: Attempt to read available data

**Case 2: Incomplete Data**
- Calculate available bytes: `file_size - data_position`
- Determine how many complete values can be read per channel
- Interleaved: Truncate all channels equally at last complete row
- Contiguous: Distribute bytes to channels in order until exhausted

**Example: Contiguous Data Truncation**
```
Expected: Channel1[100 Int32] + Channel2[100 Int32] = 800 bytes
Available: 600 bytes

Result: 
  Channel1: 100 values (400 bytes) - complete
  Channel2: 50 values (200 bytes) - truncated
```

---

## 9. Endianness

### 9.1 Endian Detection

Check `kTocBigEndian` flag in ToC:
- If set: All multi-byte values are big-endian
- If clear: All multi-byte values are little-endian (default)

### 9.2 Affected Data

Endianness applies to:
- All integers in metadata and data
- Floating point values
- Timestamp values (field order also reverses for big-endian)
- String length prefixes
- Does NOT affect: String content (UTF-8 is byte-order independent)

---

## 10. Reading Algorithm Summary

### 10.1 File Reading Procedure

```
1. Open TDMS file
2. Read first segment lead-in
3. Initialize previous_objects = {}

For each segment:
  4. Read lead-in (28 bytes)
  5. Parse ToC flags
  6. If kTocMetaData:
       Read metadata section
       Update object definitions
     Else:
       Reuse previous_objects
  7. If kTocRawData:
       Calculate chunk size and count
       Read raw data chunks
  8. Seek to next segment position
  9. If at EOF or error: break
```

### 10.2 Object List Management

```
if kTocNewObjList:
    current_objects = objects_from_metadata
else if kTocMetaData:
    current_objects = previous_objects.copy()
    current_objects.update(objects_from_metadata)
else:
    current_objects = previous_objects

previous_objects = current_objects
```

### 10.3 String Channel Reading

```
For string channel in raw data:
  1. Verify NumberOfValues from metadata
  2. Read NumberOfValues × 4 bytes for offsets
  3. Calculate string lengths from offset differences
  4. Read each string using calculated lengths
  5. Decode UTF-8, handle errors gracefully
```

---

## 11. Implementation Guidelines

### 11.1 Critical Validation Checks

1. **File Signature:** First 4 bytes must be `TDSm` or `TDSh`
2. **Version:** Warn if not 4712 or 4713
3. **Object Paths:** Must be valid UTF-8, properly quoted
4. **Data Type Consistency:** Type cannot change without `kTocNewObjList`
5. **String Arrays:** Never allow interleaved with multiple channels
6. **Raw Data Index:** Validate 0x00000000 only used when object previously defined

### 11.2 Error Recovery

1. **Invalid UTF-8:** Replace with � and log warning
2. **Incomplete Segments:** Read what's available, mark incomplete
3. **Index File Mismatch:** Verify segment positions match data file
4. **Truncated Data:** Calculate actual values read, don't fail

### 11.3 Performance Optimizations

1. **Memory Mapping:** Use for large data files when possible
2. **Lazy Loading:** Read metadata first, data on-demand
3. **Index Caching:** Build segment indexes for random access
4. **Chunk Streaming:** Read data in chunks for large files

---

## 12. Common File Patterns

### 12.1 Simple Continuous Acquisition

```
Segment 1: [kTocMetaData + kTocRawData + kTocNewObjList]
  Full metadata for all channels
  1000 samples
  
Segment 2-N: [kTocRawData only]
  1000 samples each
  Reuse metadata from Segment 1
```

### 12.2 Property Updates During Acquisition

```
Segment 1: [kTocMetaData + kTocRawData + kTocNewObjList]
  Initial setup
  
Segment 2: [kTocMetaData + kTocRawData]  # No kTocNewObjList
  Same channels, updated properties
  
Segment 3: [kTocRawData only]
  Continue with Segment 2 configuration
```

### 12.3 Dynamic Channel Addition

```
Segment 1: [kTocMetaData + kTocRawData + kTocNewObjList]
  Channel A, B
  
Segment 2: [kTocMetaData + kTocRawData]  # No kTocNewObjList  
  Channel C (new)
  
Result: All three channels active in Segment 2
```

---

This specification is derived from the npTDMS reader implementation and represents the format as understood by a robust, production-tested reader. Implementations should handle all edge cases mentioned, particularly around string channels and incremental metadata updates.



---

## Part 2: Legacy NI Format Reference (Migrated from README.md)

TDMS File Format Internal Structure

Updated Nov 12, 2024

# Overview

This article provides a detailed description of the internal structure of the TDM Streaming (TDMS) file format.

# Contents

- Logical Structure
- Binary Layout
- Predefined Properties
- Optimization
- Conclusion
- Additional Resources

# Logical Structure

TDMS files organize data in a three-level hierarchy of objects. The top level is comprised of a single object that holds file-specific information like author or title. Each file can contain an unlimited number of groups, and each group can contain an unlimited number of channels. In the following illustration, the file example events.tdm contains two groups, each of which contains two channels.

Every TDMS object is uniquely identified by a path. Each path is a string including the name of the object and the name of its owner in the TDMS hierarchy, separated by a forward slash. Each name is enclosed by the quotation marks. Any single quotation mark within an object name is replaced with double quotation marks. The following table illustrates path formatting examples for each type of TDMS object:

| **Object Name** | **Object** | **Path** |
| --- | --- | --- |
| \-- | File | /   |
| Measured Data | Group | /'Measured Data' |
| Amplitude Sweep | Channel | /'Measured Data'/'Amplitude Sweep' |
| Dr. T's Events | Group | /'Dr. T''s Events' |
| Time | Channel | /'Dr. T''s Events'/'Time' |

In order for all TDMS client applications to work properly, every TDMS file must contain a file object. A file object must contain a group object for each group name used in a channel path. In addition, a file object can contain an arbitrary number of group objects with no channels.

Every TDMS object can have an unlimited number of properties. Each TDMS property consists of a combination of a name (always a string), a type identifier, and a value. Typical data types for properties include numeric types such as integers or floating-point numbers, time stamps or strings. TDMS properties do not support arrays. If a TDMS file is located within a search area of the NI DataFinder, all properties automatically are available for searching.

Only channel objects in TDMS files can contain raw data arrays. In current TDMS versions, only one-dimensional arrays are supported.

# Binary Layout

Every TDMS file contains two types of data: meta data and raw data. Meta data is descriptive data stored in objects or properties. Data arrays attached to channel objects are referred to as raw data. TDMS files contain raw data for multiple channels in one contiguous block. In order to be able to extract raw data from that block, TDMS files use a raw data index, which includes information about the data block composition, including the channel that corresponds to the data, the amount of values the block contains for that channel, and the order in which the data was stored.

## TDMS Segment Layout

Data is written to TDMS files in segments. Every time data is appended to a TDMS file, a new segment is created. Refer to the Meta Data and Raw Data sections of this article for exceptions to this rule. A segment consists of the following three parts:

- Lead In—Contains basic information, such as a tag that identifies files as TDMS, a version number, and the length information of the meta data and the raw data.
- Meta Data—Contains names and properties of all objects in the segment. For objects that include raw data (channels), the meta data part also contains index information that is used to locate the raw data for this object in the segment.
- Raw Data—A contiguous block of all raw data associated with any of the objects included in the segment. The raw data part can contain interleaved data values or a series of contiguous data chunks. The raw data part can furthermore contain raw data from DAQmx.

All strings in TDMS files, such as object paths, property names, property values, and raw data values, are encoded in UTF-8 Unicode. All of them, except for raw data values, are preceded by a 32-bit unsigned integer that contains the length of the string in bytes, not including the length value itself. Strings in TDMS files can be null-terminated, but since the length information is stored, the null terminator will be ignored when you read from the file.

Timestamps in TDMS files are stored as a structure of two components:

- (i64) seconds: since the epoch 01/01/1904 00:00:00.00 UTC (using the Gregorian calendar and ignoring leap seconds)
- (u64) positive fractions: (2^-64) of a second

Boolean values are stored as 1 byte each, where 1 represents TRUE and 0 represents FALSE.

### Lead In

The lead in contains information used to validate a segment. The lead in also contains information used for random access to a TDMS file. The following example shows the binary footprint of the lead in part of a TDMS file:

| **Binary layout (hexadecimal)** | **Description** |
| --- | --- |
| 54 44 53 6D | "TDSm" tag |
| 0E 00 00 00 | ToC mask 0x1110 (segment contains object list, meta data, raw data) |
| 69 12 00 00 | Version number (4713) |
| E6 00 00 00 00 00 00 00 | Next segment offset (value: 230) |
| DE 00 00 00 00 00 00 00 | Raw data offset (value: 222) |
| --- | --- |

The lead in part in the previous table contains the following information:

- The lead in starts with a 4-byte tag that identifies a TDMS segment ("TDSm").
- The next four bytes are used as a bit mask in order to indicate what kind of data the segment contains. This bit mask is referred to as ToC (Table of Contents). Any combination of the following flags can be encoded in the ToC:

| **Flag** | **Description** |
| --- | --- |
| #define kTocMetaData (1L<<1) | Segment contains meta data |
| #define kTocRawData (1L<<3) | Segment contains raw data |
| #define kTocDAQmxRawData (1L<<7) | Segment contains DAQmx raw data |
| #define kTocInterleavedData (1L<<5) | Raw data in the segment is interleaved (if flag is not set, data is contiguous) |
| #define kTocBigEndian (1L<<6) | All numeric values in the segment, including the lead in, raw data, and meta data, are big-endian formatted (if flag is not set, data is little-endian). ToC is not affected by endianess; it is always little-endian. |
| #define kTocNewObjList (1L<<2) | Segment contains new object list (e.g. channels in this segment are not the same channels the previous segment contains) |
| --- | --- |

- The next four bytes contain a version number (32-bit unsigned integer), which specifies the oldest TDMS revision a segment complies with. At the time of this writing, the version number is 4713. The only previous version of TDMS has number 4712.  
    Note: The version number 4713 corresponds to the TDMS file format version 2.0 in LabVIEW. The version number 4712 corresponds to the TDMS file format version 1.0 in LabVIEW.
- The next eight bytes (64-bit unsigned integer) describe the length of the remaining segment (overall length of the segment minus length of the lead in). If further segments are appended to the file, this number can be used to locate the starting point of the following segment. If an application encountered a severe problem while writing to a TDMS file (crash, power outage), all bytes of this integer can be 0xFF. This can only happen to the last segment in a file.
- The last eight bytes (64-bit unsigned integer) describe the overall length of the meta information in the segment. This information is used for random access to the raw data. If the segment contains no meta data at all (properties, index information, object list), this value will be 0.

### Meta Data

TDMS meta data consists of a three-level hierarchy of data objects including a file, groups, and channels. Each of these object types can include any number of properties. The meta data section has the following binary layout on disk:

- Number of new objects in this segment (unsigned 32-bit integer).
- Binary representation of each of these objects.

The binary layout of a single TDMS object on disk consists of components in the following order. Depending on the information stored in a particular segment, the object might contain only a subset of these components.

- Object path (string)
- Raw data index
  - If this object does not have any raw data assigned to it in this segment, an unsigned 32-bit integer (0xFFFFFFFF) will be stored instead of the index information.
  - If this object contains DAQmx raw data in this segment, then the first four bytes of the raw data index is "69 12 00 00" (which means the raw data contains DAQmx Format Changing scaler) or "69 13 00 00" (which means the raw data contains DAQmx Digital Line scaler). Following these first four bytes is information about the DAQmx raw data index. Refer to the bullet item below for mmore information about the DAQmx raw data index.
  - If the raw data index of this object in this segment exactly matches the index the same object had in the previous segment, an unsigned 32-bit integer (0x0000000) will be stored instead of the index information.
  - If the object contains raw data that does not match the index information assigned to this object in the previous segment, a new index for that raw data will be stored:
    - Length of the raw data index (unsigned 32-bit integer)
    - Data type (tdsDataType enum, stored as 32-bit integer)
    - Array dimension (unsigned 32-bit integer) (In TDMS file format version 2.0, 1 is the only valid value)
    - Number of values (unsigned 64-bit integer)
    - Total size in bytes (unsigned 64-bit integer) (only stored for variable length data types, e.g. strings)
- If the raw data index is the DAQmx raw data index, the index contains the following information:
  - Data type (unsigned 32-bit integer), where "FF FF FF FF" indicates the raw data is DAQmx raw data)
  - Array dimension (unsigned 32-bit integer) (In TDMS file format version 2.0, 1 is the only valid value)
  - Number of values (unsigned 64-bit integer), also known as "chunk size"
  - The vector of Format Changing scalers
    - Vector size (unsigned 32-bit integer)  
            The following applies to the first Format Changing scaler's information.
    - DAQmx data type (unsigned 32-bit integer)
    - Raw buffer index (unsigned 32-bit integer)
    - Raw byte offset within the stride (unsigned 32-bit integer)
    - Sample format bitmap (unsigned 32-bit integer)
    - Scale ID (unsigned 32-bit integer)  
            (If the vector size is larger than 1, the object contains multiple Format Changing scalers and the information in the previous bullet items can be repeated.)
  - The vector of raw data width
    - Vector size (unsigned 32-bit integer)
    - Elements in the vector (each is unsigned 32-bit integer)
- Number of properties (unsigned 32-bit integer)
- Properties. For each property, the following information is stored:
  - Name (string)
  - Data type (tdsDataType)
  - Value (numerics stored binary, strings stored as explained above).

The following table shows an example of meta information for a group and a channel. The group contains two properties, one string and one integer. The channel contains a raw data index and no properties.

| **Binary footprint (hexadecimal)** | **Description** |
| --- | --- |
| 02 00 00 00	| Number of objects |
| 08 00 00 00	| Length of the first object path |
| 2F 27 47 72 6F 75 70 27	| Object path (/'Group') |
| FF FF FF FF | Raw data index ("FF FF FF FF" means there is no raw data assigned to the object) |
| 02 00 00 00 |	Number of properties for /'Group' |
| 04 00 00 00	| Length of the first property name |
| 70 72 6F 70	| Property name (prop) |
| 20 00 00 00	| Data type of the property value (tdsTypeString) |
| 05 00 00 00	| Length of the property value (only for strings) |
| 76 61 6C 75 65 |	Value of the property prop (value) |
| 03 00 00 00 |	Length of the second property name |
| 6E 75 6D	| Property name (num) |
| 03 00 00 00 |	Data type of the property value (tdsTypeI32) |
| 0A 00 00 00	| Value of the property num (10) |
| 13 00 00 00	| Length of the second object path |
| 2F 27 47 72 6F 75 70 27 2F 27 43 68 61 6E 6E 65 6C 31 27 | Path of the second object (/'Group'/'Channel1') |
| 14 00 00 00	| Length of index information |
| 03 00 00 00	| Data type of the raw data assigned to this object |
| 01 00 00 00	| Dimension of the raw data array (must be 1) |
| 02 00 00 00 00 00 00 00	| Number of raw data values |
| 00 00 00 00	| Number of properties for /'Group'/'Channel1' (does not have properties) |

The following table is an example of the DAQmx raw data index.

| **Binary footprint (hexadecimal)** | **Description** |
| --- | --- |
| 03 00 00 00	| Number of objects |
| 23 00 00 00	| Length of the group object path |
| 2F 27 4D 65 61 73 75 72 65 64 20 54 68 72 6F 75 67 68 70 75 74 20 44 61 74 61 20 28 56 6F 6C 74 73 29 27 | Object path (/'Measured Throughput Data (Volts)') |
| FF FF FF FF	| Raw data index ("FF FF FF FF" means there is no raw data assigned to the object) |
| 00 00 00 00	| Number of properties for /'Measured Throughput Data (Volts)' |
| 34 00 00 00	| Length of the channel object path |
| 2F 27 4D 65 61 73 75 72 65 64 20 54 68 72 6F 75 67 68 70 75 74 20 44 61 74 61 20 28 56 6F 6C 74 73 29 27 2F 27 50 58 49 31 53 6C 6F 74 30 33 2d 61 69 30 27 69 12 00 00	| /'Measured Throughput Data (Volts)'/'PXI1Slot03-ai0' |
| 69 12 00 00	| DAQmx raw data index and contains Format Changing scaler |
| FF FF FF FF	| Data type, DAQmx raw data |
| 01 00 00 00	| Data dimension |
| 00 00 00 00 00 00 00 00	| Number of values, no values in this segment |
| 01 00 00 00 | Size of the vector of Format Changing scalers |
| 05 00 00 00 | DAQmx Data Type of the first Format Changing scaler |
| 00 00 00 00 | Raw buffer index of the first Format Changing scaler |
| 00 00 00 00 | Raw byte offset within the stride |
| 00 00 00 00 | Sample format bitmap |
| 00 00 00 00 | Scale ID |
| 01 00 00 00 | Size of the vector of raw data width |
| 08 00 00 00 | First element in the vector of the raw data width |
| 06 00 00 00 | Number of properties for /'Measured Throughput Data (Volts)'/'PXI1Slot03-ai0' |
| 11 00 00 00	| Length of the first property name |
| 4E 49 5F 53 63 61 6C 69 6E 67 5F 53 74 61 74 75 73 | Property name ("NI_Scaling_Status") |
| 20 00 00 00	| Data type of the property value (tdsTypeString) |
| 08 00 00 00	| Length of the property value (only for strings) |
| 75 6E 73 63 61 6C 65 64	| Value of the property prop ("unscaled") |
| 13 00 00 00	| Length of the second property name |
| 4E 49 5F 4E 75 6D 62 65 72 5F 4F 66 5F 53 63 61 6C 65 73 | Property name ("NI_Number_Of_Scales") |
| 07 00 00 00	| Data type of the property value (tdsTypeU32) |
| 02 00 00 00	| Value of the property (2) |
| 16 00 00 00	| Length of the third property name |
| 4E 49 5F 53 63 61 6C 65 5B 31 5D 5F 53 63 61 6C 65 5F 54 79 70 65	| Property name ("NI_Scale[1]_Scale_Type") |
| 20 00 00 00	| Data type of the property (tdsTypeString) |
| 06 00 00 00	| Length of the property value |
| 4C 69 6E 65 61 72	| Property value ("Linear") |
| 18 00 00 00	| Length of the fourth property name |
| 4E 49 5F 53 63 61 6C 65 5B 31 5D 5F 4C 69 6E 65 61 72 5F 53 6C 6F 70 65	| Property name ("NI_Scale[1]_Linear_Slope") |
| 0A 00 00 00	| Data type of the property (tdsTypeDoubleFloat) |
| 04 E9 47 DD CB 17 1D 3E	| Property value (1.693433E-9) |
| 1E 00 00 00	| Length of the fifth property name |
| 4E 49 5F 53 63 61 6C 65 5B 31 5D 5F 4C 69 6E 65 61 72 5F 59 5F 49 6E 74 65 72 63 65 70 74	| Property name ("NI_Scale[1]_Linear_Y_Intercept") |
| 0A 00 00 00	| Data type of the property (tdsTypeDoubleFloat) |
| 00 00 00 00 00 00 00 00	| Property value (0) |
| 1F 00 00 00	| Length of the sixth property name |
| 4E 49 5F 53 63 61 6C 65 5B 31 5D 5F 4C 69 6E 65 61 72 5F 59 6E 70 75 74 5F 53 6F 75 72 63 65 |Property name ("NI_Scale[1]_Linear_Input_Source") |
| 07 00 00 00	| Data type of the property (tdsTypeU32) |
| 00 00 00 00	| Property value (0) |

From the previous table, the channel "/'Measured Throughput Data (Volts)'/'PXI1Slot03-ai0" contains two scalers. One scaler is Format Changing, where the information of the Format Changing scaler is stored in the DAQmx raw data index. The other scaler is a Linear scaler, where the information is stored as TDMS properties. The Format Changing scaler is identifiable where the Slope of hte Linear scaler is 1.693433E-9, the Intercept is 0, and the Input Source ID is 0.

Meta information that matches meta information in the previous segments can be omitted in following segments. This is optional, but omitting redundant meta information significantly speeds up reading the file. If you choose to write redundant information, you can later remove it using the TDMS Defragment function in LabVIEW, LabWindows/CVI, MeasurementStudio, etc.

* Writing a new object to the next segment will imply that the segment contains all objects from the previous segment, plus the new objects described here. If the new segment does not contain any channel(s) from the previous segment, or if the order of channels in segment changes, the new segment needs to contain a new list of all objects. Refer to the Optimization section of this article for more information.
* Writing a new property to an object that already exists in the previous segment will add this property to the object.
* Writing a property that already exists on an object will overwrite the previous value of that property.

Note: In TDMS file format version 2.0, specifying a value for the name property of an existing object will rename that object.

The following example shows the binary footprint for the meta data section of a segment directly following the segment described above. The only meta information written to the new segment is the new property value.

| **Binary layout (hexadecimal)** | **Description** |
| --- | --- |
| 01 00 00 00	| Number of new/changed objects |
| 08 00 00 00	| Length of object path |
| 2F 27 47 72 6F 75 70 27	| Object path (/'Group') |
| FF FF FF FF	| Raw data index (no raw data assigned to the object) |
| 01 00 00 00	| Number of new/changed properties |
| 03 00 00 00	| Length of property name |
| 6E 75 6D | Property name (num) |
| 03 00 00 00	| Data type of the property value (tdsTypeI32) |
| 07 00 00 00	| New value for property num (7) |

### Raw Data

The segment finally contains the raw data associated with each channel. The data arrays for all channels are concatenated in the exact order in which the channels appear in the meta information part of the segment. Numeric data needs to be formatted according to the little-endian/big-endian flag in the lead in. Note that channels cannot change their endian format or data type once they have been written for the first time.

String type channels are preprocessed for fast random access. All strings are concatenated to a contiguous piece of memory. The offset of the first character of each string in this contiguous piece of memory is stored to an array of unsigned 32-bit integers. This array of offset values is stored first, followed by the concatenated string values. This layout allows client applications to access any string value from anywhere in the file by repositioning the file pointer a maximum of three times and without reading any data that is not needed by the client.

If meta information between segments does not change, the lead in and meta information parts can be completely omitted and raw data can just be appended to the end of the file.

Raw data can be organized into two types of layout: interleaved and non-interleaved. The ToC bit mask in the segment lead in declares whether or not data in the segment is interleaved. For example: storing 32-bit integer values to channel 1 (1,2,3) and channel 2 (4,5,6) results in the following layouts:

| **Data Layout** | **Binary Footprint (hexadecimal)** |
| --- | --- |
| Non-interleaved	| 01 00 00 00  02 00 00 00  03 00 00 00  04 00 00 00  05 00 00 00  06 00 00 00 |
| Interleaved	| 01 00 00 00  04 00 00 00  02 00 00 00 05 00 00 00  03 00 00 00  06 00 00 00 |

## Data Type Values

The following enum type describes the data type of a property or channel in a TDMS file. For properties, the data type value will be stored in between the name and the binary value. For channels, the data type will be part of the raw data index.

```code
typedef enum {
  tdsTypeVoid,
  tdsTypeI8,
  tdsTypeI16,
  tdsTypeI32,
  tdsTypeI64,
  tdsTypeU8,
  tdsTypeU16,
  tdsTypeU32,
  tdsTypeU64,
  tdsTypeSingleFloat,
  tdsTypeDoubleFloat,
  tdsTypeExtendedFloat,
  tdsTypeSingleFloatWithUnit=0x19,
  tdsTypeDoubleFloatWithUnit,
  tdsTypeExtendedFloatWithUnit,
  tdsTypeString=0x20,
  tdsTypeBoolean=0x21,
  tdsTypeTimeStamp=0x44,
  tdsTypeFixedPoint=0x4F,
  tdsTypeComplexSingleFloat=0x08000c,
  tdsTypeComplexDoubleFloat=0x10000d,
  tdsTypeDAQmxRawData=0xFFFFFFFF
} tdsDataType;
```

**Notes:**

- Refer to the LabVIEW Timestamp article for more information about using tdsTypeTimeStamp in LabVIEW.
- LabVIEW floating-point types with unit translate into a floating-point channel with a property named unit_string that contains the unit as a string.

## Predefined Properties

LabVIEW waveforms are represented in TDMS files as numeric channels, where the waveform attributes are added to the channel as properties.

- wf_start_time–This property represents the time at which the waveform was acquired or generated.
- wf_start_offset–This property is used for the LabVIEW Express Dynamic Data Type.
- wf_increment–This property represents the increment between two consecutive samples on the x-axis.
- wf_samples–This property represents the number of samples in the waveform.

## Optimization

Applying the format definition as described in the previous sections creates perfectly valid TDMS files. However, TDMS allows for a variety of optimizations that are commonly used by NI software like LabVIEW, LabWindows/CVI, MeasurementStudio, etc. Applications that are trying to read data written by NI software need to support the optimization mechanisms described in this paragraph.

### Incremental Meta Information Example

Meta information such as object paths, properties, and raw indexes, is added to a segment only if it changes.

Incremental meta information is explained in the following example.

In the first writing iteration, channel 1 and channel 2 are written. Each channel has three 32-bit integer values (1,2,3 and 4,5,6) and several descriptive properties. The meta information part of the first segment contains paths, properties, and raw data indexes for channel 1 and channel 2. The flags kTocMetaData, kTocNewObjList, and kTocRawData of the ToC bit field are set. The first writing iteration creates a data segment. The following table describes the binary footprint of the first segment.

| **Part** | **Binary**  **Footprint (hexadecimal)** |
| --- | --- |
| Lead In	| 54 44 53 6D  0E 00 00 00  69 12 00 00 8F 00 00 00  00 00 00 00  77 00 00 00 00 00 00 00 |
|Number of objects | 02 00 00 00 | 
|Meta information object 1 |	13 00 00 00  2F 27 67 72  6F 75 70 27  2F 27 63 68  61 6E 6E 65  6C 31 27 14 00 00 00 03  00 00 00 01  00 00 00 03 00 00 00 00  00 00 00 01  00 00 00 04 00 00 00 70  72 6F 70 20  00 00 00 05 00 00 00 76  61 6C 69 64 |
|Meta information object 2 |	13 00 00 00  2F 27 67 72  6F 75 70 27 2F 27 63 68  61 6E 6E 65  6C 32 27 14 00 00 00 03  00 00 00 01  00 00 00 03 00 00 00 00  00 00 00 00  00 00 00 |
|Raw data channel 1	| 01 00 00 00  02 00 00 00  03 00 00 00 |
|Raw data channel 2	| 04 00 00 00  05 00 00 00  06 00 00 00 |

In the second writing iteration, none of the properties have changed, channel 1 and channel 2 still have three values each, and no additional channels are written. Therefore, this iteration will not write any meta data. The meta data from the previous segment is still assumed valid. This iteration will not create a new segment; instead, this iteration only appends the raw data to the existing segment and then updates the Next Segment Offset in the Lead In section. The following table describes the binary footprint of the updated segment.

| **Part** | **Binary**  **Footprint (hexadecimal)** |
| --- | --- |
| Lead In |	54 44 53 6D  0E 00 00 00  69 12 00 00 A7 00 00 00  00 00 00 00  77 00 00 00 00 00 00 00
| Number of objects	| 02 00 00 00 | 
| Meta information object 1 |	13 00 00 00  2F 27 67 72  6F 75 70 27 2F 27 63 68  61 6E 6E 65  6C 31 27 14 00 00 00 03  00 00 00 01  00 00 00 03 00 00 00 00  00 00 00 01  00 00 00 04 00 00 00 70  72 6F 70 20  00 00 00 05 00 00 00 76  61 6C 69 64
| Meta information object 2 |	13 00 00 00  2F 27 67 72  6F 75 70 27 2F 27 63 68  61 6E 6E 65  6C 32 27 14 00 00 00 03  00 00 00 01  00 00 00 03 00 00 00 00  00 00 00 00  00 00 00 |
| Raw data channel 1 |	01 00 00 00  02 00 00 00  03 00 00 00 |
| Raw data channel 2 |	04 00 00 00  05 00 00 00  06 00 00 00 |
| Raw data channel 1 |	01 00 00 00  02 00 00 00  03 00 00 00 |
| Raw data channel 2 |	04 00 00 00  05 00 00 00  06 00 00 00 |

In the previous table, the last two rows contain data appended to the first segment during the second writing iteration.

The third writing iteration adds another three values to each channel. In channel 1, the property status was set to valid in the first segment, but now needs to be set to error. This iteration will create a new segment and the meta data section of this segment now contains the object path for channel, name, type, and value for this property. In future file reads, the error value will override the previously written valid value. However, the previous valid value remains in the file, unless it is defragmented. The following table describes the binary footprint of the second segment.

| **Part** | **Binary**  **Footprint (hexadecimal)** |
| --- | --- |
| Lead In	| 54 44 53 6D  0A 00 00 00  69 12 00 00 50 00 00 00  00 00 00 00  38 00 00 00 00 00 00 00 |
| Number of objects	| 01 00 00 00 |
| Meta information object 1 |	13 00 00 00  2F 27 67 72  6F 75 70 27 2F 27 63 68  61 6E 6E 65  6C 31 27 0000 00 00 01  00 00 00 04  00 00 00 70   72 6F 70 20  00 00 00 05  00 00 00 65   72 72 6F 72 |
| Raw data channel 1 |	01 00 00 00  02 00 00 00  03 00 00 00 |
| Raw data channel 2 | 04 00 00 00  05 00 00 00  06 00 00 00 |


The fourth writing iteration adds an additional channel, voltage, which contains five values (7,8,9,10,11). This iteration will create a new segment, the third segment, in the TDMS file. Because all other meta data from the previous segment is still valid, the meta data section of the fourth segment includes the object path, the properties, and the index information for channel voltage only. The raw data section contains three values for channel 1, three values for channel 2, and five values for channel voltage. The following table describes the binary footprint of the third segment.


| **Part** | **Binary**  **Footprint (hexadecimal)** |
| --- | --- |
|Lead In | 54 44 53 6D  0A 00 00 00  69 12 00 00 5E 00 00 00  00 00 00 00  32 00 00 00 00 00 00 00 |
|Number of objects | 01 00 00 00 |
|Meta information object 3 |	12 00 00 00  2F 27 67 72  6F 75 70 27 2F 27 76 6F  6C 74 61 67  65 27 14 00 00 00 03 00  00 00 01 00  00 00 05 00 00 00 00 00  00 00 00 00  00 00 |
|Raw data channel 1	| 01 00 00 00  02 00 00 00  03 00 00 00 |
|Raw data channel 2	| 04 00 00 00  05 00 00 00  06 00 00 00 |
|Raw data channel 3	| 07 00 00 00  08 00 00 00  09 00 00 00 0A 00 00 00  0B 00 00 00 |

In the fourth segment, channel 2 now has 27 values. All other channels remain unchanged. The meta data section now contains the object path for channel 2, the new raw data index for channel 2, and no properties for channel 2. The following table describes the binary footprint of the fourth segment.

| **Part** | **Binary**  **Footprint (hexadecimal)** |
| --- | --- |
| Lead In	 | 54 44 53 6D  0A 00 00 00  69 12 00 00 BF 00 00 00  00 00 00 00  33 00 00 00 00 00 00 00 |
| Number of objects	| 01 00 00 00 | 
| Meta information object | 2	13 00 00 00  2F 27 67 72  6F 75 70 27 2F 27 63 68  61 6E 6E 65  6C 32 27 14 00 00 00 03  00 00 00 01  00 00 00 1B 00 00 00 00  00 00 00 00  00 00 00 |
| Raw data channel 1 |	01 00 00 00  02 00 00 00  03 00 00 00 |
| Raw data channel 2 |	01 00 00 00  02 00 00 00  03 00 00 00 04 00 00 00  05 00 00 00  06 00 00 00 07 00 00 00  08 00 00 00  09 00 00 00 0A 00 00 00  0B 00 00 00  0C 00 00 00 0D 00 00 00  0E 00 00 00  0F 00 00 00 10 00 00 00  11 00 00 00  12 00 00 00 13 00 00 00  14 00 00 00  15 00 00 00 16 00 00 00  17 00 00 00  18 00 00 00 19 00 00 00  1A 00 00 00  1B 00 00 00 |
| Raw data channel 3 |	07 00 00 00  08 00 00 00  09 00 00 00 0A 00 00 00  0B 00 00 00 |

In the fifth segment, the application stops writing to channel 2. The application only continues writing to channel 1 and channel voltage. This constitutes a change in the channel order, which requires you to write a new list of channel paths. You must set the ToC bit kTocNewObjList. The meta data section of the new segment must contain a complete list of all object paths, but no properties and no raw data indexes, unless they also change. The following table describes the binary footprint of the fifth segment.

| **Part** | **Binary**  **Footprint (hexadecimal)** |
| --- | --- |
| Lead In	| 54 44 53 6D  0E 00 00 00  69 12 00 00 61 00 00 00  00 00 00 00  41 00 00 00 00 00 00 00 |
| Number of objects |	02 00 00 00 |
| Meta information object 1	| 13 00 00 00  2F 27 67 72  6F 75 70 27 2F 27 63 68  61 6E 6E 65  6C 31 27 00 00 00 00 00  00 00 00 |
| Meta information object 2	| 12 00 00 00  2F 27 67 72  6F 75 70 27 2F 27 76 6F  6C 74 61 67  65 27 00 00 00 00 00 00  00 00 |
| Raw data channel 1 | 01 00 00 00  02 00 00 00  03 00 00 00 |
| Raw data channel 3 | 07 00 00 00  08 00 00 00  09 00 00 00 0A 00 00 00  0B 00 00 00 |

### Index Files

All data written to a TDMS file is stored to a file with the extension \*.tdms. TDMS files can be accompanied by a \*.tdms_index optional index file. The index file is used to speed up reading from the \*.tdms file. If an NI application opens a TDMS file without an index file, the application automatically creates the index file. If an NI application, such as LabVIEW or LabWindows/CVI, writes a TDMS file, the application creates the index file and the main file at the same time.

The index file is an exact copy of the \*.tdms file, except that it does not contain any raw data and every segment starts with a TDSh tag instead of a TDSm tag. The index file contains all information to precisely locate any value of any channel within the \*.tdms file.

## Conclusion

In brief, the TDMS file format is designed to write and read measured data at very high speed, while maintaining a hierarchical system of descriptive information. While the binary layout by itself is rather simple, the optimizations enabled by writing meta data incrementally can lead to very sophisticated file configurations.