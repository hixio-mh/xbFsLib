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

namespace xbFsLib.StandardFs.Implementations
{

    /// <summary>
    /// The standard filesystem implementation of FATX.
    /// </summary>
    public class FATXFilesystem : StandardFs
    {
        /// <summary>
        /// The backend fatx device.
        /// </summary>
        private FATX.FATXDevice device;

        /// <summary>
        /// The device id that the underlying device has.
        /// </summary>
        public string DeviceId
        {
            get { return device.DeviceId; }
        }

        /// <summary>
        /// The current partition we're using to represent the content.
        /// </summary>
        private int partition = 0;

        /// <summary>
        /// Creates a new FATXFilesystem. Finds a device to use.
        /// </summary>
        public FATXFilesystem()
        {
            FATX.FATXDevice dev = new FATX.FATXDevice();
            dev.Open();
            if (!dev.IsValid)
                throw new Exception("No device found.");
            this.device = dev;
            name = "FATX";
            partition = device.Partitions.Count - 1;
            Initialize();
        }

        /// <summary>
        /// Creates a new FATXFilesystem from a given FATXDevice.
        /// </summary>
        /// <param name="device">The device to use for the filesystem.</param>
        public FATXFilesystem(FATX.FATXDevice device)
        {
            this.device = device;
            name = "FATX";
            partition = device.Partitions.Count - 1;
            Initialize();
        }

        /// <summary>
        /// Returns the friendly name of this device.
        /// </summary>
        public override string RootName
        {
            get
            {
                return FriendlyName;
            }
        }

        /// <summary>
        /// Gets the total amount of space for this filesystem.
        /// </summary>
        /// <returns></returns>
        public override long GetTotalSpace()
        {
            return device.GetPartitionSize(partition);
        }

        /// <summary>
        /// Gets the total amount of free space left.
        /// </summary>
        /// <returns></returns>
        public override long GetFreeSpace()
        {
            return device.GetPartitionFreeSpace(partition);
        }

        /// <summary>
        /// Gets the total amount of space used for this filesystem.
        /// </summary>
        /// <returns></returns>
        public override long GetUsedSpace()
        {
            return device.GetPartitionUsedSize(partition);
        }

        /// <summary>
        /// The name to use when displaying this filesytem.
        /// </summary>
        public string FriendlyName
        {
            get
            {
                if (friendlyName != "")
                    return friendlyName;
                if (FileExists("name.txt"))
                {
                    try
                    {
                        friendlyName = System.Text.UnicodeEncoding.BigEndianUnicode.GetString(GetFileData("name.txt"));
                        return friendlyName;
                    }
                    catch { }
                }
                if (device.Type == FATX.DeviceType.Xbox360USBStick)
                    return "USB Stick";
                if (device.Type == FATX.DeviceType.Xbox360HardDrive ||
                    device.Type == FATX.DeviceType.Xbox360HardDriveDevkit)
                    return "Hard Drive";
                if (device.Type == FATX.DeviceType.Xbox360MemoryCard)
                    return "Memory Card";
                return "Storage Device";
            }
            set
            {
                try
                {
                    if (value.Length > 26)
                        value = value.Substring(0, 26);
                    if (value == "")
                        return;
                    SetFileData("name.txt", System.Text.UnicodeEncoding.BigEndianUnicode.GetBytes(value));
                    this.friendlyName = value;
                }
                catch { }
            }
        }
        private string friendlyName = "";

