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

namespace xbFsLib.FATX
{
    /// <summary>
    /// A directory entry for a FATX device inside a partition.
    /// </summary>
    public class Dirent
    {

        /// <summary>
        /// Attribute flags.
        /// </summary>
        [Flags]
        public enum Attribute : byte
        {
            Normal = 0x00,
            ReadOnly = 0x01,
            Hidden = 0x02,
            System = 0x04,
            Directory = 0x10,
            Archive = 0x20,
            Device = 0x40,
        }

        /// <summary>
        /// The maximum length that a filename can be.
        /// </summary>
        public const int MaxFileNameLength = 42;

        #region Properties

        private byte fileNameLength;
        private Attribute fileAttributes;
        private byte[] fileName;
        private uint firstCluster;
        private int fileSize;
        private int creationTime;
        private int lastWriteTime;
        private int lastAccessTime;


        private Object _tag;
        public Object Tag
        {
            get { return _tag; }
            set { _tag = value; }
        }

        public Attribute Attributes
        {
            get { return fileAttributes; }
            set { fileAttributes = value; }
        }

        internal int FileNameLength
        {
            get { return fileNameLength; }
            set { fileNameLength = (byte)value; }
        }

        public string Name
        {
            get
            {
                // Make sure its a valid name length
                if (FileNameLength > 0 && FileNameLength <= MaxFileNameLength)
                    return Encoding.ASCII.GetString(
                        fileName, 0, FileNameLength);

                return "{Invalid Filename}";
            }
            set
            {
                // Make sure its not too long
                if (value.Length > MaxFileNameLength)
                    throw new Exception("Filename is too long");

                // Check for invalid chars
                if (!Utilities.IsValidFATXFilename(value))
                    throw new Exception("File name contains invalid characters");

                // Get our bytes
                byte[] buffer = new byte[MaxFileNameLength];
                Encoding.ASCII.GetBytes(value, 0, value.Length, buffer, 0);

                // Set our name and length
                fileName = buffer;
                FileNameLength = value.Length;
            }
        }

        public int Size
        {
            get { return fileSize; }
            set { fileSize = value; }
        }

        internal uint FirstCluster
        {
            get { return firstCluster; }
            set { firstCluster = value; }
        }

        public DateTime CreateTime
        {
            get { return Utilities.GetDateTime(creationTime); }
            set { creationTime = Utilities.SetDateTimeInt(value); }
        }

        public DateTime ModifiedTime
        {
            get { return Utilities.GetDateTime(lastWriteTime); }
            set { lastWriteTime = Utilities.SetDateTimeInt(value); }
        }

        public DateTime AccessTime
        {
            get { return Utilities.GetDateTime(lastAccessTime); }
            set { lastAccessTime = Utilities.SetDateTimeInt(value); }
        }

        internal bool IsValidEntry { get { return !IsEmpty && IsValidFileNameLength; } }
        internal bool IsValidFileNameLength
        {
            get
            {
                if (IsDeletedDirent) return true;
                return (FileNameLength > 0 && fileNameLength <= MaxFileNameLength);
            }
        }
        internal bool IsDeletedDirent { get { return (FileNameLength == 0xE5); } }
        internal bool IsEmpty { get { return (FileNameLength == 0x00 || FileNameLength == 0xFF); } }
        public bool IsFile { get { return !IsDirectory; } }
        public bool IsDirectory
        {
            get
            {
                return ((Attributes & Attribute.Directory) ==
                    Attribute.Directory);
            }
        }

        internal int DirentIndex { get; set; }
        internal uint ParentCluster { get; set; }

        #endregion

        /// <summary>
        /// Soft-deletes this dirent. Can be undeleted.
        /// </summary>
        internal void Delete()
        {
            FileNameLength = 0xE5;
        }

        /// <summary>
        /// Undeletes this dirent if it is soft deleted.
        /// </summary>
        internal void UnDelete()
        {
            int count;
            for (count = 0; count < MaxFileNameLength; count++)
                if (fileName[count] == 0 || fileName[count] == 0xFF)
                    break;
            FileNameLength = count;
        }

        /// <summary>
        /// Completely clears the properties of this dirent.
        /// </summary>
        internal void Clear()
        {
            // This is like deleting but theres no comming back
            fileNameLength = 0x00;
            fileAttributes = Attribute.Normal;
            fileName = new byte[MaxFileNameLength];
            firstCluster = 0;
            fileSize = 0;
            creationTime = 0;
            lastWriteTime = 0;
            lastAccessTime = 0;
        }

        /// <summary>
        /// Reads in the properties from the filesystem.
        /// </summary>
        /// <param name="er">The reader for the filesystem.</param>
        internal void Read(IO.EndianReader er)
        {
            fileNameLength = er.ReadByte();
            fileAttributes = (Attribute)er.ReadByte();
            fileName = er.ReadBytes(MaxFileNameLength);
            firstCluster = er.ReadUInt32();
            fileSize = er.ReadInt32();
            creationTime = er.ReadInt32();
            lastWriteTime = er.ReadInt32();
            lastAccessTime = er.ReadInt32();
        }

        /// <summary>
        /// Writes the properties to the filesystem.
        /// </summary>
        /// <param name="ew">The writer for the filesystem.</param>
        internal void Write(IO.EndianWriter ew)
        {
            ew.Write(fileNameLength);
            ew.Write((byte)fileAttributes);
            ew.Write(fileName);
            ew.Write(firstCluster);
            ew.Write(fileSize);
            ew.Write(creationTime);
            ew.Write(lastWriteTime);
            ew.Write(lastAccessTime);
        }

    }
}
