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



# TDMS Index File Specification - ADDENDUM

---

## 13. TDMS Index Files (.tdms_index)

### 13.1 Purpose and Benefits

TDMS index files are companion files that store **only metadata** to enable:
- **Fast metadata reading** without scanning large data files
- **Quick file structure discovery** for file browsers
- **Efficient random access** to channel data
- **Reduced I/O** when repeatedly accessing file structure

### 13.2 Index File Location and Naming

**Naming Convention:**
```
Data file:  measurement.tdms
Index file: measurement.tdms_index
```

**Location Rules:**
1. Index file MUST be in same directory as data file
2. Index file name MUST be data file name + `_index` suffix
3. Both files must be accessible with same relative path

### 13.3 Index File Structure

Index files mirror the segment structure of data files but contain **only lead-ins and metadata**:

```
IndexFile = IndexSegment₁ + IndexSegment₂ + ... + IndexSegmentₙ

IndexSegment = LeadIn(28) + MetaData(variable)
```

#### 13.3.1 Index Segment Lead-In

Identical to data file lead-in except:

| Offset | Size | Type | Description |
|--------|------|------|-------------|
| 0 | 4 | Char[4] | Tag: **`TDSh`** (not TDSm) |
| 4 | 4 | Int32 | Table of Contents mask (same as data file) |
| 8 | 4 | Int32 | Version number (same as data file) |
| 12 | 8 | Int64 | Next segment offset (in INDEX file) |
| 20 | 8 | Int64 | Raw data offset (from DATA file segment start) |

**Critical Notes:**
- `NextSegmentOffset` is relative to INDEX file positions
- `RawDataOffset` references positions in the DATA file
- ToC flags reflect the corresponding data segment's flags

#### 13.3.2 Index Metadata Section

**Must contain exact same metadata as corresponding data segment:**
- Same number of objects
- Identical object paths
- Identical raw data index headers
- Identical data type information
- Identical properties
- Same byte-for-byte metadata content

**What's Omitted:**
- All raw data bytes
- Everything after metadata section

### 13.4 Index File Creation Algorithm

```python
def create_index_file(tdms_file_path, index_file_path):
    """
    Create a TDMS index file from a TDMS data file.
    """
    with open(tdms_file_path, 'rb') as data_file:
        with open(index_file_path, 'wb') as index_file:
            
            index_position = 0
            
            while True:
                # Read segment lead-in from data file
                segment_start = data_file.tell()
                lead_in = data_file.read(28)
                
                if len(lead_in) < 28:
                    break  # End of file
                
                # Parse lead-in
                tag = lead_in[0:4]
                if tag != b'TDSm':
                    raise ValueError(f"Invalid segment tag: {tag}")
                
                toc_mask = struct.unpack('<I', lead_in[4:8])[0]
                version = struct.unpack('<I', lead_in[8:12])[0]
                next_seg_offset = struct.unpack('<Q', lead_in[12:20])[0]
                raw_data_offset = struct.unpack('<Q', lead_in[20:28])[0]
                
                # Determine endianness
                endian = '>' if (toc_mask & (1 << 6)) else '<'
                
                # Calculate positions in DATA file
                data_position = segment_start + 28 + raw_data_offset
                
                if next_seg_offset == 0xFFFFFFFFFFFFFFFF:
                    # Incomplete segment
                    next_segment_pos = get_file_size(data_file)
                else:
                    next_segment_pos = segment_start + next_seg_offset + 28
                
                metadata_size = raw_data_offset
                
                # Read metadata if present
                metadata = b''
                if toc_mask & (1 << 1):  # kTocMetaData
                    metadata = data_file.read(metadata_size)
                
                # Calculate next index segment offset
                index_segment_size = 28 + len(metadata)
                if is_last_segment(data_file, next_segment_pos):
                    next_index_offset = 0xFFFFFFFFFFFFFFFF
                else:
                    next_index_offset = index_segment_size - 28
                
                # Write index segment
                index_lead_in = bytearray(lead_in)
                index_lead_in[0:4] = b'TDSh'  # Change tag
                
                # Update next segment offset for INDEX file
                index_lead_in[12:20] = struct.pack(
                    endian + 'Q', next_index_offset)
                
                index_file.write(bytes(index_lead_in))
                index_file.write(metadata)
                
                index_position += index_segment_size
                
                # Seek to next segment in DATA file
                data_file.seek(next_segment_pos)
                
                if next_segment_pos >= get_file_size(data_file):
                    break
```

