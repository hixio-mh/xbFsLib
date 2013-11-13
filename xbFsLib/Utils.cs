/*
    xbFsLib
    Copyright (C) 2013  Brandon Francis

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace xbFsLib
{

    /// <summary>
    /// Contains some static methods that are used throughout the library.
    /// </summary>
    class Utils
    {

        /// <summary>
        /// Adds a byte array to the end of this byte array.
        /// </summary>
        /// <param name="buffer0">The starting buffer.</param>
        /// <param name="buffer">The buffer to append.</param>
        /// <returns>The appended data as a byte array.</returns>
        public static byte[] AppendBytes(byte[] buffer0, byte[] buffer)
        {
            byte[] rtn = new byte[buffer0.Length + buffer.Length];
            Buffer.BlockCopy(buffer0, 0, rtn, 0, buffer0.Length);
            Buffer.BlockCopy(buffer, 0, rtn, buffer0.Length, buffer.Length);
            return rtn;
        }

        /// <summary>
        /// Returns a string representing a file size.
        /// </summary>
        /// <param name="size">The size to convert.</param>
        /// <returns>The file size as a string.</returns>
        public static string SizeToString(long size)
        {
            return SizeToString((ulong)size);
        }

        /// <summary>
        /// Returns a string representing a file size.
        /// </summary>
        /// <param name="size">The size to convert.</param>
        /// <returns>The file size as a string.</returns>
        public static string SizeToString(ulong size)
        {
            // This is our format
            string[] format = new[] { "{0} bytes", "{0} KB", 
                "{0} MB", "{0} GB", "{0} TB", "{0} PB", "{0} EB" };

            // Now lets figure out the size
            double s = size; int x = 0;
            for (; x < format.Length && s >= 1024; x++)
                s = (100 * s / 1024) / 100.0;

            // Return our size
            return string.Format(format[x], s.ToString("###,###,##0.##"));
        }

        /// <summary>
        /// Converts this byte array into a string of hex characters.
        /// </summary>
        /// <param name="buffer">The byte array to convert.</param>
        /// <returns>A string of hex from the byte array.</returns>
        public static string ToHexString(byte[] buffer)
        {
            string hexStr = "";
            for (int x = 0; x < buffer.Length; x++)
            {
                hexStr += buffer[x].ToString("X2");
            }
            return hexStr;
        }

        /// <summary>
        /// Gets the extension of a filename (without the period).
        /// </summary>
        /// <param name="filename">The filename to use.</param>
        /// <returns>The extension, sans dot.</returns>
        public static string GetFilenameExtension(string filename)
        {
            int periodIndex = filename.LastIndexOf(".");
            if (periodIndex < 0)
                return "";
            if (periodIndex >= filename.Length)
                return "";
            return filename.Substring(periodIndex + 1).ToLower();
        }

        /// <summary>
        /// Returns the file name from a full path.
        /// </summary>
        /// <param name="path">The full path.</param>
        /// <returns>The name of the file</returns>
        public static string GetNameFromPath(string path)
        {
            path = path.Replace("/", "\\");
            if (path.IndexOf("\\") < 0)
                return path;
            string[] parts = path.Split("\\".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            return parts[parts.Length - 1];
        }

        /// <summary>
        /// Changes the filename in a path or HTTP path and returns the new path.
        /// </summary>
        /// <param name="path">The path to change.</param>
        /// <param name="newFilename">The new filename to use.</param>
        /// <returns>A path as a string.</returns>
        public static string SetPathFilename(string path, string newFilename)
        {
            if (path.StartsWith("http://"))
            {
                string[] parts2 = path.Split("/".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                path = path.Substring(0, path.Length - parts2[parts2.Length - 1].Length) + newFilename;
                return path;
            }
            path = path.Replace("/", "\\");
            if (path.StartsWith("\\"))
                path = path.Substring(1, path.Length - 1);
            if (path.EndsWith("\\"))
                path = path.Substring(0, path.Length - 1);
            string[] parts = path.Split("\\".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            string rtn = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (i > 0)
                    rtn += "\\";
                rtn += parts[i];
            }
            rtn += newFilename;
            return rtn;
        }

    }

}
