### Introduction

**etrace** is a command-line tool for realtime tracing of ETW events and for
processing existing .etl recording files. It was inspired by the Microsoft
[ELT](https://github.com/Microsoft/Microsoft.Diagnostics.Tracing.Logging/tree/master/utils/LogTool)
tool.

Most use-cases for etrace involve a production environment where you want quick
trace results without creating a long-running recording and then opening it with
a tool like WPA or PerfView. It can also be used for a quick query over an
existing trace file to see some basic event information without opening the
entire trace.

> etrace is under active development -- it is not at all ready for production
> use yet. In fact, I am writing these lines after a talk for which I published
> etrace on GitHub just so that I can mention it during the talk :-)
> Contributions are very welcome!

### Examples

To run these examples yourself, build the etrace solution and run a command
prompt as administrator. (ETW collection requires administrative privileges;
you can process existing .etl files with standard user privileges.)

Collect garbage collection allocation ticks across all processes and print
the raw events:

```
> etrace --clr GC --event GC/AllocationTick

Processing start time: 9/17/2016 12:14:20 PM
Session start time: 9/17/2016 12:14:20 PM
GC/AllocationTick [PID=14380 TID=12464 TIME=9/17/2016 12:14:20 PM]
  AllocationAmount     = 107136
  AllocationKind       = Small
  ClrInstanceID        = 9
  AllocationAmount64   = 107136
  TypeID               = 84331216
  TypeName             = Microsoft.Diagnostics.Tracing.Parsers.Kernel.FileIONameTraceData
  HeapIndex            = 0
  Address              = 45424572
GC/AllocationTick [PID=1308 TID=5216 TIME=9/17/2016 12:14:20 PM]
  AllocationAmount     = 106876
  AllocationKind       = Small
  ClrInstanceID        = 47
  AllocationAmount64   = 106876
  TypeID               = 1917056168
  TypeName             = System.Double
  HeapIndex            = 0
  Address              = 904193672
^C
```

Print a message every time a process is started, with the parent process and 
image file name:

```
> etrace --kernel Process --event Process/Start --field ParentID,ImageFileName
Processing start time: 9/17/2016 12:15:23 PM
Session start time: 9/17/2016 12:15:23 PM
ParentID                       ImageFileName
--------------------------------------------------------------
4840                           notepad.exe
Events lost: 0
^C
```

Filter out file accesses to all DLLs and print their details:

```
> etrace --kernel Process,Thread,FileIO,FileIOInit --event FileIO/Create --where "FileName=\.dll$"
FileIO/Create [PID=2148 TID=14540 TIME=9/17/2016 12:17:34 PM]
  IrpPtr               = 18446717194321646184
  FileObject           = 18446717194317992256
  CreateOptions        = FILE_SYNCHRONOUS_IO_NONALERT, FILE_OPEN_FOR_BACKUP_INTENT
  FileAttributes       = 0
  ShareAccess          = ReadWrite, Delete
  FileName             = C:\Windows\System32\rmclient.dll
FileIO/Create [PID=9396 TID=8880 TIME=9/17/2016 12:17:34 PM]
  IrpPtr               = 18446717194308377208
  FileObject           = 18446717194278651104
  CreateOptions        = NONE
  FileAttributes       = 0
  ShareAccess          = ReadWrite, Delete
  FileName             = C:\WINDOWS\system32\directmanipulation.dll
```

Process an existing .etl recording and print out statistics about specific
events:

```
> etrace --file trace.etl --event GC/Start,GC/AllocationTick --stats
Processing start time: 9/17/2016 12:19:19 PM
Session start time: 6/2/2016 3:55:46 PM
Events by name
 -----------------------------
 | Event             | Count |
 -----------------------------
 | GC/AllocationTick | 33745 |
 -----------------------------
 | GC/Start          | 4212  |
 -----------------------------

 Count: 2

Events by process
 --------------------------
 | Process      | Count   |
 --------------------------
 | JackCompiler | 37709   |
 --------------------------
 |              | 215     |
 --------------------------
 | devenv       | 18      |
 --------------------------
 | PerfView     | 15      |
 --------------------------

 Count: 4


Processing end time:           9/17/2016 12:19:21 PM
Processing duration:           00:00:01.3438275
Processed events:              638233
Displayed events:              37957
```

To get a list of CLR keywords, kernel keywords, or registered providers on
your system:

```
> etrace --list CLR

Supported CLR keywords (use with --clr):

        None
        GC
        GCHandle
        Binder
        Loader
        Jit
        NGen
        StartEnumeration
        StopEnumeration
        Security
        AppDomainResourceManagement
        JitTracing
        Interop
        Contention
        Exception
		... output snipped for brevity
```

To see the usage message, run `etrace --help`:

```
etrace 1.0.0.0
Copyright Sasha Goldshtein 2016

  --raw         Regular expression to match against the entire event
                description. This is not very efficient; prefer using --where
                if possible.

  --where       Filter payload fields with a regular expression. For example:
                ImageFileName=notepad,ParentID=4840

  --pid         (Default: -1) Filter only events from this process.

  --tid         (Default: -1) Filter only events from this thread.

  --event       Filter only these events. For example:
                FileIO/Create,Process/Start

  --clr         The CLR keywords to enable.

  --kernel      The kernel keywords to enable.

  --other       Other (non-kernel, non-CLR) providers to enable. A list of
                GUIDs or friendly names.

  --file        The ETL file to process.

  --list        List keywords and/or providers. Options include: CLR, Kernel,
                Registered, Published, or a comma-separated combination thereof.

  --stats       Display only statistics and not individual events.

  --field       Display only these payload fields (if they exist). The special
                fields Event, PID, TID, Time can be specified for all events.
                An optional width specifier can be provided in square brackets.
                For example: PID,TID,ProcessName[16],Receiver[30],Time

  --duration    Number of seconds after which to stop the trace. Relevant for
                realtime sessions only.

  --help        Display this help screen.


Examples:
  etrace --clr GC --event GC/AllocationTick
  etrace --kernel Process,Thread,FileIO,FileIOInit --event File/Create
  etrace --file trace.etl --stats
  etrace --clr GC --event GC/Start --field PID,TID,Reason[12],Type
  etrace --kernel Process --event Process/Start --where ImageFileName=myapp
  etrace --clr GC --event GC/Start --duration 60
  etrace --other Microsoft-Windows-Win32k --event QueuePostMessage
  etrace --list CLR,Kernel
```
