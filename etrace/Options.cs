using CommandLine;
using CommandLine.Text;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace etrace
{
    [Flags]
    enum ListFlags: int
    {
        None       = 0x0,
        Kernel     = 0x1,
        CLR        = 0x2,
        Registered = 0x4,
        Published  = 0x8,
        All        = Kernel | CLR | Registered | Published
    }

    class Options
    {
        [Option("raw", Required = false, MutuallyExclusiveSet = "filter",
            HelpText = "Regular expression to match against the entire event description. This is not very efficient; prefer using --where if possible."
            )]
        public string RawFilter { get; set; } = "";

        [OptionList("where", Required = false, Separator = ',', MutuallyExclusiveSet = "filter",
            HelpText = "Filter payload fields with a regular expression. For example: ImageFileName=notepad,ParentID=4840"
            )]
        public List<string> Filters { get; set; } = new List<string>();

        [Option("pid", Required = false, DefaultValue = -1,
            HelpText = "Filter only events from this process.")]
        public int ProcessID { get; set; }

        [Option("tid", Required = false, DefaultValue = -1,
            HelpText = "Filter only events from this thread.")]
        public int ThreadID { get; set; }

        [OptionList("event", Required = false, Separator = ',',
            HelpText = "Filter only these events. For example: FileIO/Create,Process/Start")]
        public List<string> Events { get; set; } = new List<string>();

        [OptionList("clr", Separator = ',', Required = false,
            HelpText = "The CLR keywords to enable.")]
        public List<string> ClrKeywords { get; set; } = new List<string>();

        [OptionList("kernel", Separator = ',', Required = false,
            HelpText = "The kernel keywords to enable.")]
        public List<string> KernelKeywords { get; set; } = new List<string>();

        [OptionList("other", Separator = ',', Required = false,
            HelpText = "Other (non-kernel, non-CLR) providers to enable. A list of GUIDs or friendly names."
            )]
        public List<string> OtherProviders { get; set; } = new List<string>();

        [Option("file", Required = false, HelpText = "The ETL file to process.")]
        public string File { get; set; }

        [Option("list", Required = false,
            HelpText = "List keywords and/or providers. Options include: CLR, Kernel, Registered, Published, or a comma-separated combination thereof."
            )]
        public ListFlags List { get; set; }

        [Option("stats", Required = false,
            HelpText = "Display only statistics and not individual events.")]
        public bool StatsOnly { get; set; }

        [OptionList("field", Required = false, Separator = ',',
            HelpText = "Display only these payload fields (if they exist). The special fields Event, PID, TID, Time can be specified for all events. An optional width specifier can be provided in square brackets. For example: PID,TID,ProcessName[16],Receiver[30],Time"
            )]
        public List<string> DisplayFields { get; set; } = new List<string>();

        [Option("duration", Required = false,
            HelpText = "Number of seconds after which to stop the trace. Relevant for realtime sessions only."
            )]
        public int DurationInSeconds { get; set; } = 0;

        [HelpOption]
        public string Usage()
        {
            var help = HelpText.AutoBuild(this);
            help.AddPostOptionsLine("Examples:");
            help.AddPostOptionsLine("  etrace --clr GC --event GC/AllocationTick");
            help.AddPostOptionsLine("  etrace --kernel Process,Thread,FileIO,FileIOInit --event File/Create");
            help.AddPostOptionsLine("  etrace --file trace.etl --stats");
            help.AddPostOptionsLine("  etrace --clr GC --event GC/Start --field PID,TID,Reason[12],Type");
            help.AddPostOptionsLine("  etrace --kernel Process --event Process/Start --where ImageFileName=myapp");
            help.AddPostOptionsLine("  etrace --kernel Process --where ProcessId=4");
            help.AddPostOptionsLine("  etrace --kernel Process --where ProcessName=myProcessName");	
            help.AddPostOptionsLine("  etrace --kernel Process --event Thread/Stop --where ThreadId=10272");
            help.AddPostOptionsLine("  etrace --clr GC --event GC/Start --duration 60");
            help.AddPostOptionsLine("  etrace --other Microsoft-Windows-Win32k --event QueuePostMessage");
            help.AddPostOptionsLine("  etrace --list CLR,Kernel");
            return help.ToString();
        }

        public void PostParse()
        {
            foreach (var filter in Filters)
            {
                var parts = filter.Split('=');
                if (parts.Length != 2)
                    throw new ArgumentException($"Invalid filter: {filter}");

                ParsedFilters.Add(parts[0], new Regex(parts[1], RegexOptions.Compiled));
            }

            if (!String.IsNullOrEmpty(RawFilter))
                ParsedRawFilter = new Regex(RawFilter, RegexOptions.Compiled);

            foreach (var keyword in ClrKeywords)
            {
                ParsedClrKeywords |= (long)(ClrTraceEventParser.Keywords)Enum.Parse(typeof(ClrTraceEventParser.Keywords), keyword);
            }
            foreach (var keyword in KernelKeywords)
            {
                ParsedKernelKeywords |= (KernelTraceEventParser.Keywords)Enum.Parse(typeof(KernelTraceEventParser.Keywords), keyword);
            }
        }

        // TODO LINQ-like filter, e.g. `e["Duration"] > 1000 && e.ProcessName == "notepad"`,
        //      potentially also with `select new { ... }` which is what we print

        // TODO Something that operates on event pairs, Event/Start and Event/Stop, and then
        //      allows queries on duration

        // TODO Filters with greater-than or less-than operators

        public Dictionary<string, Regex> ParsedFilters { get; } = new Dictionary<string, Regex>();
        public Regex ParsedRawFilter { get; private set; }
        public bool IsFileSession => !String.IsNullOrEmpty(File);
        public long ParsedClrKeywords { get; private set; } = 0;
        public KernelTraceEventParser.Keywords ParsedKernelKeywords { get; private set; } = KernelTraceEventParser.Keywords.None;
    }
}
