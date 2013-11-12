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

namespace xbFsLib.FATX
{

    /// <summary>
    /// A special chained stream that specifically deals with FATX usb sticks.
    /// </summary>
    public class USBStream : IO.ChainedStream
    {
        private char _driveLetter;
        public char DriveLetter
        {
            get { return _driveLetter; }
        }

        public long DataStart
        {
            get { return streams[0].Length; }
        }

        private string deviceId = "";
        public string DeviceId
        {
            get { return deviceId; }
        }

        /// <summary>
        /// Initializes a new USBStream.
        /// </summary>
        /// <param name="DriveLetter">The drive letter of the device.</param>
        public USBStream(char DriveLetter)
        {
            // Set up some variables
            string baseDirectory = DriveLetter + ":\\Xbox360\\";

            // Check if the base directory exists
            if (!System.IO.Directory.Exists(baseDirectory))
                throw new Exception("Base directory not found.");

            // Check for Data0000
            if (!System.IO.File.Exists(baseDirectory + "Data0000"))
                throw new Exception("Data0000 not found.");

            // Check for Data0001
            if (!System.IO.File.Exists(baseDirectory + "Data0001"))
                throw new Exception("Data0001 not found.");

            // Get the device id from data 0
            IO.CoreIO io = new IO.CoreIO(baseDirectory + "Data0000", IO.EndianStyle.BigEndian);
            io.Position = 0x228;
            deviceId = Utils.ToHexString(io.Reader.ReadBytes(0x14)).ToUpper();
            io.Close();

            // Add all of the needed files in, starting with Data0001
            int i = 1;
            while (true)
            {
                string cur = string.Format("Data{0,-5:0000}", i);
                if (System.IO.File.Exists(baseDirectory + cur))
                {
                    AddFile(baseDirectory + cur);
                    i++;
                }
                else
                {
                    break;
                }
            }

            // Finish up
            this._driveLetter = DriveLetter;

        }

    }
}