### 13.5 Index File Size Calculation

For each segment:
```
IndexSegmentSize = 28 + MetadataSize

Where MetadataSize is the RawDataOffset from the segment lead-in
```

**Total Index File Size:**
```
IndexFileSize = Σ(28 + RawDataOffset_i) for all segments
```

**Typical Size Reduction:**
- Data file: Gigabytes (with raw data)
- Index file: Kilobytes to low Megabytes (metadata only)

**Example:**
```
Data File:
  Segment 1: 28 bytes lead-in + 500 bytes metadata + 1 GB data
  Segment 2: 28 bytes lead-in + 0 bytes metadata + 1 GB data  
  Segment 3: 28 bytes lead-in + 200 bytes metadata + 1 GB data
  Total: 3 GB + 756 bytes

Index File:
  Segment 1: 28 bytes lead-in + 500 bytes metadata = 528 bytes
  Segment 2: 28 bytes lead-in + 0 bytes metadata = 28 bytes
  Segment 3: 28 bytes lead-in + 200 bytes metadata = 228 bytes
  Total: 784 bytes
```

### 13.6 Index-Data File Synchronization

#### 13.6.1 Critical Validation Rules

**Rule 1: Segment Count Must Match**
```python
index_segment_count == data_segment_count
```

**Rule 2: Metadata Must Match Exactly**
```python
for each segment i:
    index_metadata[i] == data_metadata[i]  # Byte-for-byte identical
```

**Rule 3: Raw Data Offset Must Match**
```python
for each segment i:
    index_lead_in[i].raw_data_offset == data_lead_in[i].raw_data_offset
```

**Rule 4: Data Position Alignment**
```python
for each segment i:
    # Data position in data file must align with index expectations
    data_segment_start = calculate_from_index(index_file, segment_i)
    verify_data_tag_at_position(data_file, data_segment_start)
```

#### 13.6.2 Mismatch Detection

When reading with an index file, verify alignment before reading data:

```python
def verify_segment_alignment(data_file, segment_position):
    """
    Verify segment starts at expected position in data file.
    Called before reading raw data to detect index/data mismatch.
    """
    data_file.seek(segment_position)
    tag = data_file.read(4)
    
    if tag != b'TDSm':
        raise ValueError(
            f"Segment alignment error at position {segment_position}. "
            f"Expected 'TDSm', found '{tag}'. "
            "Index file may not match data file."
        )
```

**Common Mismatch Scenarios:**
1. **Wrong Data File:** Index from different file
2. **Partial Update:** Data file modified, index not regenerated
3. **Corruption:** One file corrupted but not the other
4. **Version Mismatch:** Files from different acquisition sessions

### 13.7 Index File Reading Algorithm

