using System;
using System.Text;
using System.Reflection;
using System.Collections;
using System.IO;
using System.Linq;

namespace FarNet.ACD
{
    public static class Utility
    {
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
    }
}
