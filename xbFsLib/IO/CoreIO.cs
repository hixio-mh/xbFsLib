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
using System.IO;

namespace xbFsLib.IO
{

    /// <summary>
    /// An enum that describes the endian of the current CoreIO.
    /// </summary>
    public enum EndianStyle
    {
        LittleEndian,
        BigEndian
    }

    /// <summary>
    /// A feature full stream wrapper class.
    /// </summary>
    public sealed class CoreIO : IDisposable
    {

        private readonly bool _isTemp;
        private readonly bool _isFile;
        private readonly string _filePath = string.Empty;
        private readonly EndianStyle _endianStyle = EndianStyle.LittleEndian;

        /// <summary>
        /// An indication of whether the stream is open or not.
        /// </summary>
        public bool Opened { get; set; }

        /// <summary>
        /// The EndianReader that allows reading to the stream.
        /// </summary>
        public EndianReader Reader { get; set; }

        /// <summary>
        /// The EndianWriter that allows writing to the stream.
        /// </summary>
        public EndianWriter Writer { get; set; }

        /// <summary>
        /// The underlying stream.
        /// </summary>
        public Stream Stream { get; set; }

        /// <summary>
        /// The current position address of the stream.
        /// </summary>
        public long Position
        {
            get { return this.Stream.Position; }
            set { this.Stream.Position = value; }
        }

        /// <summary>
        /// The current length of the stream.
        /// </summary>
        public long Length
        {
            get
            {
                return this.Stream.Length;
            }
        }

        public CoreIO(string FilePath, EndianStyle EndianStyle, FileMode FileMode)
        {
            // Set our endian style, file path, filemode and let us know we're working with a file
            _endianStyle = EndianStyle;
            _filePath = FilePath;
            _isFile = true;
            _isTemp = false;
            this.Open(FileMode);
        }

        public CoreIO(string FilePath, EndianStyle EndianStyle)
        {
            // Set our endian style, file path and let us know we're working with a file
            _endianStyle = EndianStyle;
            _filePath = FilePath;
            _isFile = true;
            _isTemp = false;
            this.Open();
        }

        public CoreIO(Stream Stream, EndianStyle EndianStyle)
        {
            // Set our endian style and stream
            _endianStyle = EndianStyle;
            this.Stream = Stream;
            _isFile = false;
            _isTemp = false;
            this.Open();
        }

        public CoreIO(byte[] Buffer, EndianStyle EndianStyle)
        {
            // Set our endian style and create a temp filestream
            _endianStyle = EndianStyle;
            _filePath = System.IO.Path.GetTempFileName();
            File.WriteAllBytes(_filePath, Buffer);
            _isFile = true;
            _isTemp = true;
            this.Open(FileMode.Open);
        }

        public CoreIO()
        {
            // Set our endian style and create a temp filestream
            _endianStyle = EndianStyle.BigEndian;
            _filePath = System.IO.Path.GetTempFileName();
            _isFile = true;
            _isTemp = true;
            this.Open(FileMode.Create);
        }

        public CoreIO(EndianStyle EndianStyle)
        {
            // Set our endian style and create a temp filestream
            _endianStyle = EndianStyle;
            _filePath = System.IO.Path.GetTempFileName();
            _isFile = true;
            _isTemp = true;
            this.Open(FileMode.Create);
        }

        /// <summary>
        /// Open the underlying stream using the default Open filemode.
        /// </summary>
        public void Open()
        {
            this.Open(FileMode.Open);
        }

        /// <summary>
        /// Open the underlying stream.
        /// </summary>
        /// <param name="FileMode">The mode to open with.</param>
        public void Open(FileMode FileMode)
        {
            // If open lets return
            if (this.Opened)
                return;

            // If isFile create a new filestream
            if (_isFile)
                this.Stream = new FileStream(_filePath, FileMode, FileAccess.ReadWrite);

            // Set our reader and writer
            this.Reader = new EndianReader(this.Stream, _endianStyle);
            this.Writer = new EndianWriter(this.Stream, _endianStyle);

            // Let us know the stream is open
            this.Opened = true;
        }

        /// <summary>
        /// Close the underlying stream.
        /// </summary>
        public void Close()
        {
            // If closed lets return
            if (!this.Opened)
                return;

            // Close the stream and our reader and writer
            this.Stream.Close();
            this.Reader.Close();
            this.Writer.Close();

            // Let us know the stream is closed
            this.Opened = false;

            // If it's a temp let's delete the file
            if (this._isTemp)
            {
                try
                {
                    System.IO.File.Delete(_filePath);
                }
                catch { }
            }

        }

