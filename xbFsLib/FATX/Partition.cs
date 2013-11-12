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
    /// A partition for a FATX device.
    /// </summary>
    public class Partition : IComparable<Partition>
    {
        // Basic partition info
        protected readonly FATXDevice fatx;
        public readonly string friendlyName;
        protected long offset, size;
        public long Offset
        {
            get { return offset; }
            set { offset = value; }
        }
        public long Size
        {
            get { return size; }
            set { size = value; }
        }
        public long FileAreaSize { get { return (offset + size) - fileAreaOffset; } }

        // This is actually in the partition data
        protected uint magic, id, sectorsPerCluster, rootDirFirstCluster;
        public uint Magic { get { return magic; } }
        public uint ID { get { return id; } }
        public bool IsValid { get { return magic == 0x58544146; } }

        // Basic cluster stuff
        protected uint clusterSize, clusterCount;
        internal uint ClusterSize { get { return clusterSize; } }

        // File info
        protected long chainMapOffset, fileAreaOffset;

        // Chainmap stuff
        protected uint[] chainMap;
        protected int chainMapEntrySize;
        public int xchainMapEntrySize
        {
            get { return chainMapEntrySize; }
        }
        protected uint chainMapSize;

        // Dirent caching
        protected uint direntsPerCluster, direntCacheClusterIndex;
        protected List<Dirent> rootDir, direntCache;
        public uint RootDirFirstCluster { get { return rootDirFirstCluster; } }

        public Partition(FATXDevice fatx, string friendlyName, long offset)
        {
            this.fatx = fatx;
            this.friendlyName = friendlyName;
            this.offset = offset;
        }

        public Partition(FATXDevice fatx, string friendlyName, long offset, long size)
        {
            this.fatx = fatx;
            this.friendlyName = friendlyName;
            this.offset = offset;
            this.size = size;
        }

        /// <summary>
        /// Reads the information of the partition into memory and reads
        /// the chainmap as well.
        /// </summary>
        public void Read()
        {
            Read(true);
        }

        /// <summary>
        /// Reads the information of the partition into memory.
        /// </summary>
        /// <param name="readChainMap">Whether or not to read the chainmap.</param>
        public virtual void Read(bool readChainMap)
        {
            // Read the partition header
            fatx.Io.Position = offset;
            magic = fatx.Io.Reader.ReadUInt32();

            // If its not valid lets stop here
            // No more exception thorwing so we can handle elsewhere
            if (!IsValid)
                return;

            // Valid partition lets continue
            id = fatx.Io.Reader.ReadUInt32();
            sectorsPerCluster = fatx.Io.Reader.ReadUInt32();
            rootDirFirstCluster = fatx.Io.Reader.ReadUInt32();

            // Work out cluster size and count
            clusterSize = sectorsPerCluster * 0x200;
            clusterCount = (uint)(size / clusterSize);

            // Work out Chain Map size, count, offset
            chainMapEntrySize = clusterCount < 0xfff0 ? 2 : 4;
            chainMapSize = (clusterCount + 1) * (uint)chainMapEntrySize;
            chainMapSize = Utilities.RoundToPages(chainMapSize);
            chainMapOffset = offset + 0x1000;

            // Set the File Area Offset
            fileAreaOffset = chainMapOffset + chainMapSize;
            direntsPerCluster = clusterSize / 0x40;

            // Read our chainmap
            if (readChainMap)
                ReadChainMap();
        }

        /// <summary>
        /// Reads the chain map from the io.
        /// </summary>
        public void ReadChainMap()
        {
            // Read our chainmap
            chainMap = new uint[clusterCount];
            fatx.Io.Position = chainMapOffset;

            // Create a temp buffer to read from
            IO.CoreIO io = new IO.CoreIO(fatx.Io.Reader.ReadBytes((int)chainMapSize),
                IO.EndianStyle.BigEndian);
            io.Open();

            // Read our chainmap
            for (int x = 0; x < clusterCount; x++)
                chainMap[x] = (chainMapEntrySize == 2) ?
                    io.Reader.ReadUInt16() : io.Reader.ReadUInt32();

            // Close our temp buffer
            io.Close();

            // Fix our chainmap
            if (chainMapEntrySize == 2)
                for (int x = 0; x < clusterCount; x++)
                    if ((chainMap[x] & 0xFFF0) == 0xFFF0)
                        chainMap[x] |= 0xFFFF0000;
        }

        /// <summary>
        /// Writes the stored chainmap out of memory and onto the filesystem.
        /// </summary>
        public void WriteChainMap()
        {
            // Create a temp buffer to write to
            IO.CoreIO io = new IO.CoreIO(new byte[chainMapSize],
                IO.EndianStyle.BigEndian);
            io.Open(); io.Position = 0;

            // Write it
            for (int x = 0; x < clusterCount; x++)
                if (chainMapEntrySize == 2)
                    io.Writer.Write((ushort)chainMap[x]);
                else
                    io.Writer.Write(chainMap[x]);

            // Now move to the chainmap and write it
            fatx.Io.Position = chainMapOffset;
            fatx.Io.Writer.Write(io.ToArray());
        }

        /// <summary>
        /// Frees a chain starting at the cluster given.
        /// </summary>
        /// <param name="startingCluster">The cluster to start freeing from.</param>
        public void FreeChain(uint startingCluster)
        {
            FreeChain(startingCluster, 0xFFFFFFFF, false, true);
        }

        /// <summary>
        /// Frees a chain starting at the cluster given, up to a certain count.
        /// </summary>
        /// <param name="startingCluster">The cluster to srart freeing from.</param>
        /// <param name="count">The amount of chain map entries to free.</param>
        /// <param name="markFirstAsLast">Whether or not to mark as last.</param>
        /// <param name="writeChainMap">Whether or not to write the chain map.</param>
        public void FreeChain(uint startingCluster, uint count, bool markFirstAsLast, bool writeChainMap)
        {
            uint x = 0, nextCluster;

            // Figure out if we are gonna mark this first cluster as the last
            if (markFirstAsLast)
            {
                nextCluster = GetNextCluster(startingCluster);
                SetNextCluster(startingCluster, 0xFFFFFFFF, false, false);
                startingCluster = nextCluster;
                x++;// Deecrease our count
            }

            // If this is the last lets just stop
            if (startingCluster == 0xFFFFFFFF)
                return;

            //Loop and free the rest
            for (; x < count; x++)
            {
                // Get our next cluster before we null this one
                if ((nextCluster = GetNextCluster(startingCluster)) == 0xFFFFFFFF)
                    if (x != (count - 1) && count != 0xFFFFFFFF)
                        throw new Exception("SetDirentLength: Bad next Cluster");
                    else
                    {
                        SetNextCluster(startingCluster, 0, false, false);
                        break;
                    }

                // Set our current to free and set our next one
                SetNextCluster(startingCluster, 0, false, false);
                startingCluster = nextCluster;
            }

            // Write our chainmap if we must
            if (writeChainMap)
                WriteChainMap();
        }

        /// <summary>
        /// Reads a directory given the directory's cluster.
        /// </summary>
        /// <param name="cluster">The directory's cluster.</param>
        /// <returns>Returns a list of dirents listed in the directory.</returns>
        public List<Dirent> ReadDirectory(uint cluster)
        {
            // Check to see if this is the same and read from our cache
            if (direntCacheClusterIndex == cluster && direntCache != null)
                return direntCache;

            // If this is our root lets return that
            if (cluster == rootDirFirstCluster && rootDir != null)
                return rootDir;

            // Set our cluster
            direntCacheClusterIndex = cluster;

            // Read our directory
            direntCache = new List<Dirent>();

            // Make sure its a good cluster
            if (direntCacheClusterIndex > clusterCount) return direntCache;
            bool done = false; uint currentCluster = direntCacheClusterIndex;
        Continue:
            SeekToCluster(currentCluster);
            for (int x = 0; x < direntsPerCluster; x++)
            {
                Dirent dirent = new Dirent();
                dirent.Read(fatx.Io.Reader);
                dirent.DirentIndex = x;
                dirent.ParentCluster = cluster;
                if (dirent.IsValidEntry)
                    direntCache.Add(dirent);
                else
                {
                    done = true;
                    break; // Stop since we are done with the valid ones
                }
            }

            // If we arent done lets continue
            if (!done)
                if ((currentCluster = GetNextCluster(currentCluster)) != 0xFFFFFFFF)
                    goto Continue;

            // if this is our root lets set it
            if (cluster == rootDirFirstCluster && rootDir == null)
                rootDir = direntCache;

            // Return our cache
            return direntCache;
        }

        /// <summary>
        /// Updates the information of a dirent onto the filesystem.
        /// </summary>
        /// <param name="dirent">The dirent to update.</param>
        public void UpdateDirent(Dirent dirent)
        {
            // Just so we can get to a specific position within the cluster
            IO.CoreIO io = new IO.CoreIO(
                new MemoryStream(ReadCluster(dirent.ParentCluster)),
                IO.EndianStyle.BigEndian);
            io.Open(); io.Position = (0x40 * dirent.DirentIndex);

            // Lets write it now
            dirent.Write(io.Writer);
            WriteCluster(dirent.ParentCluster, io.ToArray());
        }

        /// <summary>
        /// Moves a dirent into a new parent directory.
        /// </summary>
        /// <param name="parentDirent">The new parent directory cluster.</param>
        /// <param name="direntToMove">The dirent that is being moved.</param>
        public void MoveDirent(uint parentDirent, Dirent direntToMove)
        {
            // If we are already there, do nothing
            if (parentDirent == direntToMove.ParentCluster)
                return;

            // If we are creating something in the root dir lets clear our cache
            if (parentDirent == rootDirFirstCluster)
                rootDir = null;
            if (parentDirent == direntCacheClusterIndex)
                direntCache = null;

            // First lets scan our parent for a empty dirent
            uint currentCluster = parentDirent;
        Continue:
            SeekToCluster(currentCluster);
            for (int x = 0; x < direntsPerCluster; x++)
            {
                Dirent dirent = new Dirent();
                dirent.Read(fatx.Io.Reader);

                // If this is a empty dirent we are good
                if (!dirent.IsEmpty)
                    continue;

                // Update info
                dirent.Name = direntToMove.Name;
                dirent.ParentCluster = currentCluster;
                dirent.DirentIndex = x;
                dirent.Size = direntToMove.Size;
                DateTime now = DateTime.Now;
                dirent.CreateTime = direntToMove.CreateTime;
                dirent.AccessTime = now;
                dirent.ModifiedTime = now;
                dirent.Attributes = direntToMove.Attributes;
                dirent.FirstCluster = direntToMove.FirstCluster;

                // Delete our old dirent
                direntToMove.Delete();
                direntToMove.FirstCluster = 0xFFFFFFFF;
                UpdateDirent(direntToMove);

                // Update our new dirent
                UpdateDirent(dirent);
                return;
            }

            // We didnt find a empty dirent so lets check for more space
            currentCluster = GetNextCluster(currentCluster);

            // Allocate if we need to
            if (currentCluster == 0xFFFFFFFF)
            {
                currentCluster = AllocateCluster();
                WriteCluster(currentCluster, new byte[clusterSize]);
            }

            // Now we should have a good cluster to continue with
            if (currentCluster != 0xFFFFFFFF)
                goto Continue;

            // This should never be hit
            return;
        }

        /// <summary>
        /// Creates a dirent given a parent direcotry and name.
        /// </summary>
        /// <param name="parentDirent">The parent directory cluster.</param>
        /// <param name="name">The name of the new dirent.</param>
        /// <param name="directory">Whether or not it should be a directory.</param>
        /// <returns>Returns the newly created dirent.</returns>
        public Dirent CreateDirent(uint parentDirent, string name, bool directory)
        {
            // If we are creating something in the root dir lets clear our cache
            if (parentDirent == rootDirFirstCluster)
                rootDir = null;
            if (parentDirent == direntCacheClusterIndex)
                direntCache = null;

            // First lets scan our parent for a empty dirent
            uint currentCluster = parentDirent;
        Continue:
            SeekToCluster(currentCluster);
            for (int x = 0; x < direntsPerCluster; x++)
            {
                Dirent dirent = new Dirent();
                dirent.Read(fatx.Io.Reader);

                // If this is a empty dirent we are good
                if (!dirent.IsEmpty)
                    continue;

                // Update info
                dirent.Name = name;
                dirent.ParentCluster = currentCluster;
                dirent.DirentIndex = x;
                dirent.Size = 0;
                DateTime now = DateTime.Now;
                dirent.CreateTime = now;
                dirent.AccessTime = now;
                dirent.ModifiedTime = now;
                dirent.Attributes = Dirent.Attribute.Normal;
                if (directory)
                {
                    dirent.Attributes |= Dirent.Attribute.Directory;
                    // Allocate a cluster for this and null it
                    dirent.FirstCluster = AllocateCluster();
                    WriteCluster(dirent.FirstCluster, new byte[clusterSize]);
                }
                else
                    dirent.FirstCluster = 0xFFFFFFFF;

                // Update our dirent now
                UpdateDirent(dirent);
                return dirent;
            }

            // We didnt find a empty dirent so lets check for more space
            currentCluster = GetNextCluster(currentCluster);

            // Allocate if we need to
            if (currentCluster == 0xFFFFFFFF)
            {
                currentCluster = AllocateCluster();
                WriteCluster(currentCluster, new byte[clusterSize]);
            }

            // Now we should have a good cluster to continue with
            if (currentCluster != 0xFFFFFFFF)
                goto Continue;

            // This should never be hit
            return null;
        }

        /// <summary>
        /// Seeks the IO to the position relative to the start of a cluster.
        /// </summary>
        /// <param name="cluster">The cluster to be relative to.</param>
        /// <param name="position">The position inside the cluster.</param>
        public void SeekToPositionInCluster(uint cluster, uint position)
        {
            // Seek to our cluster
            SeekToCluster(cluster);

            // Lets move to the entry we want to update
            fatx.Io.Position += position;
        }

        /// <summary>
        /// Seeks the IO to the position relative to the start of a cluster.
        /// </summary>
        /// <param name="cluster">The cluster to be relative to.</param>
        /// <param name="position">The position inside the cluster.</param>
        public void SeekToPositionInCluster(uint cluster, int position)
        {
            // Seek to our cluster
            SeekToCluster(cluster);

            // Lets move to the entry we want to update
            fatx.Io.Position += position;
        }

        /// <summary>
        /// Seeks the IO to the start of the cluster given.
        /// </summary>
        /// <param name="cluster">The cluster to seek to.</param>
        public void SeekToCluster(uint cluster)
        {
            // Just to make sure we arent going over
            if (cluster > clusterCount)
                throw new Exception("SeekToCluster: Cluster out of range");

            // Seek to our position
            long clusterOffset = fileAreaOffset + (((long)cluster - 1) * clusterSize);
            fatx.Io.Position = clusterOffset;
        }

        /// <summary>
        /// Reads the contents of a cluster into memory.
        /// </summary>
        /// <param name="cluster">The cluster to read.</param>
        /// <returns>A byte array of the data in the cluster.</returns>
        public byte[] ReadCluster(uint cluster)
        {
            SeekToCluster(cluster);
            return fatx.Io.Reader.ReadBytes((int)clusterSize);
        }

        /// <summary>
        /// Writes data into a cluster to the filesystem.
        /// </summary>
        /// <param name="cluster">The cluster to update.</param>
        /// <param name="data">The new data for the cluster.</param>
        public void WriteCluster(uint cluster, byte[] data)
        {
            SeekToCluster(cluster);
            fatx.Io.Writer.Write(data);
        }

        /// <summary>
        /// Gets the next cluster in the chain map.
        /// </summary>
        /// <param name="cluster">The cluster to start at.</param>
        /// <returns>The next cluster id.</returns>
        public uint GetNextCluster(uint cluster)
        {
            // Make sure this cluster is valid
            if (cluster > clusterCount)
                throw new Exception("GetNextCluster: Cluster out of range");

            // Return our next cluster
            return chainMap[cluster];
        }

        /// <summary>
        /// Sets the next cluster in the chain map.
        /// </summary>
        /// <param name="cluster">The cluster to set.</param>
        /// <param name="nextCluster">The next cluster to set.</param>
        public void SetNextCluster(uint cluster, uint nextCluster)
        {
            SetNextCluster(cluster, nextCluster, true);
        }

        /// <summary>
        /// Sets the next cluster in the chain map.
        /// </summary>
        /// <param name="cluster">The cluster to set.</param>
        /// <param name="nextCluster">The next cluster to set.</param>
        /// <param name="writeChainMap">Whether or not the chain map should be updated.</param>
        public void SetNextCluster(uint cluster, uint nextCluster, bool writeChainMap)
        {
            SetNextCluster(cluster, nextCluster, writeChainMap, true);
        }

        /// <summary>
        /// Sets the next cluster in the chain map.
        /// </summary>
        /// <param name="cluster">The cluster to set.</param>
        /// <param name="nextCluster">The next cluster to set.</param>
        /// <param name="writeChainMap">Whether or not to update the chain map.</param>
        /// <param name="checkBounds">Whether or not bounds should be checked before taking action.</param>
        public void SetNextCluster(uint cluster, uint nextCluster, bool writeChainMap, bool checkBounds)
        {
            // Make sure this cluster is valid
            if (cluster > clusterCount && checkBounds)
                throw new Exception("SetNextCluster: Cluster out of range");

            // Make sure the next cluster is valid
            if (nextCluster > clusterCount && checkBounds)
                throw new Exception("SetNextCluster: Next Cluster out of range");

            // Return our next cluster
            chainMap[cluster] = nextCluster;

            // Lets write our new chain map
            if (writeChainMap)
                WriteChainMap();
        }

        /// <summary>
        /// Allocates a previously unallocated cluster.
        /// </summary>
        /// <returns>The id of the newly allocated cluster.</returns>
        public uint AllocateCluster()
        {
            return AllocateCluster(true);
        }

        /// <summary>
        /// Allocates a previously unallocated cluster.
        /// </summary>
        /// <param name="writeChainMap">Whether or not to write the chain map after.</param>
        /// <returns>The id of the newly allocated cluster.</returns>
        public uint AllocateCluster(bool writeChainMap)
        {
            // Find a free cluster
            uint cluster = 0xFFFFFFFF;
            for (uint x = 1; x < clusterCount; x++)
                if (chainMap[x] == 0)
                {
                    chainMap[x] = 0xFFFFFFFF;
                    cluster = x;
                    break;
                }

            // Lets write our new chain map
            if (cluster != 0xFFFFFFFF && writeChainMap)
                WriteChainMap();

            // Return our cluster
            return cluster;
        }

        /// <summary>
        /// Gets the amount of free clusters left.
        /// </summary>
        /// <returns>The amount of free clusters left.</returns>
        public uint GetFreeClusterCount()
        {
            uint count = 0;
            for (int x = 1; x < clusterCount; x++)
                if (chainMap[x] == 0)
                    count++;
            return count;
        }

        /// <summary>
        /// Sets the length of a dirent.
        /// </summary>
        /// <param name="length">The new length to set.</param>
        /// <param name="dirent">The dirent to update.</param>
        public void SetDirentLength(int length, ref Dirent dirent)
        {
            // Its the same so we are fine
            if (length == dirent.Size)
                return;

            // Check if we must allocate
            if (length > dirent.Size)
            {
                // How many clusters will we be adding?
                int fileClusterCount = (int)Math.Ceiling((double)dirent.Size / clusterSize);
                int clustersToAdd = (int)Math.Ceiling((double)length / clusterSize);
                clustersToAdd -= fileClusterCount;
                if (clustersToAdd > GetFreeClusterCount())
                    throw new Exception("SetDirentLength: There is not enogh space");

                // If our first cluster is nothing then lets begin by allocating
                uint currentCluster = dirent.FirstCluster;
                if (currentCluster == 0xFFFFFFFF)
                {
                    dirent.FirstCluster = AllocateCluster(false);
                    currentCluster = dirent.FirstCluster;
                    clustersToAdd--; // Since we added 1 already
                    fileClusterCount++; // We added a cluster
                }

                // Get our last cluster so we can begin adding
                for (int x = 1; x < fileClusterCount; x++)
                    if ((currentCluster = GetNextCluster(currentCluster)) == 0xFFFFFFFF)
                        throw new Exception("SetDirentLength: Bad next Cluster");

                // Now lets continue to add
                for (int x = 0; x < clustersToAdd; x++)
                {
                    uint nextCluster = AllocateCluster(false);
                    if (nextCluster == 0xFFFFFFFF)
                        throw new Exception("SetDirentLength: Could not allocate cluster");

                    SetNextCluster(currentCluster, nextCluster, false);
                    currentCluster = nextCluster;
                }
            }
            else
            {
                // How many clusters will we have after
                uint newClusterCount = (uint)Math.Ceiling((double)length / clusterSize);

                // Get our last cluster
                uint currentCluster = dirent.FirstCluster;
                for (int x = 1; x < newClusterCount; x++)
                    if ((currentCluster = GetNextCluster(currentCluster)) == 0xFFFFFFFF)
                        throw new Exception("SetDirentLength: Bad next Cluster");

                // Now lets continue and remove any after this
                FreeChain(currentCluster, 0xFFFFFFFF, true, false);

                if (newClusterCount == 0)
                {
                    SetNextCluster(currentCluster, 0, false, false);
                    dirent.FirstCluster = 0xFFFFFFFF;
                }
            }

            // Write our chainmap now
            WriteChainMap();

            // Set our new length
            dirent.Size = length;
        }

        /// <summary>
        /// Returns the friendly name of this partition.
        /// </summary>
        /// <returns>The friendly name as a string.</returns>
        public override string ToString()
        {
            return friendlyName;
        }

        /// <summary>
        /// Compare this partition to others for sorting.
        /// </summary>
        /// <param name="other">The other partition to compare against.</param>
        /// <returns>A compareto response.</returns>
        public int CompareTo(Partition other)
        {
            return this.offset.CompareTo(other.offset);
        }

    }

    /// <summary>
    /// A special type of partition for USB devices.
    /// </summary>
    public class USBPartition : Partition
    {
        long xdatastart;

        public USBPartition(FATXDevice fatx, string FriendlyName, long Offset)
            : base(fatx, FriendlyName, Offset)
        {
            USBStream stream = (USBStream)fatx.Io.Stream;
            xdatastart = stream.DataStart;
        }

        public USBPartition(FATXDevice fatx, string FriendlyName, long offset, long size)
            : base(fatx, FriendlyName, offset, size)
        {
            USBStream stream = (USBStream)fatx.Io.Stream;
            xdatastart = stream.DataStart;
        }

        public override void Read(Boolean readChainMap)
        {
            // Read the partition header
            fatx.Io.Position = (offset);
            magic = fatx.Io.Reader.ReadUInt32();

            // If its not valid lets stop here
            // No more exception thorwing so we can handle elsewhere
            if (!IsValid)
                return;

            // Valid partition lets continue
            id = fatx.Io.Reader.ReadUInt32();
            sectorsPerCluster = fatx.Io.Reader.ReadUInt32();
            rootDirFirstCluster = fatx.Io.Reader.ReadUInt32();

            // Work out cluster size and count
            clusterSize = sectorsPerCluster * 0x200;
            clusterCount = (uint)((size - xdatastart) / clusterSize);

            // Work out Chain Map size, count, offset
            chainMapEntrySize = clusterCount < 0xfff0 ? 2 : 4;
            chainMapSize = (uint)xdatastart - 0x1000;
            chainMapSize = Utilities.RoundToPages(chainMapSize);
            chainMapOffset = offset + 0x1000;

            //TEST
            long pos = fatx.Io.Reader.BaseStream.Position;
            fatx.Io.Reader.BaseStream.Position = chainMapOffset;
            uint test1 = fatx.Io.Reader.ReadUInt32();
            fatx.Io.Reader.BaseStream.Position = chainMapOffset;
            uint test2 = fatx.Io.Reader.ReadUInt16();
            if (test2 == 0xFFF8)
            {
                chainMapEntrySize = 2;
            }
            else
            {
                chainMapEntrySize = 4;
            }

            // Set the File Area Offset
            //fileAreaOffset = chainMapOffset + chainMapSize;
            fileAreaOffset = xdatastart;
            direntsPerCluster = clusterSize / 0x40;

            // Read our chainmap
            if (readChainMap)
                ReadChainMap();

        }

    }

}
