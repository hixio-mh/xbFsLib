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
using System.Security.Cryptography;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace xbFsLib.FATX
{

    /// <summary>
    /// Utilities used by the FATX classes.
    /// </summary>
    public class Utilities
    {

        /// <summary>
        /// Pulls out an Int24 value from a byte array.
        /// </summary>
        /// <param name="data">The byte array to pull from.</param>
        /// <param name="index">The index to start at.</param>
        /// <param name="bigEndian">Whether or not to use big endian.</param>
        /// <returns>Returns the int24 value as an int32.</returns>
        internal static int GetInt24(byte[] data, int index, bool bigEndian)
        {
            if (!bigEndian)
                return (data[index + 2] << 16) | (data[index + 1] << 8) | data[index];

            return (data[index] << 16) | (data[index + 1] << 8) | data[index + 2];
        }

        /// <summary>
        /// Sets an Int24 value.
        /// </summary>
        /// <param name="value">The value to set.</param>
        /// <param name="bigEndian">Whether or not to use big endian.</param>
        /// <returns>Returns the byte array representing the value in int24.</returns>
        internal static byte[] SetInt24(int value, bool bigEndian)
        {
            if (value > 0x00FFFFFF)
                throw new Exception("Value to large");

            if (!bigEndian)
                return new[] { 
                    (byte)(value & 0xFF), 
                    (byte)((value >> 8) & 0xFF), 
                    (byte)((value >> 16) & 0xFF)};

            return new[] { 
                    (byte)((value >> 16) & 0xFF), 
                    (byte)((value >> 8) & 0xFF), 
                    (byte)(value & 0xFF)};
        }

        /// <summary>
        /// Pulls an int32 value out of a byte array.
        /// </summary>
        /// <param name="data">The byte array to pull from.</param>
        /// <param name="bigEndian">Whether or not to use big endian.</param>
        /// <returns>Returns the int pulled from the data.</returns>
        internal static int GetInt32(byte[] data, bool bigEndian)
        {
            if (bigEndian)
                return (data[0] << 24) | (data[1] << 16) |
                     (data[2] << 8) | data[3];

            return (data[3] << 24) | (data[2] << 16) |
               (data[1] << 8) | data[0];
        }

        /// <summary>
        /// Gets the byte representation of an int32 value.
        /// </summary>
        /// <param name="value">The value to get the byte representation for.</param>
        /// <param name="bigEndian">Whether or not to use big endian.</param>
        /// <returns>Returns the int32 as a byte array.</returns>
        internal static byte[] SetInt32(int value, bool bigEndian)
        {
            if (bigEndian)
                return new[] {
                    (byte)((value >> 24) & 0xFF), 
                    (byte)((value >> 16) & 0xFF), 
                    (byte)((value >> 8) & 0xFF),
                    (byte)(value & 0xFF)};

            return new[] { 
                (byte)(value  & 0xFF), 
                (byte)((value >> 8) & 0xFF), 
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 24) & 0xFF)};
        }

        /// <summary>
        /// Gets a DateTime from the FATX date given.
        /// </summary>
        /// <param name="dateTime">The FATX date.</param>
        /// <returns>Returns a DateTime.</returns>
        internal static DateTime GetDateTime(byte[] dateTime)
        {
            return GetDateTime(dateTime, true);
        }

        /// <summary>
        /// Gets a DateTime from the FATX date given.
        /// </summary>
        /// <param name="dateTime">The FATX date time.</param>
        /// <param name="bigEndian">Whether or not to use big endian.</param>
        /// <returns>Returns a DateTime.</returns>
        internal static DateTime GetDateTime(byte[] dateTime, bool bigEndian)
        {
            return GetDateTime(GetInt32(dateTime, bigEndian));
        }

        /// <summary>
        /// Gets a date time from a timestamp int.
        /// </summary>
        /// <param name="dateTime">The int timestamp.</param>
        /// <returns>Returns a DateTime.</returns>
        internal static DateTime GetDateTime(int dateTime)
        {
            // Check our datetime first
            if (dateTime == 0)
                return DateTime.MinValue;

            int second = (dateTime & 0x1F) << 1;
            int minute = (dateTime >> 5) & 0x3F;
            int hour = (dateTime >> 11) & 0x1F;
            int day = (dateTime >> 16) & 0x1F;
            int month = (dateTime >> 21) & 0x0F;
            int year = ((dateTime >> 25) & 0x7F) + 1980;

            try
            {
                return new DateTime(year, month, day, hour, minute, second).ToLocalTime();
            }
            catch { return DateTime.MinValue; }
        }

        /// <summary>
        /// Gets the FATX byte representation of a DateTime.
        /// </summary>
        /// <param name="value">The DateTime to use.</param>
        /// <returns>Returns a FATX byte array of the DateTime.</returns>
        internal static byte[] SetDateTime(DateTime value)
        {
            return SetDateTime(value, true);
        }

        /// <summary>
        /// Gets the FATX byte representation of a DateTime.
        /// </summary>
        /// <param name="value">The DateTime to use.</param>
        /// <param name="bigEndian">Whether or not to use big endian.</param>
        /// <returns>Returns the FATX byte array of the DateTime.</returns>
        internal static byte[] SetDateTime(DateTime value, bool bigEndian)
        {
            return SetInt32(SetDateTimeInt(value), bigEndian);
        }

        /// <summary>
        /// Gets the integer timestamp from a DateTime.
        /// </summary>
        /// <param name="dateTime">The DateTime to use.</param>
        /// <returns>The integer timestamp for the DateTime.</returns>
        internal static int SetDateTimeInt(DateTime dateTime)
        {
            // Get universal time                
            dateTime = dateTime.ToUniversalTime();

            int second = dateTime.Second;
            int minute = dateTime.Minute;
            int hour = dateTime.Hour;
            int day = dateTime.Day;
            int month = dateTime.Month;
            int year = dateTime.Year;

            year -= 1980; // Year can only be within 1980-2107
            second >>= 1; // Divide by 2

            // Combine into 1 int and return
            return (year << 25) | (month << 21) | (day << 16) |
                   (hour << 11) | (minute << 5) | second;
        }

        /// <summary>
        /// Rounds a value to a valid page number.
        /// </summary>
        /// <param name="tempSize">The size before rounding.</param>
        /// <returns>The new number that was rounded.</returns>
        internal static uint RoundToPages(uint tempSize)
        {
            if ((tempSize % 0x1000) == 0)
                return tempSize;
            return tempSize + (0x1000 - (tempSize % 0x1000));
        }

        /// <summary>
        /// Determines whether or not a filename is valid for the FATX
        /// filesystem.
        /// </summary>
        /// <param name="fileName">The filename to check.</param>
        /// <returns>True if valid, false otherwise.</returns>
        public static bool IsValidFATXFilename(string fileName)
        {
            if (fileName.Length == 0 ||
                fileName.Length > Dirent.MaxFileNameLength)
                return false;
            if (fileName.IndexOfAny(FATXDevice.InvalidFileNameChars) != -1)
                return false;
            return true;
        }

    }

}