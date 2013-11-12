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
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;

namespace xbFsLib.FATX
{

    /// <summary>
    /// The different types of FATX devices.
    /// </summary>
    public enum DeviceType
    {
        Unknown = -1,
        Xbox360MemoryCard = 0,
        Xbox360HardDrive = 1,
        Xbox360HardDriveDevkit = 2,
        Xbox360USBStick = 3
    }

    /// <summary>
    /// A class with the functionality to manipulate a FATX storage device in nearly every way imaginable.
    /// </summary>
    public class FATXDevice
    {
        /// <summary>
        /// The characters the FATX file system doesn't support/allow.
        /// </summary>
        public static readonly char[] InvalidFileNameChars = new[] { '>', '<', '=', '?', ':', ';', '"', '*', '+', ',', '/', '\\', '|' };

        public IO.CoreIO Io { get; set; }
        public List<Partition> Partitions { get; private set; }

        /// <summary>
        /// Path to the storage device.
        /// </summary>
        public string DevicePath { get; private set; }

        /// <summary>
        /// The type of storage device this is.
        /// </summary>
        public DeviceType Type { get; private set; }

        /// <summary>
        /// Determines whether this is a valid FATX storage device.
        /// </summary>
        public bool IsValid { get; private set; }

        /// <summary>
        /// The device id of this device.
        /// </summary>
        public string DeviceId { get { return deviceId; } }
        private string deviceId = "";

        /// <summary>
        /// The physical size of the storage device.
        /// </summary>
        public long DriveSize { get; private set; }

        /// <summary>
        /// Returns the size of the storage size as a formatted string.
        /// </summary>
        public string DriveSizeString
        {
            get { return Utils.SizeToString(DriveSize); }
        }

        /// <summary>
        /// The amount of partitions that are in the storage device.
        /// </summary>
        public int PartitionCount { get { return Partitions.Count; } }

        /// <summary>
        /// Initializes a new instance of the FATX class.
        /// </summary>
        public FATXDevice() { }

        /// <summary>
        /// Initializes a new instance of the FATX class on the specified storage device and opens it.
        /// </summary>
        public FATXDevice(string path) { Open(path); }

        private SafeFileHandle sfh;

        /// <summary>
        /// Opens the first storage device it can find.
        /// </summary>
        public void Open()
        {
            List<string> drives = EnumDrives();
            if (drives.Count > 0)
                Open(drives[0]);
            else
                throw new Exception("No drives found.");
        }

        /// <summary>
        /// Opens a specified storage device.
        /// </summary>
        /// <param name="DevicePath">The location of the storage device.</param>
        public void Open(string DevicePath)
        {

            // Check for USB notation
            if (DevicePath[0] == 'U')
            {
                OpenUSB(new USBStream(DevicePath[1]));
                return;
            }

            // Set some basic stuff
            IsValid = false;
            Type = DeviceType.Unknown;
            this.DevicePath = DevicePath;

            // Open our device
            SafeFileHandle sfh = Windows.Imports.CreateFile(DevicePath,
                Windows.Imports.FileAccess.GenericRead | Windows.Imports.FileAccess.GenericAll,
                Windows.Imports.FileShare.Read | Windows.Imports.FileShare.Write,
                IntPtr.Zero, Windows.Imports.CreationDisposition.OpenExisting,
                Windows.Imports.FileAttributes.Normal, IntPtr.Zero);
            this.sfh = sfh;

            // Create a stream to read with
            FileStream fs = new FileStream(sfh, FileAccess.ReadWrite, 0x200, false);
            Io = new IO.CoreIO(fs, IO.EndianStyle.BigEndian);
            Io.Open();

            // Get our device size
            DriveSize = Windows.Imports.GetDriveSize(sfh);

            // Figure out what type of device this is
            Type = GetDeviceType(Io);

            // Now we know what layout to use
            LoadPartitionLayout();

            // Now we are done with the basic stuff
            IsValid = true;
        }

