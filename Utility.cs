using System;
using System.Text;
using System.Reflection;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace FarNet.ACD
{
    public static class Utility
    {
        internal static DateTime _1900101 = new DateTime(1970, 1, 1);

        public static string BytesToString(long byteCount)
        {
            string Result;
            long bytes = Math.Abs(byteCount);

            if (bytes < 100 * 1024)
            {
                // Result = FormatFloat(L"#,##0 \"B\"", byteCount);
                Result = string.Format("{0} B", bytes);
            }
            else if (bytes < 100 * 1024 * 1024)
            {
                // Result = FormatFloat(L"#,##0 \"KB\"", byteCount / 1024);
                Result = string.Format("{0} KiB", Math.Round(bytes / 1024.0, 0));
            }
            else
            {
                // Result = FormatFloat(L"#,##0 \"MiB\"", byteCount / (1024*1024));
                Result = string.Format("{0} MiB", Math.Round(bytes / (1024 * 1024.0), 0));
            }
            return Result;
        }

        public static string BPSToString(long bps)
        {
            string Result;
            long bpsRate = Math.Abs(bps);

            if (bpsRate < 5 * 1000)
            {
                Result = string.Format("{0} bit/s", bpsRate);
            }
            else if (bpsRate < 5 * 1000 * 1000)
            {
                Result = string.Format("{0} kbit/s", Math.Round(bpsRate / 1000.0, 0));
            }
            else
            {
                Result = string.Format("{0} Mbit/s", Math.Round(bpsRate / (1000 * 1000.0), 0));
            }
            return Result;
        }

        public static string var_dump(object obj, int recursion)
        {
            StringBuilder result = new StringBuilder();

            // Protect the method against endless recursion
            if (recursion < 5)
            {
                // Determine object type
                Type t = obj.GetType();

                // Get array with properties for this object
                PropertyInfo[] properties = t.GetProperties();

                foreach (PropertyInfo property in properties)
                {
                    try
                    {
                        // Get the property value
                        object value = property.GetValue(obj, null);

                        // Create indenting string to put in front of properties of a deeper level
                        // We'll need this when we display the property name and value
                        string indent = String.Empty;
                        string spaces = "|   ";
                        string trail = "|...";

                        if (recursion > 0)
                        {
                            indent = new StringBuilder(trail).Insert(0, spaces, recursion - 1).ToString();
                        }

                        if (value != null)
                        {
                            // If the value is a string, add quotation marks
                            string displayValue = value.ToString();
                            if (value is string) displayValue = String.Concat('"', displayValue, '"');

                            // Add property name and value to return string
                            result.AppendFormat("{0}{1} = {2}\n", indent, property.Name, displayValue);

                            try
                            {
                                if (!(value is ICollection))
                                {
                                    // Call var_dump() again to list child properties
                                    // This throws an exception if the current property value
                                    // is of an unsupported type (eg. it has not properties)
                                    result.Append(var_dump(value, recursion + 1));
                                }
                                else
                                {
                                    // 2009-07-29: added support for collections
                                    // The value is a collection (eg. it's an arraylist or generic list)
                                    // so loop through its elements and dump their properties
                                    int elementCount = 0;
                                    foreach (object element in ((ICollection)value))
                                    {
                                        string elementName = String.Format("{0}[{1}]", property.Name, elementCount);
                                        indent = new StringBuilder(trail).Insert(0, spaces, recursion).ToString();

                                        // Display the collection element name and type
                                        result.AppendFormat("{0}{1} = {2}\n", indent, elementName, element.ToString());

                                        // Display the child properties
                                        result.Append(var_dump(element, recursion + 2));
                                        elementCount++;
                                    }

                                    result.Append(var_dump(value, recursion + 1));
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            // Add empty (null) property to return string
                            result.AppendFormat("{0}{1} = {2}\n", indent, property.Name, "null");
                        }
                    }
                    catch
                    {
                        // Some properties will throw an exception on property.GetValue()
                        // I don't know exactly why this happens, so for now i will ignore them...
                    }
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Checks the given string for being (apparently) a valid filename
        /// </summary>
        /// <param name="testName">Filename to test</param>
        /// <returns></returns>
        public static bool IsValidFilename(string testName)
        {
            return !string.IsNullOrEmpty(testName) && testName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        /// <summary>
        /// Checks the given string for being (apparently) a valid pathname
        /// </summary>
        /// <param name="testName">Pathname to test</param>
        /// <returns></returns>
        public static bool IsValidPathname(string testName)
        {
            return !string.IsNullOrEmpty(testName) && testName.IndexOfAny(Path.GetInvalidPathChars()) < 0;
        }

        /// <summary>
        /// Generates random string
        /// Source: http://stackoverflow.com/questions/1344221/how-can-i-generate-random-alphanumeric-strings-in-c
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public static string RandomString(int length, string chars = "abcdefghijklmnopqrstuvwxyz0123456789")
        {
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        /// <summary>
        /// Recursively searches for files in a given directory
        /// http://stackoverflow.com/a/929418/2102087
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static IEnumerable<string> GetFiles(string path)
        {
            Queue<string> queue = new Queue<string>();
            queue.Enqueue(path);
            while (queue.Count > 0)
            {
                path = queue.Dequeue();
                yield return path;
                try
                {
                    foreach (string subDir in Directory.GetDirectories(path))
                    {
                        queue.Enqueue(subDir);
                    }
                }
                catch (Exception)
                {
                    //Console.Error.WriteLine(ex);
                }
                string[] files = null;
                try
                {
                    files = Directory.GetFiles(path);
                }
                catch (Exception)
                {
                    //Console.Error.WriteLine(ex);
                }
                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        yield return files[i];
                    }
                }
            }
        }

        /// <summary>
        /// Shortens string putting placeholder in the middle of the string
        /// </summary>
        /// <param name="str"></param>
        /// <param name="sideLen"></param>
        /// <param name="placeholder"></param>
        /// <returns></returns>
        public static string ShortenString(string str, ushort sideLen, string placeholder = "...")
        {
            if (str.Length <= sideLen * 2)
            {
                return str;
            }
            var start = str.Substring(0, sideLen);
            var end = str.Substring(str.Length - sideLen, sideLen);

            return start + placeholder + end;
        }

        /// <summary>
        /// Wraps line to a given maxLength.
        /// If the line is longer, inserts System.Environment.NewLine between the closest words.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="maxLength"></param>
        /// <returns></returns>
        public static string WrapLineToString(string line, int maxLength = 80)
        {
            return string.Join(Environment.NewLine, WrapLineToList(line, maxLength));
        }

        public static List<string> WrapLineToList(string line, int maxLength = 80)
        {
            var words = line.Split(' ');
            var lines = words.Skip(1).Aggregate(words.Take(1).ToList(), (l, w) =>
            {
                if (l.Last().Length + w.Length >= maxLength)
                    l.Add(w);
                else
                    l[l.Count - 1] += " " + w;
                return l;
            });
            return lines;
        }

        /// <summary>
        /// Ensures that the text width is not longer than maxLength
        /// </summary>
        /// <param name="text"></param>
        /// <param name="maxLength"></param>
        /// <returns></returns>
        public static string WrapText(string text, int maxLength = 80)
        {
            var lines = StringToList(text);
            var result = WrapText(lines, maxLength);

            return string.Join(Environment.NewLine, result);
        }

        /// <summary>
        /// Ensures that the text width is not longer than maxLength
        /// </summary>
        /// <param name="lines"></param>
        /// <param name="maxLength"></param>
        /// <param name="maxHeight"></param>
        /// <returns></returns>
        public static List<string> WrapText(List<string> lines, int maxLength = 80, int maxHeight = 24)
        {
            List<string> _lines = new List<string>();
            foreach (var line in lines)
            {
                _lines.AddRange(WrapLineToList(line, maxLength));
            }

            var result = _lines.Take(maxHeight);

            if (result.ElementAt(result.Count() - 1) == string.Empty) {
                result = result.Take(result.Count() - 2);
            }

            return result.ToList();
        }

        /// <summary>
        /// Gets the size of the longest line in the text
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static int GetTextWidth(string text)
        {
            List<string> lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
            return GetTextWidth(lines);
        }

        /// <summary>
        /// Gets the size of the longest line in the text
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static int GetTextWidth(List<string> lines)
        {
            return lines.OrderByDescending(s => s.Length).First().Length;
        }

        /// <summary>
        /// Gets the list of strings from the given string by splitting it to lines
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static List<string> StringToList(string text)
        {
            return text.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
        }

        /// <summary>
        /// Gets Unix timestamp
        /// </summary>
        /// <returns></returns>
        public static int GetUnixTimestamp()
        {
            return (int)(DateTime.UtcNow.Subtract(_1900101)).TotalSeconds;
        }
    }
}