        /// <summary>
        /// Whether or not the device is responding.
        /// </summary>
        /// <returns>True if responsive.</returns>
        public bool IsResponsive()
        {
            try
            {
                if (device.Io.Position > 0)
                {
                    device.Io.Position -= 1;
                    device.Io.Reader.ReadByte();
                }
                else
                {
                    device.Io.Reader.ReadByte();
                    device.Io.Position = 0;
                }
                return true;
            }
            catch { return false; }
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
        /// Determines whether or not a string is a valid entry name.
        /// </summary>
        /// <param name="name">The name to check.</param>
        /// <returns>True if it is a valid entry name.</returns>
        protected override bool IsValidEntryName(string name)
        {
            return FATX.Utilities.IsValidFATXFilename(name);
        }

        public override Entry GetEntry(string path)
        {
            //Xbox.FATXBackend.Dirent dir = device.DirentFind(path, partition);
            FATX.Dirent dir = device.DirentGet(path, partition);
            if (dir == null)
                return null;
            return DirentToEntry(dir, path);
        }
        
        /// <summary>
        /// Returns the path of an entry.
        /// </summary>
        /// <param name="entry">The entry to get the path of.</param>
        /// <returns>The full path as a string.</returns>
        protected override string GetEntryPath(Entry entry)
        {
            FatxEntry entry2 = (FatxEntry)entry;
            return entry2.Path;
        }

        /// <summary>
        /// Deletes a given entry.
        /// </summary>
        /// <param name="entry">The entry to delete.</param>
        /// <returns>Returns true if it was deleted successfully.</returns>
        protected override bool DeleteEntry(Entry entry)
        {
            FatxEntry entry2 = (FatxEntry)entry;
            device.DirentDelete(entry2.dirent, partition);
            return true;
        }

        /// <summary>
        /// Creates a new entry on the filesystem.
        /// </summary>
        /// <param name="path">The path of the entry to create.</param>
        /// <param name="directory">Whether or not it should be a directory.</param>
        /// <returns>True if created successfully.</returns>
        protected override bool CreateEntry(string path, bool directory)
        {
            if (directory)
                device.DirentCreateDirectory(path, partition);
            else
                device.DirentCreate(path, partition);
            return true;
        }

        /// <summary>
        /// Renames an entry to a new name.
        /// </summary>
        /// <param name="entry">The entry to rename.</param>
        /// <param name="newName">The new name of the entry.</param>
        /// <returns>True if renamed successfully.</returns>
        protected override bool RenameEntry(Entry entry, string newName)
        {
            FatxEntry entry2 = (FatxEntry)entry;
            device.DirentRename(entry2.dirent, newName, partition);
            return true;
        }

        /// <summary>
        /// Gets a list of entries belonging to a directory.
        /// </summary>
        /// <param name="entry">The directory entry to look under.</param>
        /// <returns>The list of entries belonging to the directory.</returns>
        protected override List<Entry> GetEntries(Entry entry)
        {
            if (entry != null) // if its not the root
            {
                FatxEntry entry2 = (FatxEntry)entry;
                List<Entry> rtn = new List<Entry>();
                if (!entry2.IsFolder)
                    return rtn;
                List<FATX.Dirent> dirents = device.DirentGetFilesAndFolders(entry2.dirent, partition);
                for (int i = 0; i < dirents.Count; i++)
                    if (dirents[i].IsDirectory)
                        rtn.Add(new FatxDirectoryEntry(entry2.Path, dirents[i]));
                    else
                        rtn.Add(new FatxFileEntry(entry2.Path, dirents[i]));
                return rtn;
            }
            else // if it is the root
            {
                List<Entry> rtn = new List<Entry>();
                List<FATX.Dirent> dirents = device.DirentGetFilesAndFolders("", partition);
                for (int i = 0; i < dirents.Count; i++)
                    if (dirents[i].IsDirectory)
                        rtn.Add(new FatxDirectoryEntry("", dirents[i]));
                    else
                        rtn.Add(new FatxFileEntry("", dirents[i]));
                return rtn;
            }
        }

        /// <summary>
        /// Gets info associated with a specific entry.
        /// </summary>
        /// <param name="entry">The entry to get the info for.</param>
        /// <returns>An EntryExplorerInfo object containing the info.</returns>
        public override StandardFs.EntryExplorerInfo GetEntryExplorerInfo(Entry entry)
        {
            StandardFs.EntryExplorerInfo info = base.GetEntryExplorerInfo(entry);
            return info;
        }

        /// <summary>
        /// Closes this filesystem.
        /// </summary>
        protected override void CloseFilesystem()
        {
            // Close the underlying device
            device.Close();
        }

        /// <summary>
        /// Gets an IO stream associated with an entry at a path.
        /// </summary>
        /// <param name="path">The path of the entry to get.</param>
        /// <param name="mode">The FileMode to use.</param>
        /// <returns>The IO associated with the entry.</returns>
        protected override IO.CoreIO GetEntryStream(string path, System.IO.FileMode mode)
        {
            return new IO.CoreIO(new FATX.DirentStream(device, partition, path, mode), IO.EndianStyle.BigEndian);
        }

        /// <summary>
        /// Converts a FATX dirent into a CoreFilesystem FatxEntry.
        /// </summary>
        /// <param name="dirent">The dirent to convert.</param>
        /// <param name="path">The path of the dirent. Not base path.</param>
        /// <returns>A FatxEntry from the dirent.</returns>
        private FatxEntry DirentToEntry(FATX.Dirent dirent, string path)
        {
            // Remove the name from the path to create the base path
            string basePath = FixPath(path.Substring(0, path.Length - dirent.Name.Length));
            if (dirent.IsDirectory)
                return new FatxDirectoryEntry(basePath, dirent);
            return new FatxFileEntry(basePath, dirent);
        }

    }

    /// <summary>
    /// The entry class for the FATXFilesystem. Uses FATX dirents.
    /// </summary>
    public abstract class FatxEntry : Entry
    {
        public FATX.Dirent dirent;

        public FatxEntry(string basePath, FATX.Dirent dirent)
            : base(basePath)
        {
            this.dirent = dirent;
            this.name = dirent.Name;
            this.modified = dirent.ModifiedTime;
            this.created = dirent.CreateTime;
            this.size = dirent.Size;
        }

    }

    /// <summary>
    /// The FileEntry class for the FATXFilesystem.
    /// </summary>
    public class FatxFileEntry : FatxEntry
    {

        public FatxFileEntry(string basePath, FATX.Dirent dirent)
            : base(basePath, dirent)
        {
        }

        public override EntryType Type
        {
            get { return EntryType.File; }
        }

    }

    /// <summary>
    /// The DirectoryEntry for the FATXFilesystem.
    /// </summary>
    public class FatxDirectoryEntry : FatxEntry
    {

        public FatxDirectoryEntry(string basePath, FATX.Dirent dirent)
            : base(basePath, dirent)
        {

        }

        public override Entry.EntryType Type
        {
            get { return EntryType.Folder; }
        }

    }

}