```python
def read_with_index(tdms_path, index_path):
    """
    Efficiently read TDMS file using index.
    """
    # Phase 1: Read ALL metadata from index file
    metadata = {}
    segment_info = []
    
    with open(index_path, 'rb') as index_file:
        index_pos = 0
        
        while True:
            lead_in = index_file.read(28)
            if len(lead_in) < 28:
                break
            
            if lead_in[0:4] != b'TDSh':
                raise ValueError("Invalid index file tag")
            
            # Parse lead-in
            toc_mask, version, next_offset, raw_data_offset = \
                parse_lead_in(lead_in)
            
            # Read metadata
            if toc_mask & (1 << 1):  # kTocMetaData
                metadata_bytes = index_file.read(raw_data_offset)
                segment_metadata = parse_metadata(metadata_bytes)
            else:
                segment_metadata = None
            
            # Store segment info for data reading
            segment_info.append({
                'toc_mask': toc_mask,
                'raw_data_offset': raw_data_offset,
                'metadata': segment_metadata,
                'data_position': calculate_data_position(...)
            })
            
            # Move to next index segment
            if next_offset == 0xFFFFFFFFFFFFFFFF:
                break
            index_pos += 28 + raw_data_offset
    
    # Phase 2: Build complete object metadata
    objects = build_object_metadata(segment_info)
    
    # Phase 3: Read data on-demand from data file
    def read_channel_data(channel_path, offset=0, length=None):
        with open(tdms_path, 'rb') as data_file:
            for seg_info in segment_info:
                # Verify alignment
                verify_segment_alignment(
                    data_file, seg_info['data_position'])
                
                # Read raw data
                data = read_segment_data(
                    data_file, seg_info, channel_path)
                
                yield data
    
    return objects, read_channel_data
```

### 13.8 Index File Update Strategies

#### 13.8.1 When to Regenerate Index

**Must Regenerate When:**
1. Data file is modified (any segment added/changed)
2. Data file is defragmented/reorganized
3. Index file is missing or corrupt
4. Version mismatch detected

**Optional Regeneration:**
1. Periodic verification (integrity check)
2. After data file repair operations

#### 13.8.2 Incremental Index Updates

For append-only operations (new segments added):

```python
def append_to_index(data_file_path, index_file_path, last_known_segment):
    """
    Incrementally update index file when new segments are added.
    """
    with open(data_file_path, 'rb') as data_file:
        # Seek to last known position
        data_file.seek(last_known_segment.next_position)
        
        with open(index_file_path, 'ab') as index_file:
            # Update previous segment's next_offset if needed
            update_last_segment_offset(index_file, last_known_segment)
            
            # Process new segments
            while True:
                segment = read_segment_metadata(data_file)
                if not segment:
                    break
                
                write_index_segment(index_file, segment)
```

**Warning:** Only safe for truly append-only operations. If any existing segment is modified, full regeneration is required.

### 13.9 Index File Format Edge Cases

#### 13.9.1 Segments Without Metadata

When data segment has no metadata (metadata reuse):

```
Index Segment:
  Tag: TDSh
  ToC: kTocRawData only (NO kTocMetaData flag)
  NextSegmentOffset: 0 (in index file)
  RawDataOffset: 0
  [No metadata bytes]
```

**Index segment is exactly 28 bytes** - just the lead-in.

#### 13.9.2 Incomplete Segments

For incomplete segments in data file:

```
Index Segment:
  Tag: TDSh
  ToC: [Same as data segment]
  NextSegmentOffset: 0xFFFFFFFFFFFFFFFF
  RawDataOffset: [Actual metadata size if metadata exists]
  [Metadata bytes if present]
```

**Important:** Index accurately reflects incomplete status.

#### 13.9.3 First Segment Considerations

First segment index entry:

```python
# First segment absolute position in data file is always 0
first_data_position = 0

# In index file, also starts at 0
first_index_position = 0

# NextSegmentOffset in index is relative to this index segment
# NOT copied from data file
```

### 13.10 Index File Advantages and Limitations

#### 13.10.1 Advantages

**Performance:**
- Metadata read is ~1000x faster for large files
- No seeking through gigabytes of data
- Single sequential read of small file

**Use Cases:**
- File browsers showing channel lists
- Property extraction tools  
- Channel discovery in large datasets
- Building channel selection UIs

**Storage:**
- Minimal disk space (typically <0.01% of data file)
- Can be regenerated if lost
- Compresses well (repeated structure)

