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
using System.IO;

namespace xbFsLib.FATX
{

    /// <summary>
    /// A stream that reads and writes to a FATX device.
    /// </summary>
    public class DirentStream : Stream
    {
        private long position;

        private readonly Partition partition;
        private Dirent dirent;

        private uint currentCluster;
        private int clustersIn;
        private byte[] clusterData;
        private bool clusterModified;

        private bool fileModified;

        private FATXDevice fatx;

        public DirentStream(FATXDevice fatx, int partition, Dirent dirent, FileMode fileMode)
        {
            // Make sure the dirent isnt null
            if (dirent == null)
                throw new Exception("Dirent can not be null");

            // Set our partition and dirent
            this.fatx = fatx;
            this.partition = fatx.Partitions[partition];
            this.dirent = dirent;

            // Now figure out what we want to do with it first
            switch (fileMode)
            {
                case FileMode.Append:
                case FileMode.Create:
                case FileMode.OpenOrCreate:
                    currentCluster = this.dirent.FirstCluster;
                    if (fileMode == FileMode.Append)
                        Position = Length;
                    if (fileMode == FileMode.Create)
                    {
                        this.dirent.CreateTime = DateTime.Now;
                        SetLength(0);
                    }
                    break;
                case FileMode.CreateNew:
                    throw new IOException("File already exists");
                case FileMode.Open:
                case FileMode.Truncate:
                    currentCluster = this.dirent.FirstCluster;
                    if (fileMode == FileMode.Truncate)
                        SetLength(0);
                    break;
                default:
                    throw new Exception("FileMode not supported");
            }

            // Since we are accessing it lets update it
            this.dirent.AccessTime = DateTime.Now;
            this.partition.UpdateDirent(dirent);
        }

        public DirentStream(FATXDevice fatx, int partition, string filePath, FileMode fileMode)
        {
            this.fatx = fatx;

            // Set our partition
            this.partition = fatx.Partitions[partition];

            // Try and find our dirent, so we know what to do
            dirent = fatx.DirentFind(filePath, partition, false);

            // Now figure out what we want to do with it first
            switch (fileMode)
            {
                case FileMode.Append:
                case FileMode.Create:
                case FileMode.OpenOrCreate:
                    if (dirent == null)
                        dirent = fatx.DirentCreate(filePath, partition);
                    currentCluster = dirent.FirstCluster;
                    if (fileMode == FileMode.Append)
                        Position = Length;
                    if (fileMode == FileMode.Create)
                    {
                        dirent.CreateTime = DateTime.Now;
                        SetLength(0);
                    }
                    break;
                case FileMode.CreateNew:
                    if (dirent != null)
                        throw new IOException("File already exists");
                    dirent = fatx.DirentCreate(filePath, partition);
                    currentCluster = dirent.FirstCluster;
                    break;
                case FileMode.Open:
                case FileMode.Truncate:
                    if (dirent == null)
                        throw new FileNotFoundException("File not found");
                    currentCluster = dirent.FirstCluster;
                    if (fileMode == FileMode.Truncate)
                        SetLength(0);
                    break;
                default:
                    throw new Exception("FileMode not supported");
            }

            // Since we are accessing it lets update it
            //dirent.AccessTime = DateTime.Now;
            //this.partition.UpdateDirent(dirent);
        }