        /// <summary>
        /// Read the stream from beginning to end and return the buffer. Does not change the position.
        /// </summary>
        /// <returns>A byte array of the entire stream.</returns>
        public byte[] ToArray()
        {
            //If not opened, return nothing
            if (!Opened)
                return null;

            //Read the entire stream
            long pos = Position;
            Position = 0;
            byte[] buffer = Reader.ReadBytes((int)Length);
            Position = pos;
            return buffer;

        }

        /// <summary>
        /// Dispose the stream and delete the file if it is a temp stream.
        /// </summary>
        public void Dispose()
        {
            Close();
            if (_isFile && _isTemp)
                File.Delete(_filePath);
            Stream.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Copies the contents of this stream into another. Uses a 4K buffer.
        /// </summary>
        /// <param name="other">The stream to copy to.</param>
        public void CopyTo(CoreIO other)
        {
            long pos = this.Position;
            this.Position = 0;

            // Do the copying: Fix this later
            other.Position = 0;
            byte[] buffer = ToArray();
            other.Writer.Write(buffer);

            // Reset this position
            this.Position = pos;
        }

        public delegate void FileTransferProgressChanged(ProgressChangedEventArgs args);
        public struct ProgressChangedEventArgs
        {
            public int Percent;
            public string Status;
        }

        /// <summary>
        /// A custom CopyTo method that allows progress monitoring.
        /// </summary>
        /// <param name="other">The IO to copy to.</param>
        /// <param name="progressChanged">The delegate for the progress changed raising.</param>
        public void TransferTo(CoreIO other, FileTransferProgressChanged progressChanged = null, string statusToShow = "Transferring")
        {
            // Set up our buffer size
            int bufferSize = 0x1000;

            // Save the current position for later
            long curPos = this.Position;

            // Set both positions
            this.Position = 0;
            other.Position = 0;

            // Set the destination stream length
            // to make room
            other.Stream.SetLength(this.Length);

            // Do the iterations
            int i = 0;
            int oldpercent = -1;
            while (this.Position < this.Length)
            {
                // Make sure everything's correct
                other.Position = i * bufferSize;
                this.Position = i * bufferSize;

                // Read the buffer
                byte[] buff = this.Reader.ReadBytes(Math.Min(bufferSize,
                    (int)this.Length - (int)this.Position));

                // Write the buffer
                other.Writer.Write(buff);

                // Update progress if wanted
                if (progressChanged != null)
                {
                    if (i % 4 == 0 || this.Position >= this.Length)
                    {
                        double progress = ((double)this.Position / (double)this.Length) * 100;
                        int percent = (int)Math.Ceiling(progress);
                        if (oldpercent != percent)
                        {
                            ProgressChangedEventArgs args = new ProgressChangedEventArgs()
                            {
                                Percent = (int)Math.Ceiling(progress),
                                Status = statusToShow
                            };
                            progressChanged(args);
                        }
                        oldpercent = percent;
                    }
                }
                i++;
            }

            // Set the position back
            this.Position = curPos;

        }

        /// <summary>
        /// Copies the contents of this stream into another. Uses a 4K buffer.
        /// </summary>
        /// <param name="other">The stream to copy to.</param>
        public void CopyTo(Stream other)
        {
            CoreIO io = new CoreIO(other, EndianStyle.BigEndian);
            CopyTo(io);
        }

    }

    /// <summary>
    /// A BinaryReader class with extra features.
    /// </summary>
    public class EndianReader : BinaryReader
    {
        private readonly EndianStyle _endianStyle;

        public EndianReader(Stream Stream, EndianStyle EndianStyle)
            : base(Stream)
        {
            // Set our EndianStyle
            _endianStyle = EndianStyle;
        }

        /// <summary>
        /// Change the position of the stream.
        /// </summary>
        /// <param name="position">The address to change to.</param>
        public void Seek(long position)
        {
            base.BaseStream.Position = position;
        }

        /// <summary>
        /// Change the position of the stream and read a UInt32.
        /// </summary>
        /// <param name="Address">The address to change to.</param>
        /// <returns>A UInt of the value read.</returns>
        public uint SeekAndRead(long Address)
        {
            base.BaseStream.Position = Address;
            return ReadUInt32();
        }