#### 13.10.2 Limitations

**Synchronization:**
- Must be kept in sync with data file
- No built-in version tracking
- Corruption of either file problematic

**Portability:**
- Index file must travel with data file
- Missing index file is recoverable (read data file directly)
- Path dependencies (must be in same directory)

**Integrity:**
- No checksums or validation built-in
- Mismatch detection only at read time
- Partial updates are dangerous

### 13.11 Best Practices for Index Files

#### 13.11.1 Creation Guidelines

```python
# DO: Create index immediately after data file is finalized
finalize_tdms_file(data_path)
create_index_file(data_path, index_path)

# DO: Verify index after creation
verify_index_matches_data(data_path, index_path)

# DON'T: Create index for actively written files
if is_file_open_for_writing(data_path):
    raise Error("Wait until data file is closed")

# DON'T: Partially update index files
# Always regenerate completely if data changes
```

#### 13.11.2 Distribution Guidelines

**When Distributing TDMS Files:**

```
Option A - Include Index (Recommended):
  ✓ Faster for recipients
  ✓ Better user experience
  ✓ Minimal size overhead
  package/
    ├── measurement.tdms
    └── measurement.tdms_index

Option B - Omit Index:
  ✓ Simpler (one file)
  ✓ Index auto-generated on first read
  ✓ No sync issues
  package/
    └── measurement.tdms
```

#### 13.11.3 Error Recovery

**If Index File Becomes Invalid:**

```python
try:
    data = read_with_index(tdms_path, index_path)
except IndexMismatchError:
    log.warning("Index file invalid, regenerating...")
    os.remove(index_path)  # Delete bad index
    create_index_file(tdms_path, index_path)
    data = read_with_index(tdms_path, index_path)
```

**If Index File Missing:**

```python
if not os.path.exists(index_path):
    # Option 1: Generate on-demand
    create_index_file(tdms_path, index_path)
    
    # Option 2: Read data file directly (slower)
    data = read_tdms_without_index(tdms_path)
```

### 13.12 Index File Creation Complete Example