        /// <summary>
        /// Opens a specified USB storage device using a USBStream.
        /// </summary>
        /// <param name="DeviceStream">The USBStream for the device.</param>
        public void OpenUSB(USBStream DeviceStream)
        {
            // Set some basic stuff
            IsValid = false;
            DevicePath = "U" + DeviceStream.DriveLetter;
            Type = DeviceType.Xbox360USBStick;
            deviceId = DeviceStream.DeviceId;

            // Create a stream to read with
            Io = new IO.CoreIO(DeviceStream, IO.EndianStyle.BigEndian);
            Io.Open();

            // Get our device size
            DriveSize = DeviceStream.Length;

            // Now we know what layout to use
            LoadPartitionLayout();

            // Now we are done with the basic stuff
            IsValid = true;
        }

        /// <summary>
        /// Closes the storage device.
        /// </summary>
        public void Close() { Close(false); }

        /// <summary>
        /// Closes the storage device, but keeps some device information.
        /// </summary>
        /// <param name="KeepBasicInfo">Keep the basic information?</param>
        public void Close(bool KeepBasicInfo)
        {
            // Close our device
            Io.Close();

            // Now lets clear some stuff
            if (!KeepBasicInfo)
            {
                DevicePath = "";
                Io = null;
                Type = DeviceType.Unknown;
                Partitions = null;
                DriveSize = 0;
                IsValid = false;
            }
        }

        private void LoadPartitionLayout()
        {
            // Load our basic layout, we will calculate our sizes later
            Partitions = new List<Partition>();
            switch (Type)
            {
                case DeviceType.Xbox360MemoryCard:
                    Partitions.Add(new Partition(this, "Cache", 0x00, 0x7ff000));
                    Partitions.Add(new Partition(this, "Data", 0x7ff000));
                    break;
                case DeviceType.Xbox360HardDrive:
                case DeviceType.Xbox360HardDriveDevkit:
                    Partitions.Add(new Partition(this, "Dump", 0x80000, 0x80000000));
                    Partitions.Add(new Partition(this, "Windows", 0x80080000, 0xA0E30000));
                    Partitions.Add(new Partition(this, "System", 0x120eb0000, 0x10000000));
                    if (Type == DeviceType.Xbox360HardDriveDevkit)
                    {
                        // Lets read our partition table
                        Io.Position = 8;
                        int index = 0;
                        while (true)
                        {
                            // Read our index and count
                            uint sectorIndex = Io.Reader.ReadUInt32();
                            uint sectorCount = Io.Reader.ReadUInt32();

                            // Make sure we are valid
                            if (sectorIndex == 0) break;

                            // Add the partiton
                            Partition part = new Partition(
                                this, "DevkitPartition" + index,
                                (long)sectorIndex * 0x200, (long)sectorCount * 0x200);
                            Partitions.Add(part);
                            index++;
                        }
                    }
                    else
                        Partitions.Add(new Partition(this, "Data", 0x130eb0000));
                    break;
                case DeviceType.Xbox360USBStick:
                    //Partitions.Add(new Partition(this, "Data", 0x00));
                    Partitions.Add(new USBPartition(this, "Data", 0x00));
                    break;
                default:
                    throw new Exception("Device type not supported!");
            }

            // Sort our partitions
            Partitions.Sort();

            // We must now calculate the last "data" partition size
            if (DriveSize == 0x04ab440c00)
                Partitions[Partitions.Count - 1].Size = 0x377FFC000; // 20gb HDD
            else if (Type != DeviceType.Xbox360HardDriveDevkit)
                Partitions[Partitions.Count - 1].Size = DriveSize -
                    Partitions[Partitions.Count - 1].Offset;

            // Now lets read each partition
            for (int x = 0; x < Partitions.Count; x++)
            {
                Partitions[x].Read();
                if (Partitions[x].IsValid) continue;

                // Remove if the partition is invalid
                Partitions.RemoveAt(x);
                x--;
            }
        }

        /// <summary>
        /// Gets the size of the specified partition.
        /// </summary>
        /// <param name="Partition">Partition to calculate the size of.</param>
        /// <returns>Returns the size of the specified partition as a long.</returns>
        public long GetPartitionSize(int Partition)
        {
            return Partitions[Partition].Size;
        }

