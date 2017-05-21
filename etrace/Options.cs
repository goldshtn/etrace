using CommandLine;
using CommandLine.Text;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Tracing;

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
            help.AddPostOptionsLine("  etrace --kernel Process,Thread,FileIO,FileIOInit --event FileIO/Create");
            help.AddPostOptionsLine("  etrace --file trace.etl --stats");
            help.AddPostOptionsLine("  etrace --clr GC --event GC/Start --field PID,TID,Reason[12],Type");
            help.AddPostOptionsLine("  etrace --kernel Process --event Process/Start --where ImageFileName=myapp");
            help.AddPostOptionsLine("  etrace --kernel Process --where ProcessId=4");
            help.AddPostOptionsLine("  etrace --kernel Process --where ProcessName=myProcessName");
            help.AddPostOptionsLine("  etrace --kernel Process --event Thread/Stop --where ThreadId=10272");
            help.AddPostOptionsLine("  etrace --kernel Process --event --where \"ThreadId=1999 && ProcessId=4\"");
            help.AddPostOptionsLine("  etrace --clr GC --event GC/Start --duration 60");
            help.AddPostOptionsLine("  etrace --other Microsoft-Windows-Win32k --event QueuePostMessage");
            help.AddPostOptionsLine("  etrace --list CLR,Kernel");

            return help.ToString();
        }

        public void PostParse()
        {
            foreach (var filter in Filters)
            {
                var parsedFilter = ParseFilter(filter);
                if (parsedFilter != null)
                    ParsedFilters.Add(parsedFilter);
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

        #region Filter Parsing 

        private Filter ParseFilter(string filter)
        {
            Filter result = null;

            if (filter.Contains(MultipleFilter.MULTIPLE_FILTER_SIGN))
            {
                var subFilters = SplitMultipleFilter(filter, MultipleFilter.MULTIPLE_FILTER_SIGN);
                MultipleFilter multiFilter = new MultipleFilter();

                foreach (var subFilter in subFilters)
                {
                    var paredFilter = ParseFilter(subFilter);
                    multiFilter.SubFilters.Add(paredFilter);
                }

                result = multiFilter;
            }
            else
            {
                if (filter.Contains(Filter.GREATER_OR_EQUAL_SIGN))
                    result = ParseAnyOfFilter(filter, Filter.GREATER_OR_EQUAL_SIGN);
                if (filter.Contains(Filter.LESS_OR_EQUAL_SIGN))
                    result = ParseAnyOfFilter(filter, Filter.LESS_OR_EQUAL_SIGN);
                if (filter.Contains(Filter.GREATER_SIGN))
                    result = ParseAnyOfFilter(filter, Filter.GREATER_SIGN);
                else if (filter.Contains(Filter.LESS_SIGN))
                    result = ParseAnyOfFilter(filter, Filter.LESS_SIGN);
                else if (filter.Contains(Filter.NOT_EQUAL_SIGN))
                    result = ParseAnyOfFilter(filter, Filter.NOT_EQUAL_SIGN);
                else
                    result = ParseAnyOfFilter(filter, Filter.EQUAL_SIGN);
            }

            return result;
        }

        private string[] SplitMultipleFilter(string filter, string splitby)
        {
            filter = filter.Replace(" ", "");
            string[] result = filter.Split(new string[] { splitby }, StringSplitOptions.RemoveEmptyEntries);

            if (result.Length == 1)
                throw new ArgumentException($"Invalid filter: {filter}");

            return result;
        }

        private string[] SplitBinaryFilter(string filter, string splitby)
        {
            filter = filter.Replace(" ", "");
            string[] result = filter.Split(new string[] { splitby }, StringSplitOptions.RemoveEmptyEntries);

            if (result.Length != 2)
                throw new ArgumentException($"Invalid filter: {filter}");

            return result;
        }

        private Filter ParseAnyOfFilter(string filter, string @operator)
        {
            var splited = SplitBinaryFilter(filter, @operator);
            return new Filter(splited[0], splited[1], @operator);
        }

        #endregion

        // TODO LINQ-like filter, e.g. `e["Duration"] > 1000 && e.ProcessName == "notepad"`,
        //      potentially also with `select new { ... }` which is what we print

        // TODO Something that operates on event pairs, Event/Start and Event/Stop, and then
        //      allows queries on duration

        public List<Filter> ParsedFilters { get; } = new List<Filter>();
        public Regex ParsedRawFilter { get; private set; }
        public bool IsFileSession => !String.IsNullOrEmpty(File);
        public long ParsedClrKeywords { get; private set; } = 0;
        public KernelTraceEventParser.Keywords ParsedKernelKeywords { get; private set; } = KernelTraceEventParser.Keywords.None;


        public class MultipleFilter : Filter
        {
            internal const string MULTIPLE_FILTER_SIGN = "&&";

            internal MultipleFilter() : base ()
            {
                SubFilters = new List<Filter>();
            }

            public List<Filter> SubFilters { get; set; }

            internal override bool IsMatch(TraceEvent e)
            {
                return SubFilters.All(s => s.IsMatch(e));
            }
        }

        public class Filter
        {
            #region Const Signs

            internal const string GREATER_OR_EQUAL_SIGN = ">=";
            internal const string GREATER_SIGN = ">";
            internal const string LESS_OR_EQUAL_SIGN = "<=";
            internal const string LESS_SIGN = "<";
            internal const string EQUAL_SIGN = "=";
            internal const string NOT_EQUAL_SIGN = "!=";
            internal const string DOUBLE_EQUAL_SIGN = "==";
            
            #endregion

            protected Filter()
            {

            }

            public Filter(string key, string value, string operatorChar)
            {
                Key = key;
                RawValue = value;
                OperatorChar = operatorChar;
                Value = Value = new Regex(value, RegexOptions.Compiled);
            }

            #region Propeties

            public string Key { get; private set; }
            public Regex Value { get; private set; }
            public string RawValue { get; private set; }
            public string OperatorChar { get; private set; }

            #endregion

            #region Match Methods

            /// <summary>
            ///  Checks whether given filter match event.
            /// </summary>
            /// <param name="e">event</param>
            /// <returns>true if filter matches the event</returns>
            internal virtual bool IsMatch(TraceEvent e)
            {
                return IsMatchAny(e) || IsPayloadMatch(e);
            }

            /// <summary>
            ///  Checks if given key ad value match filter ( by OperatorChar )
            /// </summary>
            /// <returns>true if matches the event</returns>
            private bool IsMatch(string key, string value)
            {
                bool result = false;

                if (string.Equals(this.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    switch (OperatorChar)
                    {
                        case GREATER_SIGN:
                            result = int.Parse(this.RawValue) < int.Parse(value);
                            break;
                        case LESS_SIGN:
                            result = int.Parse(this.RawValue) > int.Parse(value);
                            break;
                        case LESS_OR_EQUAL_SIGN:
                            result = int.Parse(this.RawValue) >= int.Parse(value);
                            break;
                        case GREATER_OR_EQUAL_SIGN:
                            result = int.Parse(this.RawValue) <= int.Parse(value);
                            break;
                        case NOT_EQUAL_SIGN:
                            result = int.Parse(this.RawValue) != int.Parse(value);
                            break;
                        case EQUAL_SIGN:
                        case DOUBLE_EQUAL_SIGN:
                            result = string.Equals(this.RawValue, value, StringComparison.OrdinalIgnoreCase);
                            break;
                        default: break;
                    }
                }

                return result;
            }

            private bool IsPayloadMatch(TraceEvent e)
            {
                bool result = false;

                object payloadValue = e.PayloadByName(Key);

                if (payloadValue != null)
                {
                    result = Value.IsMatch(payloadValue.ToString());
                }

                return result;
            }

            /// <summary>
            /// Checks whether given filter match event.
            /// Suported filters : ProcessID, ThreadID, ProcessName
            /// </summary>
            /// <returns>true if filter matches the event</returns>
            private bool IsMatchAny(TraceEvent e)
            {
                return IsMatch(nameof(e.ProcessID), e.ProcessID.ToString())
                    || IsMatch(nameof(e.ThreadID), e.ThreadID.ToString())
                    || IsMatch(nameof(e.ProcessName), e.ProcessName);
            }

            #endregion

        }
    }
}