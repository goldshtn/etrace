using CommandLine;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Diagnostics;
using System.Linq;
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
        static private Stopwatch sessionStartStopwatch;
        static private bool statsPrinted = false;

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

            sessionStartStopwatch = Stopwatch.StartNew();
            Console.WriteLine($"Processing start time: {DateTime.Now}");
            CreateEventProcessor();
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

            CloseSession();
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
            lock (typeof(Program))
            {
                int eventsLost = 0;
                if (eventProcessor != null)
                {
                    eventProcessor.Dispose();
                }
                if (session != null)
                {
                    eventsLost = session.EventsLost;
                    session.Dispose();
                    session = null;
                }

                if (!statsPrinted)
                {
                    Console.WriteLine();
                    Console.WriteLine("{0,-30} {1}", "Processing end time:", DateTime.Now);
                    Console.WriteLine("{0,-30} {1}", "Processing duration:", sessionStartStopwatch.Elapsed);
                    Console.WriteLine("{0,-30} {1}", "Processed events:", processedEvents);
                    Console.WriteLine("{0,-30} {1}", "Displayed events:", notFilteredEvents);
                    Console.WriteLine("{0,-30} {1}", "Events lost:", eventsLost);
                    statsPrinted = true;
                }
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
                    TakeEvent(e, s);
                }
            }
            else if (options.ParsedFilters.Count > 0)
            {
                foreach (var filter in options.ParsedFilters)
                {
                    if (filter.IsMatch(e))
                    {
                        TakeEvent(e);
                        break;
                    }
                }
            }
            else
            {
                TakeEvent(e);
            }
        }

        private static void TakeEvent(TraceEvent e, string description = null)
        {
            if (description != null)
            {
                eventProcessor.TakeEvent(e, description);
            }
            else
            {
                eventProcessor.TakeEvent(e);
            }

            ++notFilteredEvents;
        }
    }
}