        /// <summary>
        /// Gets the size of the specified partition.
        /// </summary>
        /// <param name="Partition">Partition to calculate the size of.</param>
        /// <returns>Returns the size of the specified partition as a formatted string.</returns>
        public string GetPartitionSizeString(int Partition)
        {
            return Utils.SizeToString(GetPartitionSize(Partition));
        }

        /// <summary>
        /// Gets the amount of free space on the specified partition.
        /// </summary>
        /// <param name="Partition">Partition to calculate the free space of.</param>
        /// <returns>Returns the amount of free space on the specified partition as a long.</returns>
        public long GetPartitionFreeSpace(int Partition)
        {
            // Get how many clusters we have
            uint freeClusterCount = Partitions[Partition].GetFreeClusterCount();
            return freeClusterCount * Partitions[Partition].ClusterSize;
        }

        /// <summary>
        /// Gets the amount of space left on the specified partition.
        /// </summary>
        /// <param name="Partition">Partition to calculate the free space of.</param>
        /// <returns>Returns the amount of free space on the specified partition as a long.</returns>
        public string GetPartitionFreeSpaceString(int Partition)
        {
            return Utils.SizeToString(GetPartitionFreeSpace(Partition));
        }

        /// <summary>
        /// Gets the amount of space used on the specified partition.
        /// </summary>
        /// <param name="Partition">Partition to calculate the used space of.</param>
        /// <returns>Returns the amount of space used as a long.</returns>
        public long GetPartitionUsedSize(int Partition)
        {
            return GetPartitionSize(Partition) - GetPartitionFreeSpace(Partition);
        }

        /// <summary>
        /// Gets the amount of space used on the specified partition.
        /// </summary>
        /// <param name="Partition">Partition to calculate the used space of.</param>
        /// <returns>Returns the amount of space used as a formatted string.</returns>
        public string GetPartitionUsedSizeString(int Partition)
        {
            return Utils.SizeToString(GetPartitionUsedSize(Partition));
        }

        /// <summary>
        /// Dumps the Security Sector to the specified location.
        /// </summary>
        /// <param name="FilePath">Location to save the Security Sector.</param>
        public void DumpSecuritySector(string FilePath)
        {
            // Create our output file
            IO.CoreIO tempIo = new IO.CoreIO(FilePath, IO.EndianStyle.BigEndian);
            tempIo.Open(FileMode.Create);

            // Seek to and read our sector (16) as well as write
            Io.Position = 16 * 0x200;
            tempIo.Writer.Write(Io.Reader.ReadBytes(0xE000));

            // Close our file
            tempIo.Close();
        }

        #region Identification

        private static DeviceType GetDeviceType(string devicePath)
        {
            // Open our device
            SafeFileHandle tempSfh = Windows.Imports.CreateFile(devicePath,
                Windows.Imports.FileAccess.GenericRead | Windows.Imports.FileAccess.GenericWrite,
                Windows.Imports.FileShare.Read | Windows.Imports.FileShare.Write,
                IntPtr.Zero, Windows.Imports.CreationDisposition.OpenExisting,
                Windows.Imports.FileAttributes.Normal, IntPtr.Zero);

            // Create a stream to read with
            FileStream fs = new FileStream(tempSfh, FileAccess.ReadWrite);
            IO.CoreIO tempIo = new IO.CoreIO(fs, IO.EndianStyle.BigEndian);
            tempIo.Open();

            DeviceType devType = GetDeviceType(tempIo);

            // Close our device
            tempIo.Close();

            // Return our device type
            return devType;
        }

        private static DeviceType GetDeviceType(IO.CoreIO io)
        {
            // Figure out what type of device this is
            io.Position = 0;
            if (io.Reader.ReadInt32() == 0x58544146)
            {
                io.Position = 0x7ff000;
                if (io.Reader.ReadInt32() == 0x58544146)
                {
                    return DeviceType.Xbox360MemoryCard;
                }
                else
                {
                    return DeviceType.Xbox360USBStick;
                }
            }

            io.Position = 0x80000;
            if (io.Reader.ReadInt32() == 0x58544146)
            {
                // Its a HDD but lets check for devkit
                io.Position = 0;
                if (io.Reader.ReadInt32() == 0x020000)
                    return DeviceType.Xbox360HardDriveDevkit;

                // Its not
                return DeviceType.Xbox360HardDrive;
            }

            // We arent sure what this is
            return DeviceType.Unknown;
            //return DeviceType.Xbox360HardDrive;
        }