        /// <summary>
        /// Read an Int16 (short) at the current position.
        /// </summary>
        /// <returns>An Int16 that was read.</returns>
        public override short ReadInt16()
        {
            // Read a 16 bit Integer
            return ReadInt16(_endianStyle);
        }

        /// <summary>
        /// Read an Int16 (short) at the current position.
        /// </summary>
        /// <param name="EndianStyle">The EndianStyle to read with.</param>
        /// <returns>An Int16 that was read.</returns>
        public short ReadInt16(EndianStyle EndianStyle)
        {
            // Read our 16 bit buffer
            byte[] buffer = base.ReadBytes(2);

            // If big endian reverse the bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Return our 16 bit value
            return BitConverter.ToInt16(buffer, 0);
        }

        /// <summary>
        /// Read a UInt16 (ushort) at the current position.
        /// </summary>
        /// <returns>A UInt16 that was read.</returns>
        public override ushort ReadUInt16()
        {
            // Read an unsigned 16 bit Integer
            return ReadUInt16(_endianStyle);
        }

        /// <summary>
        /// Read a UInt16 (ushort) at the current position.
        /// </summary>
        /// <param name="EndianStyle">The EndianStyle to read with.</param>
        /// <returns>A UInt16 that was read.</returns>
        public ushort ReadUInt16(EndianStyle EndianStyle)
        {
            // Read our 16 bit buffer
            byte[] buffer = base.ReadBytes(2);

            // If big endian reverse the bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Return our unsigned 16 bit value
            return BitConverter.ToUInt16(buffer, 0);
        }

        /// <summary>
        /// Read an Int24 (3 bytes) at the current position.
        /// </summary>
        /// <returns>An Int32 that matches the Int24.</returns>
        public int ReadInt24()
        {
            // Read a 24 bit integer
            return ReadInt24(_endianStyle);
        }

        /// <summary>
        /// Read an Int24 (3 bytes) at the current position.
        /// </summary>
        /// <param name="EndianStyle">The EndianStyle to read with.</param>
        /// <returns>An Int32 that matches the Int24.</returns>
        public int ReadInt24(EndianStyle EndianStyle)
        {
            // Read our 24 bit buffer
            byte[] buffer = base.ReadBytes(3);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                return (buffer[0] << 16) | (buffer[1] << 8) | buffer[2];

            // Return our 24 bit integer
            return (buffer[2] << 16) | (buffer[1] << 8) | buffer[0];
        }

        /// <summary>
        /// Read an Int32 at the current position.
        /// </summary>
        /// <returns>An Int32 that was read.</returns>
        public override int ReadInt32()
        {
            // Read a 32 bit Integer
            return ReadInt32(_endianStyle);
        }

        /// <summary>
        /// Read an Int32 at the current position.
        /// </summary>
        /// <param name="EndianStyle">The EndianStyle to read with.</param>
        /// <returns>An Int32 that was read.</returns>
        public int ReadInt32(EndianStyle EndianStyle)
        {
            long x = BaseStream.Position;

            // Read our 32 bit buffer
            byte[] buffer = base.ReadBytes(4);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Return our 32 bit value
            return BitConverter.ToInt32(buffer, 0);
        }

        /// <summary>
        /// Read a UInt32 at the current position.
        /// </summary>
        /// <returns>A UInt32 that was read.</returns>
        public override uint ReadUInt32()
        {
            // Read a unsigned 32 bit integer
            return ReadUInt32(_endianStyle);
        }

        /// <summary>
        /// Read a UInt32 at the current position.
        /// </summary>
        /// <param name="EndianStyle">The EndianStyle to read with.</param>
        /// <returns>A UInt32 that was read.</returns>
        public uint ReadUInt32(EndianStyle EndianStyle)
        {
            // Read our 32 bit buffer
            byte[] buffer = base.ReadBytes(4);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Return our 32 bit value
            return BitConverter.ToUInt32(buffer, 0);
        }

        /// <summary>
        /// Read an Int64 (long) at the current position.
        /// </summary>
        /// <returns>An Int64 that was read.</returns>
        public override long ReadInt64()
        {
            // Read a 64 bit integer
            return ReadInt64(_endianStyle);
        }

