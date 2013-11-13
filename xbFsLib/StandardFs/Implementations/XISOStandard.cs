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
using xbFsLib.IO;

namespace xbFsLib.StandardFs.Implementations
{

    /// <summary>
    /// A standard filesystem implementation of a Xbox 360 ISO.
    /// </summary>
    class ISOFilesystem : StandardFs
    {
        /// <summary>
        /// The underlying IO for the ISO.
        /// </summary>
        IO.CoreIO io;

        // Instance variables
        private string rootName;
        private List<Entry> entries = new List<Entry>();

        /// <summary>
        /// The size, in bytes, of a sector in the ISO.
        /// </summary>
        private const int SECTOR_SIZE = 2048;

        // Some constants
        private const int XBOX_ORIGINAL_SECTOR = 198144;
        private const int XBOX_360_GLOBAL_OFFSET = 129824;
        private const int XBOX_360_GLOBAL_OFFSET_2 = 1783936;
        private const int XBOX_360_GLOBAL_OFFSET_3 = 16640;

        /// <summary>
        /// The various global start offsets that could be used.
        /// </summary>
        private uint[] XBOX_360_GLOBAL_OFFSETS = { 0, 16640, 129824, 17839360 };

        // Information about the root sector
        private int rootSector;
        private uint global_offset;
        private uint rootSize;

        /// <summary>
        /// Creates a new ISOFilesytem given the filename of the ISO on disk.
        /// </summary>
        /// <param name="filename">The location of the ISO on disk.</param>
        public ISOFilesystem(string filename)
        {

            // Set up the io
            io = new CoreIO(filename, EndianStyle.LittleEndian);
            rootName = Utils.GetNameFromPath(filename);

            // Let's get some info about the root sector
            int indexToUse = -1;
            for (int i = 0; i < XBOX_360_GLOBAL_OFFSETS.Length; i++)
            {
                try
                {
                    global_offset = XBOX_360_GLOBAL_OFFSETS[i];
                    io.Position = (global_offset + 32) * SECTOR_SIZE;
                    if (io.Reader.ReadAsciiString(20) == "MICROSOFT*XBOX*MEDIA")
                    {
                        rootSector = io.Reader.ReadInt32();
                        rootSize = io.Reader.ReadUInt32();
                        indexToUse = i;
                        break;
                    }
                }
                catch { throw new Exception("Invalid Xbox 360 ISO."); }
            }
            if (indexToUse == -1)
            {
                io.Close();
                throw new Exception("Invalid Xbox 360 ISO.");
            }

            // Make sure our root size is at least the block size
            if (rootSize < SECTOR_SIZE)
                rootSize = SECTOR_SIZE;

            // Let's read in all of the entries, starting with the root
            this.entries = ReadEntries(rootSector);

        }

        /// <summary>
        /// Returns the friendly root name of this filesystem.
        /// </summary>
        public override string RootName
        {
            get
            {
                return rootName;
            }
        }

        /// <summary>
        /// Whether or not to get extra information about the file entries.
        /// </summary>
        public override bool UseEnhancedExplorerInfo
        {
            get
            {
                // ISOs can have a lot of big files, no need to do the enhanced info
                return false;
            }
        }

        /// <summary>
        /// Returns the position for the sector in the stream.
        /// </summary>
        /// <param name="sector">The sector to use.</param>
        /// <returns>The position to use.</returns>
        public long GetSectorPosition(int sector)
        {
            return (this.global_offset + sector) * SECTOR_SIZE;
        }

        /// <summary>
        /// Whether or not this filesystem is read only.
        /// </summary>
        /// <returns>True if it is read only.</returns>
        public override bool IsReadOnly()
        {
            return false;
        }

