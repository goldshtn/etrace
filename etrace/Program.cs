using CommandLine;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace etrace
{
    class Program
    {
        static private Options options = new Options();
        static private IMatchedEventProcessor eventProcessor;
        static private TraceEventSession session;
        static private ulong processedEvents = 0;
        static private ulong notFilteredEvents = 0;

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments(args, options);
            options.PostParse();

            if (options.List != ListFlags.None)
            {
                List();
                return;
            }

            // TODO Can try TraceLog support for realtime stacks as well
            // TODO One session for both kernel and CLR is not supported on Windows 7 and older

            CreateEventProcessor();

            var stopwatch = Stopwatch.StartNew();
            Console.WriteLine($"\nProcessing start time: {DateTime.Now}");
            using (eventProcessor)
            {
                if (options.IsFileSession)
                {
                    FileSession();
                }
                else
                {
                    RealTimeSession();
                }
            }

            // TODO Get this information printed even on Ctrl+C
            Console.WriteLine();
            Console.WriteLine("{0,-30} {1}", "Processing end time:", DateTime.Now);
            Console.WriteLine("{0,-30} {1}", "Processing duration:", stopwatch.Elapsed);
            Console.WriteLine("{0,-30} {1}", "Processed events:", processedEvents);
            Console.WriteLine("{0,-30} {1}", "Displayed events:", notFilteredEvents);
        }

        private static void CreateEventProcessor()
        {
            if (options.StatsOnly)
                eventProcessor = new EventStatisticsAggregator();
            else if (options.DisplayFields.Count > 0)
                eventProcessor = new EveryEventTablePrinter(options.DisplayFields);
            else
                eventProcessor = new EveryEventPrinter();
        }

        private static void FileSession()
        {
            if (options.ClrKeywords.Count > 0 || options.KernelKeywords.Count > 0 || options.OtherProviders.Count > 0)
                Bail("Specifying keywords and/or providers is not supported when parsing ETL files");

            using (var source = new ETWTraceEventSource(options.File))
            {
                ProcessTrace(source);
            }
        }

        private static void CloseSession()
        {
            if (eventProcessor != null)
            {
                eventProcessor.Dispose();
            }
            if (session != null)
            {
                Console.WriteLine($"Events lost: {session.EventsLost}");
                session.Dispose();
                session = null;
            }
        }

        private static void RealTimeSession()
        {
            if (options.ParsedClrKeywords == 0 &&
                options.ParsedKernelKeywords == KernelTraceEventParser.Keywords.None &&
                options.OtherProviders.Count == 0)
            {
                Bail("No events to collect");
            }

            Console.CancelKeyPress += (_, __) => CloseSession();

            if (options.DurationInSeconds > 0)
            {
                Task.Delay(TimeSpan.FromSeconds(options.DurationInSeconds))
                    .ContinueWith(_ => CloseSession());
            }

            using (session = new TraceEventSession("etrace-realtime-session"))
            {
                if (options.ParsedKernelKeywords != KernelTraceEventParser.Keywords.None)
                {
                    session.EnableKernelProvider(options.ParsedKernelKeywords);
                }
                if (options.ParsedClrKeywords != 0)
                {
                    session.EnableProvider(ClrTraceEventParser.ProviderGuid,
                                            matchAnyKeywords: (ulong)options.ParsedClrKeywords);
                }
                if (options.OtherProviders.Any())
                {
                    foreach (var provider in options.OtherProviders)
                    {
                        Guid guid;
                        if (Guid.TryParse(provider, out guid))
                        {
                            session.EnableProvider(Guid.Parse(provider));
                        }
                        else
                        {
                            guid = TraceEventProviders.GetProviderGuidByName(provider);
                            if (guid != Guid.Empty)
                                session.EnableProvider(guid);
                        }
                    }
                }

                ProcessTrace(session.Source);
            }
        }

        private static void ProcessTrace(TraceEventDispatcher dispatcher)
        {
            Console.WriteLine($"Session start time: {dispatcher.SessionStartTime}");

            dispatcher.Clr.All += ProcessEvent;
            dispatcher.Kernel.All += ProcessEvent;
            dispatcher.Dynamic.All += ProcessEvent;

            dispatcher.Process();
        }

        private static void List()
        {
            if ((options.List & ListFlags.CLR) != 0)
            {
                Console.WriteLine("\nSupported CLR keywords (use with --clr):\n");
                foreach (var keyword in Enum.GetNames(typeof(ClrTraceEventParser.Keywords)))
                {
                    Console.WriteLine($"\t{keyword}");
                }
            }
            if ((options.List & ListFlags.Kernel) != 0)
            {
                Console.WriteLine("\nSupported kernel keywords (use with --kernel):\n");
                foreach (var keyword in Enum.GetNames(typeof(KernelTraceEventParser.Keywords)))
                {
                    Console.WriteLine($"\t{keyword}");
                }
            }
            if ((options.List & ListFlags.Registered) != 0)
            {
                Console.WriteLine("\nRegistered or enabled providers (use with --other):\n");
                foreach (var provider in
                    TraceEventProviders.GetRegisteredOrEnabledProviders()
                                       .Select(guid => TraceEventProviders.GetProviderName(guid))
                                       .OrderBy(n => n))
                {
                    Console.WriteLine($"\t{provider}");
                }
            }
            if ((options.List & ListFlags.Published) != 0)
            {
                Console.WriteLine("\nPublished providers (use with --other):\n");
                foreach (var provider in
                    TraceEventProviders.GetPublishedProviders()
                                       .Select(guid => TraceEventProviders.GetProviderName(guid))
                                       .OrderBy(n => n))
                {
                    Console.WriteLine($"\t{provider}");
                }
            }
        }

        private static void Bail(string message)
        {
            Console.WriteLine("ERROR: " + message);
            Environment.Exit(1);
        }

        private static void ProcessEvent(TraceEvent e)
        {
            ++processedEvents;

            if (options.ProcessID != -1 && options.ProcessID != e.ProcessID)
                return;
            if (options.ThreadID != -1 && options.ThreadID != e.ThreadID)
                return;
            if (options.Events.Count > 0 && !options.Events.Contains(e.EventName))
                return;

            if (options.ParsedRawFilter != null)
            {
                string s = e.AsRawString();
                if (options.ParsedRawFilter.IsMatch(s))
                {
                    eventProcessor.TakeEvent(e, s);
                    ++notFilteredEvents;
                }
            }
            else if (options.ParsedFilters.Count > 0)
            {
                foreach (var filter in options.ParsedFilters)
                {
                    string payloadName = filter.Key;
                    Regex valueRegex = filter.Value;

                    object payloadValue = e.PayloadByName(payloadName);
                    if (payloadValue == null)
                        continue;

                    if (valueRegex.IsMatch(payloadValue.ToString()))
                    {
                        eventProcessor.TakeEvent(e);
                        ++notFilteredEvents;
                        break;
                    }
                }
            }
            else
            {
                eventProcessor.TakeEvent(e);
                ++notFilteredEvents;
            }
        }
    }
}