        /// <summary>
        /// Read an Int64 (long) at the current position.
        /// </summary>
        /// <param name="EndianStyle">The EndianStyle to read with.</param>
        /// <returns>An Int64 that was read.</returns>
        public long ReadInt64(EndianStyle EndianStyle)
        {
            // Read our 64 bit buffer
            byte[] buffer = base.ReadBytes(8);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Return our 64 bit value
            return BitConverter.ToInt64(buffer, 0);
        }

        /// <summary>
        /// Read a UInt64 (ulong) at the current position.
        /// </summary>
        /// <returns>A UInt64 that was read.</returns>
        public override ulong ReadUInt64()
        {
            // Read an unsigned 64 bit integer
            return ReadUInt64(_endianStyle);
        }

        /// <summary>
        /// Read a UInt64 (ulong) at the current position.
        /// </summary>
        /// <param name="EndianStyle">The EndianStyle to read with.</param>
        /// <returns>A UInt64 that was read.</returns>
        public ulong ReadUInt64(EndianStyle EndianStyle)
        {
            // Read our 64 bit buffer
            byte[] buffer = base.ReadBytes(8);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Return our 64 bit value
            return BitConverter.ToUInt64(buffer, 0);
        }

        /// <summary>
        /// Read a Single (float) at the current position.
        /// </summary>
        /// <returns>A Single that was read.</returns>
        public override float ReadSingle()
        {
            // Read a signle
            return ReadSingle(_endianStyle);
        }

        /// <summary>
        /// Read a Single (float) at the current position.
        /// </summary>
        /// <param name="EndianStyle">The EndianStyle to read with.</param>
        /// <returns>A Single that was read.</returns>
        public float ReadSingle(EndianStyle EndianStyle)
        {
            // Read our 32 bit buffer
            byte[] buffer = base.ReadBytes(4);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Return our single
            return BitConverter.ToSingle(buffer, 0);
        }

        /// <summary>
        /// Read a Double at the current position.
        /// </summary>
        /// <returns>A Double that was read.</returns>
        public override double ReadDouble()
        {
            // Read a double
            return ReadDouble(_endianStyle);
        }

        /// <summary>
        /// Read a Double at the current position.
        /// </summary>
        /// <param name="EndianStyle">The EndianStyle to read with.</param>
        /// <returns>A Double that was read.</returns>
        public double ReadDouble(EndianStyle EndianStyle)
        {
            // Read a 64bit buffer
            byte[] buffer = base.ReadBytes(8);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Return our double
            return BitConverter.ToDouble(buffer, 0);
        }

        /// <summary>
        /// Read a Unicode string from the stream.
        /// </summary>
        /// <param name="Length">The length of the string to read.</param>
        /// <returns>A unicode string that has been read.</returns>
        public string ReadUnicodeString(int length)
        {
            // Read a unicode string
            return ReadUnicodeString(length, _endianStyle);
        }

        /// <summary>
        /// Read a Unicode string from the stream.
        /// </summary>
        /// <param name="length">The length of the string to read.</param>
        /// <param name="endianStyle">The EndianStyle to read with.</param>
        /// <returns>A unicode string that has been read.</returns>
        public string ReadUnicodeString(int length, EndianStyle endianStyle)
        {
            string newString = string.Empty;
            int howMuch = 0;
            for (int x = 0; x < length; x++)
            {
                ushort tempChar = ReadUInt16(endianStyle);
                howMuch++;
                if (tempChar != 0x00)
                    newString += (char)tempChar;
                else
                    break;
            }

            int size = (length - howMuch) * sizeof(UInt16);
            BaseStream.Seek(size, SeekOrigin.Current);

            return newString;
        }

        /// <summary>
        /// Read a Unicode string that ends with a Null (00).
        /// </summary>
        /// <returns>A unicode string that has been read.</returns>
        public string ReadUnicodeNullTermString()
        {
            return ReadUnicodeNullTermString(_endianStyle);
        }

        /// <summary>
        /// Read a Unicode string that ends with a Null (00).
        /// </summary>
        /// <param name="endianStyle">The EndianStyle to read with.</param>
        /// <returns>A unicode string that has been read.</returns>
        public string ReadUnicodeNullTermString(EndianStyle endianStyle)
        {
            string str = string.Empty;
            while (true)
            {
                // Read our bytes
                byte[] charBuffer = this.ReadBytes(2);

                // Check if our bugger is null
                if (charBuffer[0] == 0 & charBuffer[1] == 0)
                {
                    return str;
                }

                // Add our char to our string
                if (endianStyle == EndianStyle.BigEndian)
                {
                    str = str + Encoding.BigEndianUnicode.GetString(charBuffer);
                }
                else
                {
                    str = str + Encoding.Unicode.GetString(charBuffer);
                }
            }
        }