        /// <summary>
        /// Gets the amount of storage devices connected to this PC.
        /// </summary>
        /// <returns>Returns the list of storage devices.</returns>
        public static List<string> EnumDrives()
        {
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += worker_DoWork;
            worker.RunWorkerAsync();
            while (worker.IsBusy)
            {
                System.Windows.Forms.Application.DoEvents();
            }
            return workerAnswer;

        }
        private static List<string> workerAnswer = new List<string>();
        private static void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            workerAnswer = new List<string>();

            // Create our list
            List<string> drives = new List<string>();

            // Get our usb drives and denote them with a U
            string[] usbs = Environment.GetLogicalDrives();
            for (int i = 0; i < usbs.Length; i++)
            {
                if (!Directory.Exists(usbs[i] + "Xbox360"))
                    continue;
                if (!File.Exists(usbs[i] + "Xbox360\\Data0000"))
                    continue;
                if (!File.Exists(usbs[i] + "Xbox360\\Data0001"))
                    continue;
                drives.Add("U" + usbs[i][0]);
            }

            // Loop through 10 devices
            for (int x = 0; x < 10; x++)
            {
                try
                {
                    // Try and get the device path
                    string path = string.Format(@"\\.\PHYSICALDRIVE{0:D}", x);
                    if (GetDeviceType(path) != DeviceType.Unknown)
                        drives.Add(path);
                }
                catch { }
            }

            // Return our paths
            workerAnswer = drives;
        }

        /// <summary>
        /// Gets one storage device connected to this PC.
        /// </summary>
        /// <returns>Returns the storage device path.</returns>
        public static string EnumDrivesGetFirst()
        {
            List<String> ans = EnumDrives();
            if (ans.Count <= 0)
                return string.Empty;

            return ans[0];
        }

        #endregion

        #region Dirent

        /// <summary>
        /// Gets a dirent in the specified path.
        /// </summary>
        /// <param name="DirentPath">Location of the dirent.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        /// <param name="Directory">Are you looking for a directory?</param>
        /// <returns>Returns the dirent found.</returns>
        public Dirent DirentGet(string DirentPath, int Partition, bool Directory)
        {
            return DirentGet(DirentPath, Partition, Directory,
                Partitions[Partition].RootDirFirstCluster);
        }

        public Dirent DirentGet(string DirentPath, int Partition)
        {
            return DirentGet(DirentPath, Partition,
                Partitions[Partition].RootDirFirstCluster);
        }

        private Dirent DirentGet(string DirentPath, int Partition, bool Directory, uint Cluster)
        {
            // Lets split our path
            string[] split = DirentPath.Split('\\');

            // Lets get our directory
            List<Dirent> dirents = Partitions[Partition].ReadDirectory(Cluster);

            // Now lets find our first file
            for (int x = 0; x < dirents.Count; x++)
            {
                // If its deleted lets skip
                if (dirents[x].IsDeletedDirent) continue;

                // check if this is what we wanted
                if (split.Length == 1)
                {
                    if (dirents[x].IsDirectory == Directory &&
                        dirents[x].Name == split[0])
                        return dirents[x];
                }
                else
                {
                    // This isnt the last file so we gotta check our directory
                    if (dirents[x].IsDirectory && dirents[x].Name == split[0])
                        return DirentGet(string.Join("\\", split, 1, split.Length - 1),
                                         Partition, Directory, dirents[x].FirstCluster);
                }
            }

            // Hmmm we didn't find it lets return null
            return null;
        }


