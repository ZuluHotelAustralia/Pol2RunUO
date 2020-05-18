using System;
using System.Collections.Generic;
using System.IO;

namespace Pol2RunUO.Readers
{
    internal static class PolDataFileReader
    {
        public static List<Dictionary<string, string>> ReadDataFile(TextReader reader)
        {
            var elements = new List<Dictionary<string, string>>();

            Dictionary<string, string> element = null;
            string line;
            string prev = null;
            while((line = reader.ReadLine()) != null)
            {
                line = line.TrimStart();
                switch (line.Trim())
                {
                    // Open brace, parse the previous line for the entry type/id
                    case "{":
                        element = new Dictionary<string, string>();
                        var splits = (prev ?? "Unknown").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        element.Add("DataElementType", splits.Length > 0 ? splits[0] : prev);
                        element.Add("DataElementId", splits.Length > 1 ? splits[1] : null);
                        continue;
                    // Ending brace, finish parsing and reset for next element
                    case "}":
                        elements.Add(element);
                        element = null;
                        continue;
                }
                
                // Skip lines until we encounter an opening brace
                if (element == null || string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                {
                    prev = line;
                    continue;
                }

                string property;
                string value;

                // CProps have 3 fields to read, we merge the CProp name into the property name.
                // This is to keep the resulting collection flat; We could make a concrete type to make this better.
                if (line.Contains("CProp", StringComparison.InvariantCultureIgnoreCase))
                {
                    var values = ReadPropsByWhitespace(line, 3);
                    property = "CProp_" + values[1];
                    value = values[2];
                }
                // Regular top-level property with just a key: value
                else
                {
                    var values = ReadPropsByWhitespace(line);
                    property = values[0];
                    value = values[1];
                }

                element.TryAdd(property.Trim(), value.Trim());
            }
 
            return elements;
        }

        private static string[] ReadPropsByWhitespace(string s, int max = 2)
        {
            static bool IsWhitespace(int c) => char.IsWhiteSpace((char) c) || c == -1;
            
            using TextReader reader = new StringReader(s);
            var values = new string[3];

            // Skip whitespace until we hit a character, read that until we see whitespace again
            // If it's the last element, read until the end of the line.
            for (int i = 0; i < max - 1; i++)
            {
                string buffer = string.Empty;
                int c;
                while (IsWhitespace(c = reader.Peek()) && c != -1)
                {
                    reader.Read();
                }
                
                while (!IsWhitespace(c = reader.Peek()) && c != -1)
                {
                    buffer += (char)reader.Read();
                }

                values[i] = buffer;
            }

            values[max - 1] = reader.ReadToEnd();

            return values;
        }

        private static bool ReadRawInt(TextReader reader, out int result)
        {
            char c;
            string buffer = string.Empty;
            while(char.IsDigit((char)reader.Peek()) && (c = (char)reader.Read()) != -1)
            {
                buffer += c;
            }

            return int.TryParse(buffer, out result);
        }
        

        public static object UnpackElement(TextReader reader)
        {
            char type = (char) reader.Read();

            switch (type)
            {
                // String with no length, e.g. "sHuman"
                case 's': 
                {
                    return reader.ReadLine();
                }
                // String with length, e.g. "S4:bird"
                case 'S': 
                {
                    ReadRawInt(reader, out int length);
                    reader.Read(); // ':'

                    string buffer = string.Empty;
                    for (int i = 0; i < length; i++)
                    {
                        buffer += (char)reader.Read();
                    }

                    return buffer;
                }
                
                // Array e.g. "a5:S7:corpserS8:direwolfS5:harpyS6:reaperS8:skeleton"
                case 'a':
                {
                    ReadRawInt(reader, out int length);
                    reader.Read(); // ':'

                    var values = new object[length];
                    for (int i = 0; i < length; i++)
                    {
                        values[i] = UnpackElement(reader);
                    }

                    return values;
                }

                // Integer .e.g "i47571333"
                case 'i':
                {
                    ReadRawInt(reader, out int length);
                    return length;
                }

                // Dictionary e.g. "d3:S4:blahS4:testS3:heyi57S3:youi73"
                case 'd': // Dictionary
                {
                    ReadRawInt(reader, out int length);
                    reader.Read(); // ':'

                    var values = new Dictionary<string, object>();
                    for (int i = 0; i < length; i++)
                    {
                        values.Add((string)UnpackElement(reader), UnpackElement(reader));
                    }

                    return values;
                }
            }

            return null;
        }
    }
}