        /// <summary>
        /// Read an ASCII string from the stream.
        /// </summary>
        /// <param name="length">The length of the string to read.</param>
        /// <returns>An ASCII string read from the stream.</returns>
        public string ReadAsciiString(int length)
        {
            string newString = string.Empty;
            int howMuch = 0;
            for (int x = 0; x < length; x++)
            {
                byte tempChar = ReadByte();
                howMuch++;
                if (tempChar != 0)
                    newString += (char)tempChar;
                else
                    break;
            }
            int size = (length - howMuch);
            BaseStream.Seek(size, SeekOrigin.Current);
            return newString;
        }

        /// <summary>
        /// Reads an ascii string until a null value is hit.
        /// </summary>
        /// <returns>The string read from the file.</returns>
        public string ReadAsciiCString()
        {
            string newString = string.Empty;
            while (true)
            {
                char nextChar = (char)ReadByte();
                if (nextChar == 0)
                    break;
                newString += nextChar;
            }
            return newString;
        }

        /// <summary>
        /// The relative position to begin looking.
        /// </summary>
        public enum FindOrigin
        {
            Begin,
            Current
        }

        /// <summary>
        /// Finds all occurances of a byte array in the stream. Does not change position.
        /// </summary>
        /// <param name="needle">The byte array to look for.</param>
        /// <param name="origin">The relative position to begin looking from.</param>
        /// <returns>An array of long containing absolute occurance addresses.</returns>
        public long[] Find(byte[] needle, FindOrigin origin)
        {
            return Find(needle, origin, 0);
        }

        /// <summary>
        /// Finds a certain amount of occurances in the stream. Does not change position.
        /// </summary>
        /// <param name="needle">The byte array to look for.</param>
        /// <param name="origin">The relative position to begin looking from.</param>
        /// <param name="MaxOccurances">The amount of occurances to look for. 0 means unlimited.</param>
        /// <returns>An array of long containing absolute occurance addresses.</returns>
        public long[] Find(byte[] needle, FindOrigin origin, int MaxOccurances)
        {
            List<long> rtn = new List<long>();
            if (MaxOccurances < 0)
                return rtn.ToArray();
            long oldPos = BaseStream.Position;
            if (origin == FindOrigin.Begin)
                BaseStream.Position = 0;
            long startPos = BaseStream.Position;
            int i = 0;
            while (((startPos + i) + needle.Length) <= BaseStream.Length)
            {
                BaseStream.Position = startPos + i;
                byte[] buffer = ReadBytes(needle.Length);
                if (needle.SequenceEqual(buffer))
                {
                    rtn.Add(startPos + i);
                    if ((rtn.Count >= MaxOccurances) && (MaxOccurances > 0))
                        break;
                    i += needle.Length;
                }
                else
                {
                    i++;
                }
            }
            BaseStream.Position = oldPos;
            return rtn.ToArray();
        }

    }

    /// <summary>
    /// A BinaryWriter class with extra features.
    /// </summary>
    public class EndianWriter : BinaryWriter
    {
        private readonly EndianStyle _endianStyle;

        public EndianWriter(Stream Stream, EndianStyle EndianStyle)
            : base(Stream)
        {
            _endianStyle = EndianStyle;
        }

        /// <summary>
        /// Seek the stream to a position.
        /// </summary>
        /// <param name="position">The position to seek to.</param>
        public void Seek(long position)
        {
            base.BaseStream.Position = position;
        }

        /// <summary>
        /// Write a short to the stream.
        /// </summary>
        /// <param name="value">The short value to write.</param>
        public override void Write(short value)
        {
            // Write a 16bit integer
            Write(value, _endianStyle);
        }

        /// <summary>
        /// Write a short to the stream.
        /// </summary>
        /// <param name="value">The short value to write.</param>
        /// <param name="EndianStyle">The EndianStyle to write with.</param>
        public void Write(short value, EndianStyle EndianStyle)
        {
            // Get our 16 bit buffer
            byte[] buffer = BitConverter.GetBytes(value);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Write our 16 bit buffer
            base.Write(buffer);
        }