        private Dirent DirentGet(string DirentPath, int Partition, uint Cluster)
        {
            // Lets split our path
            string[] split = DirentPath.Split('\\');

            // Lets get our directory
            List<Dirent> dirents = Partitions[Partition].ReadDirectory(Cluster);

            // Now lets find our first file
            for (int x = 0; x < dirents.Count; x++)
            {
                // If its deleted lets skip
                if (dirents[x].IsDeletedDirent) continue;

                // check if this is what we wanted
                if (split.Length == 1)
                {
                    if (dirents[x].Name == split[0])
                        return dirents[x];
                }
                else
                {
                    // This isnt the last file so we gotta check our directory
                    if (dirents[x].IsDirectory && dirents[x].Name == split[0])
                        return DirentGet(string.Join("\\", split, 1, split.Length - 1),
                                         Partition, dirents[x].FirstCluster);
                }
            }

            // Hmmm we didn't find it lets return null
            return null;
        }

        /// <summary>
        /// Find a dirent in the specified path.
        /// </summary>
        /// <param name="DirentPath">Location of the dirent.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        /// <param name="Directory">Are you looking for a directory?</param>
        /// <returns>Returns the dirent found.</returns>
        public Dirent DirentFind(string DirentPath, int Partition, bool Directory)
        {
            // Find the file
            return DirentGet(DirentPath, Partition, Directory);
        }

        /// <summary>
        /// Determines if a dirent is in the specified path.
        /// </summary>
        /// <param name="DirentPath">Location of the dirent.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        /// <returns>Returns true if the dirent is found.</returns>
        public bool DirentFileExists(string DirentPath, int Partition)
        {
            return DirentFind(DirentPath, Partition, false) != null;
        }

        /// <summary>
        /// Determines if a directory is in the specified path.
        /// </summary>
        /// <param name="DirectoryPath">Location of the dirent.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        /// <returns>Returns true if the directory is found.</returns>
        public bool DirentDirectoryExists(string DirectoryPath, int Partition)
        {
            return DirentFind(DirectoryPath, Partition, true) != null;
        }

        /// <summary>
        /// Gets all of the files in the specified path.
        /// </summary>
        /// <param name="DirectoryPath">Location of the directory to find the files in.</param>
        /// <param name="Partition">Partition the directory is on.</param>
        /// <returns>Returns the files found as a list.</returns>
        public List<Dirent> DirentGetFiles(string DirectoryPath, int Partition)
        {
            // If we want our root lets get it
            if (DirectoryPath == "\\" || DirectoryPath == "")
                return DirentGetFiles(
                    Partitions[Partition].RootDirFirstCluster, Partition);

            // Now we must find our dirent
            Dirent temp = DirentFind(DirectoryPath, Partition, true);
            if (temp == null)
                return new List<Dirent>();

            return DirentGetFiles(temp.FirstCluster, Partition);
        }

        /// <summary>
        /// Gets all of the files in the specified dirent.
        /// </summary>
        /// <param name="Dirent">Dirent to find the files in.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        /// <returns>Returns the files found as a list.</returns>
        public List<Dirent> DirentGetFiles(Dirent Dirent, int Partition)
        {
            return DirentGetFiles(Dirent.FirstCluster, Partition);
        }

        private List<Dirent> DirentGetFiles(uint Cluster, int Partition)
        {
            // Create our initial list
            List<Dirent> dirents = new List<Dirent>();

            // Get all the files within this dirent
            List<Dirent> tempDirents =
                Partitions[Partition].ReadDirectory(Cluster);

            // Loop through all the dirents
            for (int x = 0; x < tempDirents.Count; x++)
                if (!tempDirents[x].IsDeletedDirent && tempDirents[x].IsFile)
                    dirents.Add(tempDirents[x]);

            // Return what we have
            return dirents;
        }

        /// <summary>
        /// Gets all of the directories in the specified path.
        /// </summary>
        /// <param name="DirectoryPath">Location of the directory to find the directories in.</param>
        /// <param name="Partition">Partition the directory is on.</param>
        /// <returns>Returns the directories found as a list.</returns>
        public List<Dirent> DirentGetFolders(string DirectoryPath, int Partition)
        {
            // If we want our root lets get it
            if (DirectoryPath == "\\" || DirectoryPath == "")
                return DirentGetFolders(
                    Partitions[Partition].RootDirFirstCluster, Partition);

            // Now we must find our dirent
            Dirent temp = DirentFind(DirectoryPath, Partition, true);
            if (temp == null)
                return new List<Dirent>();

            return DirentGetFolders(temp.FirstCluster, Partition);
        }