        public override long Position
        {
            get { return position; }
            set { Seek(value, SeekOrigin.Begin); }
        }
        public override long Length { get { return dirent.Size; } }

        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return true; } }
        public override bool CanSeek { get { return true; } }

        public override void SetLength(long value)
        {
            // We dont need to do anything
            if (Length == value)
                return;

            // Call to our partition class to do the work
            partition.SetDirentLength((int)value, ref dirent);
            dirent.ModifiedTime = DateTime.Now;
            partition.UpdateDirent(dirent);

            if (clusterData == null)
                if (dirent.Size > 0)
                {
                    currentCluster = dirent.FirstCluster;
                    Seek(position, SeekOrigin.Begin);
                    ReadCluster();
                }

            // We modified this file
            fileModified = true;

            // Flush the underlying stream
            Flush();
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            // Get our position
            switch (origin)
            {
                case SeekOrigin.Begin:
                    position = offset;
                    break;
                case SeekOrigin.Current:
                    position += offset;
                    break;
                case SeekOrigin.End:
                    position = Length - offset;
                    break;
                default:
                    throw new Exception("Invalid SeekOrigin");
            }

            // Recalculate our current cluster
            int clustersInNew = (int)(position / partition.ClusterSize);

            // If this is the same cluster lets stop here
            if (clustersInNew == clustersIn)
                return position;

            // Write whatever we have first
            WriteCluster();

            // We have a new cluster so we must read our next one
            clustersIn = clustersInNew;
            currentCluster = dirent.FirstCluster;
            for (int x = 0; x < clustersIn; x++)
                if ((currentCluster = partition.GetNextCluster(currentCluster)) == 0xFFFFFFFF)
                    throw new Exception("Position is larger then file (Allocate first!)");

            // We have our final cluster index now lets read
            ReadCluster();

            // Return our new position
            return position;
        }

        public override void Flush()
        {
            // Flush our last cluster
            WriteCluster();

            // Flush the device and avoid caching errors
            this.fatx.Io.Stream.Flush();
        }

        public override void Close()
        {
            // Flush our last cluster
            if (clusterModified)
                Flush();

            if (fileModified)
            {
                dirent.ModifiedTime = DateTime.Now;
                partition.UpdateDirent(dirent);
            }

            base.Close();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Some values we will need later on
            int read = 0;
            int partialRead;

            // If we are null lets read a block into the cache
            if (clusterData == null)
                ReadCluster();

            // Limit read size
            if ((position + count) > Length)
                count = (int)(Length - position);

            // If our count is less then 0 lets go!
            if (count <= 0)
                return 0;

            // Process partial cluster read before
            int tempPos = (int)(Position % partition.ClusterSize);
            if (tempPos != 0)
            {
                // Get our size
                partialRead = Math.Min(count, (int)partition.ClusterSize - tempPos);

                // Copy our data
                Array.Copy(clusterData, tempPos, buffer, offset, partialRead);

                // Fix up our counts
                count -= partialRead;
                read += partialRead;
                position += partialRead;
                offset += partialRead;

                // Check if we are done
                if (count == 0)
                {
                    // Before we quit if we read up the last of the data
                    if ((Position % partition.ClusterSize) == 0 &&
                        Position != Length)
                        ReadNextCluster();

                    return read;
                }
            }

            // Process entire cluster read
            partialRead = count / (int)partition.ClusterSize;
            if (partialRead != 0)
            {
                while (partialRead > 0)
                {
                    // Copy our data
                    Array.Copy(clusterData, 0, buffer, offset, (int)partition.ClusterSize);

                    // Fix up our counts
                    count -= (int)partition.ClusterSize;
                    read += (int)partition.ClusterSize;
                    position += (int)partition.ClusterSize;
                    offset += (int)partition.ClusterSize;
                    partialRead--;

                    // Read next cluster
                    if (Position != Length)
                        ReadNextCluster();
                }
            }

            // Process partial sector read after
            partialRead = count;
            Array.Copy(clusterData, 0, buffer, offset, partialRead);
            read += partialRead;
            position += partialRead;

            // Before we quit if we read up the last of the data
            if ((Position % partition.ClusterSize) == 0 && Position != Length)
                ReadNextCluster();

            // Return how much we read
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            // We modified this file
            fileModified = true;

            // Some values we will need later on
            int partialWrite;

            // Lets get ready to write
            if (clusterData == null)
            {
                if (dirent.Size == 0)
                {
                    SetLength(count);
                    currentCluster = dirent.FirstCluster;
                    Position = 0;
                }
                ReadCluster();
            }

            // Check if our file is long enough
            if ((position + count) > Length)
                SetLength(position + count);

            // Process partial cluster write before
            int tempPos = (int)(Position % partition.ClusterSize);
            if (tempPos != 0)
            {
                // Get our size
                partialWrite = Math.Min(count, (int)partition.ClusterSize - tempPos);

                // Copy our data
                Array.Copy(buffer, offset, clusterData, tempPos, partialWrite);
                clusterModified = true;

                // Fix up our counts
                count -= partialWrite;
                position += partialWrite;
                offset += partialWrite;

                // Check if we are done
                if (count == 0)
                {
                    // Before we quit if we write up the last of the data
                    if ((Position % partition.ClusterSize) == 0 &&
                        Position != Length)
                        ReadNextCluster();

                    return;
                }
            }

            // Process entire cluster write
            partialWrite = count / (int)partition.ClusterSize;
            if (partialWrite != 0)
            {
                while (partialWrite > 0)
                {
                    // Copy our data
                    Array.Copy(buffer, offset, clusterData, 0, (int)partition.ClusterSize);
                    clusterModified = true;

                    // Fix up our counts
                    count -= (int)partition.ClusterSize;
                    position += (int)partition.ClusterSize;
                    offset += (int)partition.ClusterSize;
                    partialWrite--;

                    // Read next cluster
                    if (Position != Length)
                        ReadNextCluster();
                }
            }

            // Process partial sector read after
            partialWrite = count;
            Array.Copy(buffer, offset, clusterData, 0, partialWrite);
            clusterModified = true;
            position += partialWrite;

            // Before we quit if we read up the last of the data
            if ((Position % partition.ClusterSize) == 0 && Position != Length)
                ReadNextCluster();
        }

        private void ReadNextCluster()
        {
            // Flush any data that has been written
            WriteCluster();

            // Make sure this is a valid block
            uint nextBlock = partition.GetNextCluster(currentCluster);

            clustersIn++;
            currentCluster = nextBlock;

            // We have a valid block so lets continue
            ReadCluster();
        }

        private void ReadCluster()
        {
            // Do a quick check
            if (currentCluster == 0xFFFFFFFF)
                throw new Exception("This cluster is invalid");

            // Now lets read our cluster data
            clusterData = partition.ReadCluster(currentCluster);

            // alright we got a fresh block so we know its not modified
            clusterModified = false;
        }

        private void WriteCluster()
        {
            // Lets do this only if this block has been modified to be efficent
            if (!clusterModified)
                return;

            // write block data
            if (currentCluster != 0xFFFFFFFF)
                partition.WriteCluster(currentCluster, clusterData);
        }
    }
}