        /// <summary>
        /// Write a ushort to the stream.
        /// </summary>
        /// <param name="value">The ushort to write.</param>
        public override void Write(ushort value)
        {
            // Write a unsigned 16 bit integer
            Write(value, _endianStyle);
        }

        /// <summary>
        /// Write a ushort to the stream.
        /// </summary>
        /// <param name="value">The ushort to write.</param>
        /// <param name="EndianStyle">The EndianStyle to write with.</param>
        public void Write(ushort value, EndianStyle EndianStyle)
        {
            // Get our 16 bit buffer
            byte[] buffer = BitConverter.GetBytes(value);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Write our 16 bit buffer
            base.Write(buffer);
        }

        /// <summary>
        /// Write an int to the stream.
        /// </summary>
        /// <param name="value">The int to write.</param>
        public override void Write(int value)
        {
            // Write a 32 bit integer
            Write(value, _endianStyle);
        }

        /// <summary>
        /// Write an int to the stream.
        /// </summary>
        /// <param name="value">The int to write.</param>
        /// <param name="EndianStyle">The EndianStyle to write with.</param>
        public void Write(int value, EndianStyle EndianStyle)
        {
            // Get our 32 bit buffer
            byte[] buffer = BitConverter.GetBytes(value);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Write our 32 bit buffer
            base.Write(buffer);
        }

        /// <summary>
        /// Write an Int24 to the stream.
        /// </summary>
        /// <param name="value">The int value to write.</param>
        public void WriteInt24(int value)
        {
            // Write a 24 bit integer
            WriteInt24(value, _endianStyle);
        }

