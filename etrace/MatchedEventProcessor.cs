using ConsoleTables.Core;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace etrace
{
    interface IMatchedEventProcessor : IDisposable
    {
        void TakeEvent(TraceEvent e);
        void TakeEvent(TraceEvent e, string description);
    }

    class EveryEventTablePrinter : IMatchedEventProcessor
    {
        private Regex fieldWithWidth = new Regex("(.*)\\[(\\d+)\\]");
        private Table table = new Table();

        public EveryEventTablePrinter(IEnumerable<string> fieldsToPrint)
        {
            foreach (var field in fieldsToPrint)
            {
                Match match = fieldWithWidth.Match(field);
                if (match.Success)
                {
                    table.AddColumn(match.Groups[1].Value, int.Parse(match.Groups[2].Value));
                }
                else
                {
                    table.AddColumn(field, Extensions.GetExpectedFieldWidth(field));
                }
            }
            table.PrintHeader();
        }

        public void TakeEvent(TraceEvent e)
        {
            var values = table.Columns.Select(c => e.GetFieldByName(c.Name));
            table.PrintRow(values.ToArray());
        }

        public void TakeEvent(TraceEvent e, string description)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }
    }

    class EveryEventPrinter : IMatchedEventProcessor
    {
        public void TakeEvent(TraceEvent e)
        {
            TakeEvent(e, e.AsRawString());
        }

        public void TakeEvent(TraceEvent e, string description)
        {
            Console.WriteLine(description);
        }

        public void Dispose()
        {
        }
    }

    class CountingDictionary
    {
        public void Add(string key, ulong value = 1)
        {
            ulong current;
            if (Counts.TryGetValue(key, out current))
            {
                Counts[key] = current + value;
            }
            else
            {
                Counts.Add(key, value);
            }
        }

        public void Print(string header, string key)
        {
            var table = new ConsoleTable(key, "Count");
            Console.WriteLine(header);
            foreach (var item in Counts.OrderByDescending(pair => pair.Value))
            {
                table.AddRow(item.Key, item.Value);
            }
            table.Write();
            Console.WriteLine();
        }

        public IDictionary<string, ulong> Counts { get; } = new Dictionary<string, ulong>();
    }
    
    class EventStatisticsAggregator : IMatchedEventProcessor
    {
        private CountingDictionary countByEventName = new CountingDictionary();
        private CountingDictionary countByProcess = new CountingDictionary();
        private bool disposed = false;

        public void TakeEvent(TraceEvent e)
        {
            countByEventName.Add(e.EventName);
            countByProcess.Add(e.ProcessName);
        }

        public void TakeEvent(TraceEvent e, string description)
        {
            TakeEvent(e);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            countByEventName.Print("Events by name", "Event");
            countByProcess.Print("Events by process", "Process");
        }
    }
}