```python
def create_complete_index_file(tdms_filepath):
    """
    Complete index file creation with validation.
    """
    index_filepath = tdms_filepath + '_index'
    
    # Temporary index file
    temp_index = index_filepath + '.tmp'
    
    try:
        with open(tdms_filepath, 'rb') as data_file:
            with open(temp_index, 'wb') as index_file:
                
                data_position = 0
                index_position = 0
                prev_index_segment_size = None
                
                while True:
                    # Read data file segment
                    data_file.seek(data_position)
                    lead_in = data_file.read(28)
                    
                    if len(lead_in) < 28:
                        break
                    
                    # Validate
                    if lead_in[0:4] != b'TDSm':
                        raise ValueError(
                            f"Invalid segment at {data_position}")
                    
                    # Parse lead-in
                    toc_mask = struct.unpack('<I', lead_in[4:8])[0]
                    endian = '>' if (toc_mask & 0x40) else '<'
                    next_offset = struct.unpack(
                        endian + 'Q', lead_in[12:20])[0]
                    raw_offset = struct.unpack(
                        endian + 'Q', lead_in[20:28])[0]
                    
                    # Read metadata
                    metadata = b''
                    if toc_mask & 0x02:  # kTocMetaData
                        metadata = data_file.read(raw_offset)
                    
                    # Calculate next index offset
                    index_segment_size = 28 + len(metadata)
                    
                    if next_offset == 0xFFFFFFFFFFFFFFFF:
                        next_index_offset = 0xFFFFFFFFFFFFFFFF
                    else:
                        next_index_offset = index_segment_size - 28
                    
                    # Create index lead-in
                    index_lead_in = bytearray(lead_in)
                    index_lead_in[0:4] = b'TDSh'
                    index_lead_in[12:20] = struct.pack(
                        endian + 'Q', next_index_offset)
                    
                    # Write index segment
                    index_file.write(bytes(index_lead_in))
                    index_file.write(metadata)
                    
                    # Update positions
                    if next_offset == 0xFFFFFFFFFFFFFFFF:
                        break
                    
                    data_position = data_position + next_offset + 28
                    index_position += index_segment_size
                    prev_index_segment_size = index_segment_size
        
        # Validation: verify index file
        validate_index_file(temp_index, tdms_filepath)
        
        # Atomic replace
        if os.path.exists(index_filepath):
            os.remove(index_filepath)
        os.rename(temp_index, index_filepath)
        
        return index_filepath
        
    except Exception as e:
        # Cleanup on error
        if os.path.exists(temp_index):
            os.remove(temp_index)
        raise

def validate_index_file(index_path, data_path):
    """
    Validate index file matches data file structure.
    """
    with open(index_path, 'rb') as idx:
        with open(data_path, 'rb') as dat:
            
            idx_pos = 0
            dat_pos = 0
            segment_num = 0
            
            while True:
                # Read both lead-ins
                idx.seek(idx_pos)
                dat.seek(dat_pos)
                
                idx_lead = idx.read(28)
                dat_lead = dat.read(28)
                
                if len(idx_lead) < 28 or len(dat_lead) < 28:
                    # Both should end together
                    if len(idx_lead) != len(dat_lead):
                        raise ValueError(
                            "Segment count mismatch")
                    break
                
                # Verify tags
                if idx_lead[0:4] != b'TDSh':
                    raise ValueError(
                        f"Invalid index tag at segment {segment_num}")
                if dat_lead[0:4] != b'TDSm':
                    raise ValueError(
                        f"Invalid data tag at segment {segment_num}")
                
                # Compare ToC, version, raw data offset
                if idx_lead[4:8] != dat_lead[4:8]:  # ToC
                    raise ValueError(
                        f"ToC mismatch at segment {segment_num}")
                if idx_lead[8:12] != dat_lead[8:12]:  # Version
                    raise ValueError(
                        f"Version mismatch at segment {segment_num}")
                if idx_lead[20:28] != dat_lead[20:28]:  # Raw offset
                    raise ValueError(
                        f"Raw offset mismatch at segment {segment_num}")
                
                # Read and compare metadata
                endian = '>' if (idx_lead[4] & 0x40) else '<'
                raw_offset = struct.unpack(
                    endian + 'Q', idx_lead[20:28])[0]
                
                if raw_offset > 0:
                    idx_meta = idx.read(raw_offset)
                    dat_meta = dat.read(raw_offset)
                    
                    if idx_meta != dat_meta:
                        raise ValueError(
                            f"Metadata mismatch at segment {segment_num}")
                
                # Calculate next positions
                idx_next = struct.unpack(
                    endian + 'Q', idx_lead[12:20])[0]
                dat_next = struct.unpack(
                    endian + 'Q', dat_lead[12:20])[0]
                
                if idx_next == 0xFFFFFFFFFFFFFFFF:
                    break
                
                idx_pos += idx_next + 28
                dat_pos += dat_next + 28
                segment_num += 1
    
    return True
```

### 13.13 Index File Format Summary

**Key Points:**
1. ✅ Index files store only lead-ins and metadata
2. ✅ Tag is `TDSh` instead of `TDSm`
3. ✅ NextSegmentOffset is relative to index file positions
4. ✅ RawDataOffset references data file positions
5. ✅ Metadata must match data file exactly
6. ✅ Provides massive performance improvement for metadata access
7. ✅ File size is typically <0.01% of data file
8. ⚠️ Must be kept synchronized with data file
9. ⚠️ No built-in integrity checking
10. ⚠️ Mismatch detection happens at read time

**Critical Rule:** Index files are **metadata caches** - they must perfectly mirror the data file's metadata or be discarded and regenerated.