        /// <summary>
        /// Adds a file into the filesystem.
        /// </summary>
        /// <param name="fileIo">The IO on disk for the file to add.</param>
        /// <param name="filenameOnFilesystem">The location in the ISO to place the file.</param>
        /// <param name="overwrite">Whether or not to overwrite any existing files.</param>
        /// <param name="progressChanged">The handler for progress being changed.</param>
        /// <returns>True if added successfully.</returns>
        public override bool AddFile(CoreIO fileIo, string filenameOnFilesystem, bool overwrite, CoreIO.FileTransferProgressChanged progressChanged = null)
        {
            // Make sure the file exists to replace
            if (!FileExists(filenameOnFilesystem))
            {
                fileIo.Close();
                throw new Exception("New files may not be added to ISO's, files can only be replaced.");
            }

            // Make sure we're not going to hit other data
            ISOFileEntry entry = (ISOFileEntry)GetEntry(filenameOnFilesystem);
            long start = GetSectorPosition(entry.entry.Sector);
            long newEnd = start + fileIo.Length;
            for (int i = 0; i < entries.Count; i++)
            {
                long thisStart = 0;
                if (entries[i].IsFolder)
                {
                    ISODirectoryEntry thisEntry = (ISODirectoryEntry)entries[i];
                    thisStart = GetSectorPosition(thisEntry.entry.Sector);
                } else {
                    ISOFileEntry thisEntry = (ISOFileEntry)entries[i];
                    thisStart = GetSectorPosition(thisEntry.entry.Sector);
                }
                if (thisStart > start && thisStart <= newEnd)
                    throw new Exception("Your replacement data is too large and would corrupt with the entry '" + entries[i].Name + "' if used.");
            }

            // Check to see if we're out of our bounds
            uint sectors = (uint)Math.Ceiling((double)entry.Size / (double)SECTOR_SIZE);
            uint newSectors = (uint)Math.Ceiling((double)fileIo.Length / (double)SECTOR_SIZE);
            if (newSectors > sectors)
            {
                if (System.Windows.Forms.MessageBox.Show("You are attempting to replace a file outside it's allocated size, we checked and " +
                    "it seems like no other files will be corrupted but it's possible some important info may be corrupted, would you like to continue?", "Continue Replace?", System.Windows.Forms.MessageBoxButtons.YesNoCancel)
                    != System.Windows.Forms.DialogResult.Yes)
                    return false;
            }

            // Do the transfer
            IO.CoreIO newIo = GetFileIO(filenameOnFilesystem);
            fileIo.TransferTo(newIo, progressChanged, "Replacing");

            // Update the entry
            int index = entries.IndexOf(GetEntry(filenameOnFilesystem));
            ISOFileEntry curEnt = (ISOFileEntry)GetEntry(filenameOnFilesystem);
            curEnt.SetSize((uint)fileIo.Length);
            curEnt.entry.Size = (uint)fileIo.Length;
            curEnt.entry.Save(this.io);
            entries[index] = curEnt;

            return true;
        }

        /// <summary>
        /// Determines whether a string is a valid entry name in the ISO.
        /// </summary>
        /// <param name="name">The string to check.</param>
        /// <returns>True if it is valid.</returns>
        protected override bool IsValidEntryName(string name)
        {
            if (name.Length > 255)
                return false;
            return true;
        }

        /// <summary>
        /// Gets an entry from the filesystem given a path.
        /// </summary>
        /// <param name="path">The path of the entry to get.</param>
        /// <returns></returns>
        public override Entry GetEntry(string path)
        {
            // Let's look through the entries for the path
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Path == path)
                    return entries[i];

            // We didn't find it
            return null;
        }

        /// <summary>
        /// Gets the full path of an entry.
        /// </summary>
        /// <param name="entry">The entry to get the path for.</param>
        /// <returns>The full path of the entry as a string.</returns>
        protected override string GetEntryPath(Entry entry)
        {
            return entry.Path;
        }

        /// <summary>
        /// Entries cannot be deleted from ISO files.
        /// </summary>
        /// <param name="entry">The entry to delete.</param>
        /// <returns>Throws an exception.</returns>
        protected override bool DeleteEntry(Entry entry)
        {
            throw new Exception("Files cannot be deleted. Files can only be replaced.");
        }

        /// <summary>
        /// Entries cannot be created from ISO files.
        /// </summary>
        /// <param name="path">The path of the entry to create.</param>
        /// <param name="directory">Whether or not the entry should be a directory.</param>
        /// <returns>Throws an exception.</returns>
        protected override bool CreateEntry(string path, bool directory)
        {
            throw new Exception("Files cannot be created. Files can only be replaced.");
        }

