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

namespace xbFsLib.IO
{

    /// <summary>
    /// A stream that chains multiple files together into one seamless virtual stream.
    /// </summary>
    public class ChainedStream : System.IO.Stream
    {
        protected List<CoreIO> streams;
        private int _currentStream = 0;
        private long _position = 0;
        private System.Windows.Forms.Timer flushTimer = new System.Windows.Forms.Timer()
        {
            Interval = 300
        };

        public ChainedStream()
        {
            streams = new List<CoreIO>();
            flushTimer.Tick += flushTimer_Tick;
        }

        void flushTimer_Tick(object sender, EventArgs e)
        {
            flushTimer.Stop();
            Flush();
        }

        /// <summary>
        /// Adds a file into the chain of files.
        /// </summary>
        /// <param name="filename">The filename of the file to add.</param>
        public void AddFile(string filename)
        {
            // Check to see if the file exists
            if (!System.IO.File.Exists(filename))
                throw new System.IO.FileNotFoundException();

            // Add the new stream
            streams.Add(new CoreIO(filename, EndianStyle.BigEndian));

            // Set the initial position
            if (streams.Count == 1)
                Position = 0;

        }

        /// <summary>
        /// Closes the stream and all underlying chained streams.
        /// </summary>
        public override void Close()
        {
            for (int i = 0; i < streams.Count; i++)
                streams[i].Close();
            base.Close();
        }

        /// <summary>
        /// Whether or not the stream can be read from.
        /// </summary>
        public override bool CanRead
        {
            get
            {
                if (streams.Count == 0)
                    return false;
                for (int i = 0; i < streams.Count; i++)
                    if (streams[i].Stream.CanRead == false) { return false; }
                return true;
            }
        }

        /// <summary>
        /// Whether or not the stream can be seeked.
        /// </summary>
        public override bool CanSeek
        {
            get
            {
                if (streams.Count == 0)
                    return false;
                for (int i = 0; i < streams.Count; i++)
                    if (streams[i].Stream.CanSeek == false) { return false; }
                return true;
            }
        }

        /// <summary>
        /// Whether or not the stream can be written to.
        /// </summary>
        public override bool CanWrite
        {
            get
            {
                if (streams.Count == 0)
                    return false;
                for (int i = 0; i < streams.Count; i++)
                    if (streams[i].Stream.CanWrite == false) { return false; }
                return true;
            }
        }

        /// <summary>
        /// Flushes all of the underlying streams.
        /// </summary>
        public override void Flush()
        {
            bool flag = false;
            for (int i = 0; i < streams.Count; i++)
            {
                try
                {
                    streams[i].Stream.Flush();

                    // Try to manually force a write if possible, useful if writing to devices like USB drives
                    System.IO.FileStream fs = (System.IO.FileStream)streams[i].Stream;
                    Windows.Imports.FlushFileBuffers(fs.SafeFileHandle.DangerousGetHandle());
                }
                catch { flag = true; }
                if (flag)
                    System.Windows.Forms.MessageBox.Show("You pulled out your device a little quick, some changes might not " +
                        " have saved completely. Just wait a second longer before pulling your device out next time.", "Warning");
            }
        }

        /// <summary>
        /// The length of every file length added together.
        /// </summary>
        public override long Length
        {
            get
            {
                long sum = 0;
                for (int i = 0; i < streams.Count; i++)
                    sum += streams[i].Length;
                return sum;
            }
        }

        /// <summary>
        /// The current absolute position of the pointer in the stream.
        /// </summary>
        public override long Position
        {
            get
            {
                return _position;
            }
            set
            {
                if (value < 0 || value > Length)
                    throw new ArgumentOutOfRangeException();
                _position = value;
                _currentStream = GetStreamIndex(value);
                streams[_currentStream].Position = value - GetStartOffset(_currentStream);
            }
        }