        /// <summary>
        /// Gets all of the directories in the specified dirent.
        /// </summary>
        /// <param name="Dirent">Dirent to find the directories in.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        /// <returns>Returns the directories found as a list.</returns>
        public List<Dirent> DirentGetFolders(Dirent Dirent, int Partition)
        {
            return DirentGetFolders(Dirent.FirstCluster, Partition);
        }

        private List<Dirent> DirentGetFolders(uint cluster, int partition)
        {
            // Create our initial list
            List<Dirent> dirents = new List<Dirent>();

            // Get all the files within this dirent
            List<Dirent> tempDirents =
                Partitions[partition].ReadDirectory(cluster);

            // Loop through all the dirents
            for (int x = 0; x < tempDirents.Count; x++)
                if (!tempDirents[x].IsDeletedDirent && tempDirents[x].IsDirectory)
                    dirents.Add(tempDirents[x]);

            // Return what we have
            return dirents;
        }

        /// <summary>
        /// Gets all of the files and directories in the specified path.
        /// </summary>
        /// <param name="DirectoryPath">Directory to find the files and directories in.</param>
        /// <param name="Partition">Partition the directory is on.</param>
        /// <returns>Returns the files and directories found as a list.</returns>
        public List<Dirent> DirentGetFilesAndFolders(string DirectoryPath, int Partition)
        {
            // If we want our root lets get it
            if (DirectoryPath == "\\" || DirectoryPath == "")
                return DirentGetFilesAndFolders(
                    Partitions[Partition].RootDirFirstCluster, Partition);

            // Now we must find our dirent
            Dirent temp = DirentFind(DirectoryPath, Partition, true);
            if (temp == null)
                return new List<Dirent>();

            return DirentGetFilesAndFolders(temp.FirstCluster, Partition);
        }

        /// <summary>
        /// Gets all of the files and directories in the specified dirent.
        /// </summary>
        /// <param name="Dirent">Dirent to find the files and directories in.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        /// <returns>Returns the files and directories found as a list.</returns>
        public List<Dirent> DirentGetFilesAndFolders(Dirent Dirent, int Partition)
        {
            return DirentGetFilesAndFolders(Dirent.FirstCluster, Partition);
        }

        private List<Dirent> DirentGetFilesAndFolders(uint cluster, int partition)
        {
            // Create our initial list
            List<Dirent> dirents = new List<Dirent>();

            // Get all the files within this dirent
            List<Dirent> tempDirents =
                Partitions[partition].ReadDirectory(cluster);

            // Loop through all the dirents
            for (int x = 0; x < tempDirents.Count; x++)
                if (!tempDirents[x].IsDeletedDirent)
                    dirents.Add(tempDirents[x]);

            // Return what we have
            return dirents;
        }

        /// <summary>
        /// Moves a file or folder into a new location.
        /// </summary>
        /// <param name="direntToMove">The dirent thats going to be moved.</param>
        /// <param name="DirectoryPath">The path to move the dirent to.</param>
        /// <param name="Partition">The partition the path is on.</param>
        public void DirentMove(Dirent direntToMove, string DirectoryPath, int Partition)
        {
            // Check if its moving to the root
            if (DirectoryPath == "\\" || DirectoryPath == "")
            {
                Partitions[Partition].MoveDirent(Partitions[Partition].RootDirFirstCluster, direntToMove);
                return;
            }

            // See if this directory exists
            Dirent moveTo = DirentFind(DirectoryPath, Partition, true);
            if (moveTo == null)
                moveTo = DirentCreateDirectory(DirectoryPath, Partition);

            // We tried creating it so lets check again
            if (moveTo == null)
                return;

            // Lets pass this on
            DirentMove(direntToMove, moveTo, Partition);
        }

        /// <summary>
        /// Moves a file or folder into a new location.
        /// </summary>
        /// <param name="direntToMove">The dirent thats going to be moved.</param>
        /// <param name="direntToMoveTo">The directory dirent to move to.</param>
        /// <param name="Partition">The partition the dirents are on.</param>
        public void DirentMove(Dirent direntToMove, Dirent direntToMoveTo, int Partition)
        {
            // Make sure we were passed good dirents
            if (!direntToMoveTo.IsDirectory)
                return;

            // Make the partition do the work
            Partitions[Partition].MoveDirent(direntToMoveTo.FirstCluster, direntToMove);

        }

