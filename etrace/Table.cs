using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace etrace
{
    class Table
    {
        public class Column
        {
            public string Name { get; set; }
            public int Width { get; set; }
        }

        public int MaxWidth { get; set; } = Console.BufferWidth;
        public List<Column> Columns { get; } = new List<Column>();

        private int usedWidth = 0;

        public void AddColumn(string name, int width)
        {
            if (usedWidth + width + 1 > MaxWidth)
                return; // Do not show this column

            usedWidth += width + 1;
            Columns.Add(new Column { Name = name, Width = width });
        }

        public void PrintHeader()
        {
            foreach (var column in Columns)
            {
                Console.Write("{0,-" + column.Width + "} ", column.Name.Truncate(column.Width));
            }
            Console.WriteLine();
            Console.WriteLine(new string('-', usedWidth));
        }

        public void PrintRow(IEnumerable<object> values)
        {
            int index = 0;
            foreach (object value in values)
            {
                if (index >= Columns.Count)
                    return; // Discard extraneous data

                var column = Columns[index];

                // The last column gets all the remaining space.
                if (index == Columns.Count - 1)
                    Console.Write(value.ToString().Truncate(MaxWidth - usedWidth));
                else
                    Console.Write("{0,-" + column.Width + "} ",
                                  value.ToString().Truncate(column.Width));

                ++index;
            }
            Console.WriteLine();
        }
    }
}