        /// <summary>
        /// Reads a buffer from the underlying stream format.
        /// </summary>
        /// <param name="count">The amount of bytes to read.</param>
        /// <returns>The bytes read as a byte array.</returns>
        private byte[] DoRead(int count)
        {
            byte[] rtn = new byte[] { };
            int amountRead = 0;
            while (amountRead < count)
            {

                // Calculate the amount to read in the current stream
                int difference = (int)(streams[_currentStream].Length - streams[_currentStream].Position);
                int amountToRead = Math.Min(count - amountRead, difference);

                // Read the amount calculated from the current stream
                rtn = Utils.AppendBytes(rtn, streams[_currentStream].Reader.ReadBytes(amountToRead));

                // Increment the amount read and the position
                amountRead += amountToRead;
                Position += amountToRead;

            }
            return rtn;
        }

        /// <summary>
        /// Reads a buffer from the stream.
        /// </summary>
        /// <param name="buffer">The buffer to read to.</param>
        /// <param name="offset">The offset in the buffer to begin at.</param>
        /// <param name="count">The amount of bytes to read.</param>
        /// <returns>The amount of bytes read as an int.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            byte[] temp = DoRead(count);
            Array.Copy(temp, 0, buffer, offset, count);
            return temp.Length;
        }

        /// <summary>
        /// Seeks to a specific offset in the stream.
        /// </summary>
        /// <param name="offset">The offset to seek to.</param>
        /// <param name="origin">The origin to reference the offset from.</param>
        /// <returns>The new absolute position as a long.</returns>
        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            switch (origin)
            {
                case System.IO.SeekOrigin.Begin:
                    Position = offset;
                    break;
                case System.IO.SeekOrigin.Current:
                    Position += offset;
                    break;
                case System.IO.SeekOrigin.End:
                    Position = Length - offset;
                    break;
            }
            return Position;
        }

        /// <summary>
        /// Since there are multiple files being referenced, setting the length is impossible.
        /// </summary>
        /// <param name="value">The new length to use.</param>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Writes a buffer to the underlying stream format.
        /// </summary>
        /// <param name="buffer">The buffer to write.</param>
        private void DoWrite(byte[] buffer)
        {
            int count = buffer.Length;
            int amountWritten = 0;
            while (amountWritten < count)
            {
                // Calculate the amount to write in the current stream
                int difference = (int)(streams[_currentStream].Length - streams[_currentStream].Position);
                int amountToWrite = Math.Min(count - amountWritten, difference);

                // Create a temp buffer and write it to the current stream
                byte[] temp = new byte[amountToWrite];
                Buffer.BlockCopy(buffer, amountWritten, temp, 0, amountToWrite);
                streams[_currentStream].Writer.Write(temp);

                // Increment the amount written and the position
                amountWritten += amountToWrite;
                Position += amountToWrite;
            }
        }

        /// <summary>
        /// Writes a buffer to the stream.
        /// </summary>
        /// <param name="buffer">The buffer to write.</param>
        /// <param name="offset">The offset of the buffer to start at.</param>
        /// <param name="count">The amount of bytes in the buffer to write.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            byte[] temp = new byte[count];
            Buffer.BlockCopy(buffer, offset, temp, 0, count);
            DoWrite(temp);
            flushTimer.Start();
        }

        /// <summary>
        /// Gets the starting offset for the beginning of a stream.
        /// </summary>
        /// <param name="index">The index of the stream.</param>
        /// <returns>The starting offset as a long.</returns>
        private long GetStartOffset(int index)
        {
            long sum = 0;
            for (int i = 0; i < index; i++)
                sum += streams[i].Length;
            return sum;
        }

        /// <summary>
        /// Gets the current stream index according to an offset.
        /// </summary>
        /// <param name="offset">The offset to use.</param>
        /// <returns>The index of the stream as an int.</returns>
        private int GetStreamIndex(long offset)
        {
            if (offset == 0) { return 0; }
            if (offset == Length) { return streams.Count - 1; }
            long sum = 0;
            for (int i = 0; i < streams.Count; i++)
            {
                if (offset >= sum && offset < (sum + streams[i].Length))
                    return i;
                sum += streams[i].Length;
            }
            return -1;
        }

    }
}