        /// <summary>
        /// Creates a directory in the specified path.
        /// </summary>
        /// <param name="DirectoryPath">Path to create the directory in.</param>
        /// <param name="Partition">Partition the path is on.</param>
        /// <returns>Returns the directory created.</returns>
        public Dirent DirentCreateDirectory(string DirectoryPath, int Partition)
        {
            // Make sure this directory dosent exist
            Dirent newDirent = DirentFind(DirectoryPath, Partition, true);
            if (newDirent != null)
                return newDirent;

            // It doesnt so lets create
            if (DirectoryPath.Length > 0)
                if (DirectoryPath[DirectoryPath.Length - 1] == '\\') DirectoryPath = DirectoryPath.Remove(DirectoryPath.Length - 1, 1);
            string[] split = DirectoryPath.Split('\\');
            uint currentCluster = Partitions[Partition].RootDirFirstCluster;
            for (int x = 0; x < split.Length; x++)
            {
                // Lets create this dirent if it doesnt exist
                string tempPath = string.Join("\\", split, 0, x + 1);
                newDirent = DirentFind(tempPath, Partition, true) ??
                    Partitions[Partition].CreateDirent(currentCluster, split[x], true);
                currentCluster = newDirent.FirstCluster;
            }

            // Hopefully it worked
            return newDirent;
        }

        internal Dirent DirentCreate(string filePath, int partition)
        {
            // Lets get our directory
            string[] split = filePath.Split('\\');

            uint directoryCluster = Partitions[partition].RootDirFirstCluster;
            if (split.Length > 1)
            {
                string directoryPath = string.Join("\\", split, 0, split.Length - 1);
                Dirent directory = DirentFind(directoryPath, partition, true) ??
                    DirentCreateDirectory(directoryPath, partition);
                directoryCluster = directory.FirstCluster;
            }

            // Now lets create our file within it
            return Partitions[partition].CreateDirent(
                directoryCluster, split[split.Length - 1], false);
        }



        /// <summary>
        /// Deletes a directory in the specified path.
        /// </summary>
        /// <param name="DirectoryPath">Location of the directory to delete.</param>
        /// <param name="Partition">Partition the directory is on.</param>
        public void DirentDeleteDirectory(string DirectoryPath, int Partition)
        {
            if (DirectoryPath == "\\" || DirectoryPath == "")
                throw new Exception("You cannot delete the Root Directory");

            // Get our directory dirent
            Dirent dirent = DirentFind(DirectoryPath, Partition, true);
            if (dirent == null)
                throw new Exception("Directory not found");

            // Now lets call our delete method
            DirentDeleteDirectory(dirent, Partition);
        }

        private void DirentDeleteDirectory(Dirent dirent, int partition)
        {
            // Get all our files and folders and delete
            List<Dirent> dirents = DirentGetFilesAndFolders(dirent.FirstCluster, partition);
            for (int x = 0; x < dirents.Count; x++)
                if (dirents[x].IsDirectory)
                    DirentDeleteDirectory(dirents[x], partition);
                else
                    DirentDelete(dirents[x], partition);

            // Now delete ourself
            DirentDelete(dirent, partition);
        }

        /// <summary>
        /// Deletes a dirent in the specified path.
        /// </summary>
        /// <param name="DirentPath">Location of the dirent to delete.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        public void DirentDelete(string DirentPath, int Partition)
        {
            Dirent dirent = DirentFind(DirentPath, Partition, false);
            if (dirent == null)
                throw new Exception("File not found");

            DirentDelete(dirent, Partition);
        }

        /// <summary>
        /// Deletes a dirent.
        /// </summary>
        /// <param name="Dirent">Dirent to delete.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        public void DirentDelete(Dirent Dirent, int Partition)
        {
            try
            {
                Partitions[Partition].FreeChain(Dirent.FirstCluster);
            }
            catch { }
            Dirent.Delete();
            DirentUpdate(Dirent, Partition);
        }