        /// <summary>
        /// Entries cannot be renamed in ISO files.
        /// </summary>
        /// <param name="entry">The entry to be renamed.</param>
        /// <param name="newName">The new name for the entry to be given.</param>
        /// <returns>Throws an exception.</returns>
        protected override bool RenameEntry(Entry entry, string newName)
        {
            throw new Exception("Files cannot be renamed. Files can only be replaced.");
        }

        /// <summary>
        /// Gets a list of entries in a directory.
        /// </summary>
        /// <param name="entry">The directory entry to use.</param>
        /// <returns>A list of entries that are in the directory.</returns>
        protected override List<Entry> GetEntries(Entry entry)
        {
            List<Entry> rtn = new List<Entry>();

            // If we were given the root entry
            if (entry == null)
            {
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i].BasePath == "")
                        rtn.Add(entries[i]);
                return rtn;
            }

            // It's not the root so let's use it
            if (!entry.IsFolder)
                return rtn;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].BasePath == entry.Path)
                    rtn.Add(entries[i]);
            }

            // Return our result
            return rtn;
        }

        /// <summary>
        /// Closes the ISO filesystem.
        /// </summary>
        protected override void CloseFilesystem()
        {
            io.Close();
            return;
        }

        /// <summary>
        /// Gets the IO stream of an entry.
        /// </summary>
        /// <param name="path">The path of the entry to get the stream of.</param>
        /// <param name="mode">The FileMode to use when getting the entry.</param>
        /// <returns>A CoreIO of the entry stream.</returns>
        protected override CoreIO GetEntryStream(string path, System.IO.FileMode mode)
        {
            // Get the entry from the path
            Entry ent = GetEntry(path);

            // Make sure it's good
            if (ent == null)
                throw new Exception("Invalid entry.");
            if (ent.IsFolder)
                throw new Exception("Entry is directory.");

            // Create the IO to give
            ISOFileEntry entry = (ISOFileEntry)ent;
            long pos = GetSectorPosition(entry.entry.Sector);
            IO.CoreIO newIo = new CoreIO(new ISOFileStream(entry.entry, pos, io.Stream), EndianStyle.BigEndian);

            // Return the new IO
            return newIo;
        }

        /// <summary>
        /// Gets the amount of space used in the ISO.
        /// </summary>
        /// <returns>The amount of space used as a long.</returns>
        public override long GetUsedSpace()
        {
            long total = 0;
            for (int i = 0; i < entries.Count; i++)
                if (!entries[i].IsFolder)
                    total += entries[i].Size;
            return total;
        }

        /// <summary>
        /// Gets the amount of free space in the ISO. Which should be 0.
        /// </summary>
        /// <returns>The amount of free space as a long.</returns>
        public override long GetFreeSpace()
        {
            return 0;
        }

        public override long GetTotalSpace()
        {
            long total = 0;
            for (int i = 0; i < entries.Count; i++)
                if (!entries[i].IsFolder)
                    total += entries[i].Size;
            return total;
        }

        /// <summary>
        /// Will recursively read through the directory entries for the iso.
        /// </summary>
        /// <param name="sector">The current directory sector.</param>
        /// <param name="currentPath">The current path of the sector.</param>
        /// <returns>A list of entries.</returns>
        private List<Entry> ReadEntries(int sector, string currentPath = "")
        {
            List<Entry> rtn = new List<Entry>();
            List<ISOEntry> entries = ReadDirectorySector(sector);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsDirectory)
                {
                    rtn.Add(new ISODirectoryEntry(entries[i], currentPath));
                    if (entries[i].Sector > 0)
                        rtn.AddRange(ReadEntries(entries[i].Sector, currentPath + (currentPath == "" ? "" : "\\") + entries[i].Name));
                }
                else
                    rtn.Add(new ISOFileEntry(entries[i], currentPath));
            }
            return rtn;
        }

        /// <summary>
        /// Reads all of the ISOEntries in a directory sector.
        /// </summary>
        /// <param name="sector">The directory sector.</param>
        /// <param name="currentOffset">The current offset for the entry.</param>
        /// <returns>A list of entries.</returns>
        private List<ISOEntry> ReadDirectorySector(int sector, int currentOffset = 0)
        {
            List<ISOEntry> rtn = new List<ISOEntry>();
            io.Position = GetSectorPosition(sector) + currentOffset;
            int previousEntryOffset = io.Reader.ReadInt16() * 4;
            int nextEntryOffset = io.Reader.ReadInt16() * 4;
            long curPos = io.Position;
            if (previousEntryOffset > 0)
                rtn.AddRange(ReadDirectorySector(sector, previousEntryOffset));

            io.Position = curPos;
            try
            {
                rtn.Add(new ISOEntry(io));
            }
            catch { }

            if (nextEntryOffset > 0)
                rtn.AddRange(ReadDirectorySector(sector, nextEntryOffset));

            return rtn;
        }

        /// <summary>
        /// Represents an entry in an ISO filesystem.
        /// </summary>
        public class ISOEntry
        {
            // Private instance variables
            private long position;

            // Public instance variables
            public int Sector;
            public uint Size;
            public byte Flags;
            public string Name;

            /// <summary>
            /// Creates a new ISOEntry object given the ISO IO.
            /// </summary>
            /// <param name="io">The IO for the ISO.</param>
            public ISOEntry(IO.CoreIO io)
            {
                position = io.Position;
                Sector = io.Reader.ReadInt32();
                Size = io.Reader.ReadUInt32();
                Flags = io.Reader.ReadByte();
                byte nameLength = io.Reader.ReadByte();
                Name = io.Reader.ReadAsciiString(nameLength);
                if (Sector == -1)
                    throw new Exception("Invalid entry.");
            }

            /// <summary>
            /// Saves the details of the entry to the ISO.
            /// </summary>
            /// <param name="io">The IO of the ISO.</param>
            public void Save(IO.CoreIO io)
            {
                io.Position = position;
                io.Position += 4;
                io.Writer.Write(Size);
            }

            /// <summary>
            /// Whether or not this entry represents a directory.
            /// </summary>
            public bool IsDirectory
            {
                get { return (Flags & 16) == 16; }
            }

        }

        /// <summary>
        /// Represents a file entry in the ISO filesystem.
        /// </summary>
        public class ISOFileEntry : FileEntry
        {
            public ISOEntry entry;

            public ISOFileEntry(ISOEntry entry, string basePath) :
                base(basePath)
            {
                this.entry = entry;
                this.name = entry.Name;
                this.modified = DateTime.Now;
                this.created = DateTime.Now;
                this.size = entry.Size;
            }

            public void SetSize(uint size)
            {
                this.size = size;
            }

        }

        /// <summary>
        /// Represents a directory entry in the ISO filesystem.
        /// </summary>
        public class ISODirectoryEntry : DirectoryEntry
        {
            public ISOEntry entry;

            public ISODirectoryEntry(ISOEntry entry, string basePath) :
                base(basePath)
            {
                this.entry = entry;
                this.name = entry.Name;
                this.modified = DateTime.Now;
                this.created = DateTime.Now;
                this.size = 0;
            }

        }

        /// <summary>
        /// Special stream class that allows reading and writing to ISO files.
        /// </summary>
        public class ISOFileStream : System.IO.Stream
        {
            private System.IO.Stream stream;
            private ISOEntry entry;
            private long startPosition;

            public ISOFileStream(ISOEntry entry, long startPosition, System.IO.Stream stream)
            {
                this.entry = entry;
                this.stream = stream;
                this.startPosition = startPosition;
                this.length = entry.Size;
            }

            public override bool CanRead
            {
                get { return stream.CanRead; }
            }

            public override bool CanSeek
            {
                get { return stream.CanSeek; }
            }

            public override bool CanWrite
            {
                get { return stream.CanWrite; }
            }

            public override void Flush()
            {
                stream.Flush();
            }

            public override void Close()
            {
                return;
            }

            private long length;
            public override long Length
            {
                get { return length; }
            }

            public override long Position
            {
                get
                {
                    return stream.Position - startPosition;
                }
                set
                {
                    stream.Position = startPosition + value;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return stream.Read(buffer, offset, count);
            }

            public override long Seek(long offset, System.IO.SeekOrigin origin)
            {
                if (origin == System.IO.SeekOrigin.Begin)
                    Position = offset;
                if (origin == System.IO.SeekOrigin.Current)
                    Position += offset;
                if (origin == System.IO.SeekOrigin.End)
                    Position = Length - offset;
                return Position;
            }

            public override void SetLength(long value)
            {
                this.length = value;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                stream.Write(buffer, offset, count);
            }
        }

    }
}