        /// <summary>
        /// Write an Int24 to the stream.
        /// </summary>
        /// <param name="value">The int value to write.</param>
        /// <param name="endianType">The EndianStyle to write with.</param>
        public void WriteInt24(int value, EndianStyle endianType)
        {
            // Get our 32 bit buffer
            byte[] buffer = BitConverter.GetBytes(value);

            // Resize our buffer down to 24 bits
            Array.Resize(ref buffer, 3);

            // If Big endian reverse our bits
            if (endianType == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Write our 24 bit buffer
            base.Write(buffer);

        }

        /// <summary>
        /// Write a uint to the stream.
        /// </summary>
        /// <param name="value">The uint value to write.</param>
        public override void Write(uint value)
        {
            // Write an unsigned 32 bit integer
            Write(value, _endianStyle);
        }

        /// <summary>
        /// Write a uint to the stream.
        /// </summary>
        /// <param name="value">The uint value to write.</param>
        /// <param name="EndianStyle">The EndianStyle to write with.</param>
        public void Write(uint value, EndianStyle EndianStyle)
        {
            // Get our 32 bit buffer
            byte[] buffer = BitConverter.GetBytes(value);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Write our 32 bit buffer
            base.Write(buffer);
        }

        /// <summary>
        /// Write a long to the stream.
        /// </summary>
        /// <param name="value">The long value to write.</param>
        public override void Write(long value)
        {
            // Write a 64 bit integer
            Write(value, _endianStyle);
        }

        /// <summary>
        /// Write a long to the stream.
        /// </summary>
        /// <param name="value">The long value to write.</param>
        /// <param name="EndianStyle">The EndianStyle to write with.</param>
        public void Write(long value, EndianStyle EndianStyle)
        {
            // Get our 64 bit buffer
            byte[] buffer = BitConverter.GetBytes(value);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Write our 64 bit buffer
            base.Write(buffer);
        }

        /// <summary>
        /// Write a ulong to the stream.
        /// </summary>
        /// <param name="value">The ulong value to write.</param>
        public override void Write(ulong value)
        {
            // Write an unsigned 64 bit integer
            Write(value, _endianStyle);
        }

        /// <summary>
        /// Write a ulong to the stream.
        /// </summary>
        /// <param name="value">The ulong value to write.</param>
        /// <param name="EndianStyle">The EndianStyle to write with.</param>
        public void Write(ulong value, EndianStyle EndianStyle)
        {
            // Get our 64 bit buffer
            byte[] buffer = BitConverter.GetBytes(value);

            // If big endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Write our 64 bit buffer
            base.Write(buffer);
        }

        /// <summary>
        /// Write a float (single) to the stream.
        /// </summary>
        /// <param name="value">The float value to write.</param>
        public override void Write(float value)
        {
            // Write a single
            Write(value, _endianStyle);
        }

        /// <summary>
        /// Write a float (single) to the stream.
        /// </summary>
        /// <param name="value">The float value to write.</param>
        /// <param name="EndianStyle">The EndianStyle to write with.</param>
        public void Write(float value, EndianStyle EndianStyle)
        {
            // Get our 32 bit buffer
            byte[] buffer = BitConverter.GetBytes(value);

            // If bit endian reverse our bits
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Write our 32 bit buffer
            base.Write(buffer);
        }

        /// <summary>
        /// Write a double to the stream.
        /// </summary>
        /// <param name="value">The double value to write.</param>
        public override void Write(double value)
        {
            // Write a double
            Write(value, _endianStyle);
        }

        /// <summary>
        /// Write a double to the stream.
        /// </summary>
        /// <param name="value">The double value to write.</param>
        /// <param name="EndianStyle">The EndianStyle to write with.</param>
        public void Write(double value, EndianStyle EndianStyle)
        {
            // Get our 64 bit buffer
            byte[] buffer = BitConverter.GetBytes(value);

            // If big endian reverse our buffer
            if (EndianStyle == EndianStyle.BigEndian)
                Array.Reverse(buffer);

            // Write our 64 bit buffer
            base.Write(buffer);
        }

        /// <summary>
        /// Write an Ascii string to the stream.
        /// </summary>
        /// <param name="str">The string to write.</param>
        public void WriteAsciiString(string str)
        {
            WriteAsciiString(str, str.Length);
        }

        /// <summary>
        /// Writes an ascii c-string, denoted by a null at the end.
        /// </summary>
        /// <param name="str">The string to write.</param>
        public void WriteAsciiCString(string str)
        {
            WriteAsciiString(str, str.Length);
            Write((byte)0);
        }

        /// <summary>
        /// Write an Ascii string to the stream.
        /// </summary>
        /// <param name="string">The string to write.</param>
        /// <param name="length">The length to pad/truncate to.</param>
        public void WriteAsciiString(string str, int length)
        {
            int strLen = str.Length;
            for (int x = 0; x < strLen; x++)
            {
                if (x > length)
                    break; // Just incase they pass a huge string

                byte val = (byte)str[x];
                Write(val);
            }

            int nullSize = (length - strLen) * sizeof(byte);
            if (nullSize > 0)
                Write(new byte[nullSize]);
        }

        /// <summary>
        /// Write a Unicode string to the stream.
        /// </summary>
        /// <param name="str">The string to write.</param>
        public void WriteUnicodeString(string str)
        {
            WriteUnicodeString(str, str.Length);
        }

        /// <summary>
        /// Write a Unicode string to the stream.
        /// </summary>
        /// <param name="str">The string to write.</param>
        /// <param name="length">The length of the string to pad/truncate to.</param>
        public void WriteUnicodeString(string str, int length)
        {
            WriteUnicodeString(str, length, _endianStyle);
        }

        /// <summary>
        /// Write a Unicode string to the stream.
        /// </summary>
        /// <param name="str">The string to write.</param>
        /// <param name="length">The length of the string to pad/truncate to.</param>
        /// <param name="endianStyle">The EndianStyle to write with.</param>
        public void WriteUnicodeString(string str, int length, EndianStyle endianStyle)
        {
            int strLen = str.Length;
            for (int x = 0; x < strLen; x++)
            {
                if (x > length)
                    break; // Just incase they pass a huge string

                ushort val = str[x];
                Write(val, endianStyle);
            }

            int nullSize = (length - strLen) * sizeof(ushort);
            if (nullSize > 0)
                Write(new byte[nullSize]);
        }

        /// <summary>
        /// Write a Unicode string to the stream with a null term after.
        /// </summary>
        /// <param name="str">The string to write.</param>
        public void WriteUnicodeNullTermString(string str)
        {
            WriteUnicodeNullTermString(str, _endianStyle);
        }

        /// <summary>
        /// Write a Unicode string to the stream with a null term after.
        /// </summary>
        /// <param name="str">The string to write.</param>
        /// <param name="endianStyle">The EndianStyle to write with.</param>
        public void WriteUnicodeNullTermString(string str, EndianStyle endianStyle)
        {
            int strLen = str.Length;
            for (int x = 0; x < strLen; x++)
            {
                ushort val = str[x];
                Write(val, endianStyle);
            }
            Write((ushort)0, endianStyle);
        }

    }
}