        /// <summary>
        /// Renames the specified dirent.
        /// </summary>
        /// <param name="DirentPath">Location of the dirent to rename.</param>
        /// <param name="NewName">The name to rename the dirent to.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        public void DirentRename(string DirentPath, string NewName, int Partition)
        {
            // Find our dirent first
            Dirent dirent = DirentFind(DirentPath, Partition, false);
            if (dirent == null)
                throw new Exception("File not found");

            // Now lets rename it
            DirentRename(dirent, NewName, Partition);
        }

        /// <summary>
        /// Renames the specified directory.
        /// </summary>
        /// <param name="DirectoryPath">Location of the directory to rename.</param>
        /// <param name="NewName">The name to rename the directory to.</param>
        /// <param name="Partition">Partition the directory is on.</param>
        public void DirentRenameDirectory(string DirectoryPath, string NewName, int Partition)
        {
            // Find our dirent first
            Dirent dirent = DirentFind(DirectoryPath, Partition, true);
            if (dirent == null)
                throw new Exception("Directory not found");

            // Now lets rename it
            DirentRename(dirent, NewName, Partition);
        }

        /// <summary>
        /// Renames the specified directory.
        /// </summary>
        /// <param name="Dirent">Dirent to rename.</param>
        /// <param name="NewName">The name to rename the dirent to.</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        public void DirentRename(Dirent Dirent, string NewName, int Partition)
        {
            // Set new filename
            Dirent.Name = NewName;

            // Set times
            Dirent.AccessTime = DateTime.Now;
            Dirent.ModifiedTime = DateTime.Now;

            // Now lets update our dirent
            DirentUpdate(Dirent, Partition);
        }

        /// <summary>
        /// Updates the specified dirent. (Assuming you changes its properties.) - Eaton
        /// </summary>
        /// <param name="Dirent">Dirent to update</param>
        /// <param name="Partition">Partition the dirent is on.</param>
        public void DirentUpdate(Dirent Dirent, int Partition)
        {
            Partitions[Partition].UpdateDirent(Dirent);
        }

        #endregion

        #region File Streaming

        public byte[] FileReadAllBytes(string filePath, int partition)
        {
            IO.CoreIO tio = FileGetIo(filePath, partition); tio.Open();
            byte[] data = tio.Reader.ReadBytes((int)tio.Stream.Length);
            tio.Close();
            return data;
        }
        public byte[] FileReadAllBytes(Dirent dirent, int partition)
        {
            IO.CoreIO tio = FileGetIo(dirent, partition); tio.Open();
            byte[] data = tio.Reader.ReadBytes((int)tio.Stream.Length);
            tio.Close();
            return data;
        }

        public IO.CoreIO FileGetIo(string filePath, int partition)
        {
            return FileGetIo(filePath, partition, IO.EndianStyle.BigEndian);
        }
        public IO.CoreIO FileGetIo(string filePath, int partition, IO.EndianStyle endianType)
        {
            return new IO.CoreIO(FileStreamGet(filePath, partition), endianType);
        }
        public IO.CoreIO FileGetIo(Dirent dirent, int partition)
        {
            return FileGetIo(dirent, partition, IO.EndianStyle.BigEndian);
        }
        public IO.CoreIO FileGetIo(Dirent dirent, int partition, IO.EndianStyle endianType)
        {
            return new IO.CoreIO(FileStreamGet(dirent, partition), endianType);
        }

        public DirentStream FileStreamGet(Dirent dirent, int partition)
        {
            return new DirentStream(this, partition, dirent, FileMode.Open);
        }
        public DirentStream FileStreamGet(string filePath, int partition)
        {
            return new DirentStream(this, partition, filePath, FileMode.Open);
        }
        public DirentStream FileStreamCreate(string filePath, int partition)
        {
            return FileStreamCreate(filePath, FileMode.Create, partition);
        }
        public DirentStream FileStreamCreate(string filePath, FileMode fileMode, int partition)
        {
            return new DirentStream(this, partition, filePath, fileMode);
        }

        #endregion
    }
}