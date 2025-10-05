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


