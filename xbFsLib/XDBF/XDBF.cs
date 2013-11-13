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
using xbFsLib.IO;

namespace xbFsLib.XDBF
{

    /// <summary>
    /// Class that manages free and allocated sections for the XDBF file format.
    /// 
    /// XDBF files are fixed size data files that use allocated and free sections
    /// to allocate data. The fixed nature of these files allow for data to be written
    /// and overwritten without the file size changing.
    /// </summary>
    public class XDBF
    {
        
        /// <summary>
        /// The list of allocated data sections in the file.
        /// </summary>
        protected List<AllocatedSection> AllocatedSections { get; set; }

        /// <summary>
        /// The list of free sections in the file.
        /// </summary>
        protected List<FreeSection> FreeSections { get; set; }

        /// <summary>
        /// Magic, XDBF in hex.
        /// </summary>
        protected const int Magic = 0x58444246;

        /// <summary>
        /// The version of XDBF this file is.
        /// </summary>
        protected int Version { get; set; }

        /// <summary>
        /// The maximum amount of entries allowed in this file.
        /// </summary>
        protected int EntryMax { get; set; }

        /// <summary>
        /// The current amount of entries in this file.
        /// </summary>
        protected int EntryCurrent { get; set; }

        /// <summary>
        /// The maximum amount of free sections allowed in this file.
        /// </summary>
        protected int FreeMax { get; set; }

        /// <summary>
        /// The amount of free sections in this file.
        /// </summary>
        protected int FreeCurrent { get; set; }

        /// <summary>
        /// Gets the start address of the section.
        /// </summary>
        protected int SectionStart
        {
            get
            {
                return ((FreeMax + 3) << 3) + (EntryMax * 0x12);
            }
        }

        /// <summary>
        /// Gets the start address of the free section.
        /// </summary>
        protected int FreeSectionStart
        {
            get
            {
                return (EntryMax * 0x12) + 0x18;
            }
        }

        /// <summary>
        /// The underlying IO for the data.
        /// </summary>
        public CoreIO IO { get; set; }

        /// <summary>
        /// Creates a new XDBF file from an IO.
        /// </summary>
        /// <param name="stream">The stream to use.</param>
        public XDBF(IO.CoreIO stream)
        {
            // Create our io
            this.IO = stream;

            // Set our default values
            this.EntryMax = 0x200;
            this.FreeMax = 0x200;
            this.Version = 0x10000;

            // Init our lists
            this.FreeSections = new List<FreeSection>();
            this.AllocatedSections = new List<AllocatedSection>();
        }

        /// <summary>
        /// Reads the header in using the default reader.
        /// </summary>
        public virtual void Read()
        {
            // Read
            this.Read(this.IO.Reader);
        }

        /// <summary>
        /// Reads the header in.
        /// </summary>
        /// <param name="reader">The reader to use.</param>
        protected void Read(EndianReader reader)
        {
            // Move to beggining
            reader.BaseStream.Position = 0;

            // Read our magic
            if (reader.ReadInt32() != Magic)
                throw new Exception("Invalid magic!");

            // Read our version
            this.Version = reader.ReadInt32();

            // Read our entry max and current
            this.EntryMax = reader.ReadInt32();
            this.EntryCurrent = reader.ReadInt32();

            // Read our free max and current
            this.FreeMax = reader.ReadInt32();
            this.FreeCurrent = reader.ReadInt32();

            // Read our allocated sections
            for (int x = 0; x < EntryCurrent; x++)
            {
                // Create a new allocated section
                AllocatedSection section = new AllocatedSection();
                section.Read(reader);

                // Add our section to our list
                this.AllocatedSections.Add(section);
            }

            // Go to our free section start
            reader.BaseStream.Position = this.FreeSectionStart;

            // Read our free sections
            for (int x = 0; x < this.FreeCurrent; x++)
            {
                // Create a new free section
                FreeSection section = new FreeSection();
                section.Read(reader);

                // Add our free section to our list
                this.FreeSections.Add(section);
            }

        }

        /// <summary>
        /// Saves the header using the default IO writer.
        /// </summary>
        public virtual void Save()
        {
            // Save
            this.Save(this.IO.Writer);
        }

        /// <summary>
        /// Saves the header part of the file.
        /// </summary>
        /// <param name="writer">The writer to write to.</param>
        public void Save(EndianWriter writer)
        {
            // Set our counts
            this.EntryCurrent = this.AllocatedSections.Count;
            this.FreeCurrent = this.FreeSections.Count;

            // Go to the start of the file
            writer.BaseStream.Position = 0;

            // Write our magic
            writer.Write(Magic);

            // Write our version
            writer.Write(Version);

            // Write our allocated section values
            writer.Write(this.EntryMax);
            writer.Write(this.EntryCurrent);

            // Write our free section values
            writer.Write(this.FreeMax);
            writer.Write(this.FreeCurrent);

            // Write all of our allocated sections
            for (int x = 0; x < this.AllocatedSections.Count; x++)
            {
                // Save our section
                this.AllocatedSections[x].Save(writer);
            }

            // Go to our free section start
            writer.BaseStream.Position = this.FreeSectionStart;

            // Write all of our free sections
            for (int x = 0; x < this.FreeSections.Count; x++)
            {
                // Save our free section
                this.FreeSections[x].Save(writer);
            }
        }

        /// <summary>
        /// Nulls all of the data left in free sections.
        /// </summary>
        public void ClearAllFreeData()
        {
            for (int i = 0; i < FreeSections.Count; i++)
            {
                // Go to its data
                this.IO.Reader.BaseStream.Position = FreeSections[i].Offset + this.SectionStart;

                // Write over it
                this.IO.Writer.Write(new byte[FreeSections[i].Size]);
            }
        }

        /// <summary>
        /// Rebuilds the file and cleans things up. Also groups allocated blocks and free blocks tighter for more space
        /// as well as adds to the max amounts for frees and allocated sections.
        /// </summary>
        private void Rebuild()
        {
            // Sort the sections
            AllocatedSections.Sort();
            FreeSections.Sort();

            // Get all of the data for the sections
            List<byte[]> currentDataBlocks = new List<byte[]>();
            for (int i = 0; i < AllocatedSections.Count; i++)
            {
                MoveToSectionData(AllocatedSections[i]);
                currentDataBlocks.Add(IO.Reader.ReadBytes(AllocatedSections[i].Size));
            }

            // Now that we've gotten our data, let's update some things
            EntryCurrent = AllocatedSections.Count;
            FreeCurrent = FreeSections.Count;
            if (EntryCurrent >= EntryMax)
                EntryMax = EntryCurrent + 10;
            if (FreeCurrent >= FreeMax)
                FreeMax = FreeCurrent + 10;

            // Set the length of the stream
            long totalDataLength = 0;
            for (int i = 0; i < AllocatedSections.Count; i++)
                totalDataLength += AllocatedSections[i].Size;
            for (int i = 0; i < FreeSections.Count; i++)
                totalDataLength += FreeSections[i].Size;
            IO.Stream.SetLength(0); // Delete everything
            IO.Stream.SetLength(SectionStart + totalDataLength);

            // Write the data back in
            int curOffset = 0;
            for (int i = 0; i < AllocatedSections.Count; i++)
            {
                // Go to the start of the block to write
                IO.Position = SectionStart + curOffset;

                // Write the data in
                IO.Writer.Write(currentDataBlocks[i]);

                // Update the allocated section's offset
                AllocatedSections[i].Offset = curOffset;
                AllocatedSections[i].Size = currentDataBlocks[i].Length;

                // Updat the offset for the next one
                curOffset += currentDataBlocks[i].Length;
            }

            // Turn the free sections into one big one
            FreeSection newSingleFree = new FreeSection();
            newSingleFree.Offset = curOffset;
            uint totalFreeSectionSize = 0;
            for (int i = 0; i < FreeSections.Count; i++)
                totalFreeSectionSize += FreeSections[i].Size;
            newSingleFree.Size = totalFreeSectionSize;
            FreeSections.Clear();
            FreeSections.Add(newSingleFree);

            // Save the header
            this.Save(IO.Writer);

            // Clean up
            currentDataBlocks.Clear();
        }

        /// <summary>
        /// Attempts to expand the file size to accomodate a new section.
        /// </summary>
        /// <param name="amount">The amount to expand by.</param>
        private void ExpandFileSize(int amount)
        {
            // Find the section that comes last
            bool isAllocated = false;
            int index = -1;
            int maxOffset = -1;
            for (int i = 0; i < AllocatedSections.Count; i++)
            {
                if (AllocatedSections[i].Offset > maxOffset)
                {
                    maxOffset = AllocatedSections[i].Offset;
                    index = i;
                    isAllocated = true;
                }
            }
            for (int i = 0; i < FreeSections.Count; i++)
            {
                if (FreeSections[i].Offset > maxOffset)
                {
                    maxOffset = AllocatedSections[i].Offset;
                    index = i;
                    isAllocated = false;
                }
            }

            // Now let's see what we can do
            if (!isAllocated)
            {
                // It's not allocated so we can just add to it!
                FreeSections[index].Size += (uint)amount;

                // Now let's expand the stream
                IO.Stream.SetLength(IO.Stream.Length + amount);

                // Now let's save the header to reflect the length change
                Save();
            }
            else
            {
                // It is allocated so we need to add a free section
                if (FreeSections.Count >= FreeMax)
                    throw new Exception("Cannot add new free section. Out of space.");

                // Set up the new free section
                FreeSection newFree = new FreeSection();
                newFree.Offset = AllocatedSections[index].Offset + AllocatedSections[index].Size;
                newFree.Size = (uint)amount;

                // Add the new free section
                FreeSections.Add(newFree);

                // Save the header to reflect the change
                Save();
            }

        }

        /// <summary>
        /// Allocates a section from the free list.
        /// </summary>
        /// <param name="dataSize">The size needed.</param>
        /// <param name="ns">The namespace being used.</param>
        /// <param name="id">The id of the section.</param>
        /// <returns>The newly allocated section.</returns>
        protected AllocatedSection AllocateSection(int dataSize, Namespace ns, long id, bool tryExpandFile = true)
        {
            // The free section to eventually allocate
            FreeSection freeToUse = null;

            // Go through all of our free sections to find the best fit
            foreach (FreeSection freeSection in this.FreeSections)
            {
                if (freeSection.Size == dataSize)
                {
                    // This is a perfect match, we should definitely use it
                    freeToUse = freeSection;
                    break;
                }
                else if (freeSection.Size > dataSize)
                {
                    if (freeToUse == null)
                        freeToUse = freeSection; // This one would work
                    else
                    {
                        if (freeSection.Offset < freeToUse.Offset)
                            freeToUse = freeSection; // We found a better spot
                    }
                }
            }

            // We couldn't find a good spot to allocate
            if (freeToUse == null)
            {
                // Avoid never ending recursion
                if (!tryExpandFile)
                    return null;

                // Let's try to expand the size of the file
                ExpandFileSize(dataSize);

                // Now let's try again
                AllocatedSection newAttempt = AllocateSection(dataSize, ns, id, false);
                if (newAttempt == null)
                    return null;
                else
                    return newAttempt;
            }

            // Check if the size of the section is bigger or matches
            if (freeToUse.Size == dataSize)
            {
                // Remove the section
                this.FreeSections.Remove(freeToUse);

                // Create the new section to return
                AllocatedSection rtn = new AllocatedSection() { 
                    Offset = freeToUse.Offset, 
                    Size = (int)freeToUse.Size, 
                    Id = id, 
                    Namespace = ns 
                };

                // Add to the allocated sections
                AllocatedSections.Add(rtn);

                // Return the new section
                return rtn;

            }
            else if (freeToUse.Size > dataSize)
            {
                // Make sure we have room to split
                if (AllocatedSections.Count >= EntryMax)
                    return null;

                // We have to split the free section into two parts
                FreeSection newFreeSection = new FreeSection();
                newFreeSection.Offset = freeToUse.Offset + dataSize;
                newFreeSection.Size = (uint)freeToUse.Size - (uint)dataSize;

                // Remove our free section
                this.FreeSections.Remove(freeToUse);

                // Fix the size
                freeToUse.Size = (uint)dataSize;

                // Add our new free section
                this.FreeSections.Add(newFreeSection);

                // Create the new section to return
                AllocatedSection rtn = new AllocatedSection() { 
                    Offset = freeToUse.Offset, 
                    Size = (int)freeToUse.Size, 
                    Id = id, 
                    Namespace = ns 
                };

                // Add to the allocated sections
                AllocatedSections.Add(rtn);

                // Return the new section
                return rtn;
            }

            // Return null since no sections could fit the data
            return null;
        }

        /// <summary>
        /// Deallocates a section and adds it to the free list.
        /// </summary>
        /// <param name="section">The section to free.</param>
        /// <returns>The new free section.</returns>
        private FreeSection FreeSection(AllocatedSection section)
        {
            // Make sure the allocated section exists
            if (!AllocatedSections.Contains(section))
                throw new Exception("Unable to free non-allocated section.");

            // Get the most up to date section for it
            section = GetAllocatedSection(section.Namespace, section.Id);

            // Let's erase the current data just to be on the safe side
            MoveToSectionData(section);
            this.IO.Writer.Write(new byte[section.Size]);

            // Remove it from the list of allocated sections
            AllocatedSections.Remove(section);

            // Create a new free section to take it's place
            FreeSection newFree = new FreeSection() { 
                Size = (uint)section.Size, 
                Offset = section.Offset 
            };
            FreeSections.Add(newFree);

            // Sort the lists
            this.AllocatedSections.Sort();
            this.FreeSections.Sort();

            // Return the new free section
            return newFree;
        }

        /// <summary>
        /// Moves the IO to the start of the allocated section's data.
        /// </summary>
        /// <param name="section">The section to use.</param>
        protected void MoveToSectionData(AllocatedSection section)
        {
            // Make sure the allocated section exists first
            if (!AllocatedSectionExists(section.Namespace, section.Id))
                throw new Exception("Unable to move to non-real section.");

            // Make sure we're working with the latest section
            section = GetAllocatedSection(section.Namespace, section.Id);

            // Move our stream to our offset
            this.IO.Reader.BaseStream.Position = section.Offset + this.SectionStart;
        }

        /// <summary>
        /// Gets an allocated section by namespace and id.
        /// </summary>
        /// <param name="Namespace">The namespace to look in.</param>
        /// <param name="Id">The id to match.</param>
        /// <returns>Returns the allocated section if found. Null if not found.</returns>
        private AllocatedSection GetAllocatedSection(Namespace Namespace, long Id)
        {
            // Go through all of our sections
            foreach (AllocatedSection allocatedSection in this.AllocatedSections)
            {
                // Check if we have a match
                if (allocatedSection.Id == Id && allocatedSection.Namespace == Namespace)
                    return allocatedSection;
            }

            // Return null
            return null;
        }

        /// <summary>
        /// Whether or not the allocated section exists.
        /// </summary>
        /// <param name="Namespace">The namespace to check in.</param>
        /// <param name="Id">The id to check for.</param>
        /// <returns>True if it exists.</returns>
        private bool AllocatedSectionExists(Namespace Namespace, long Id)
        {
            // Get our section
            AllocatedSection section = this.GetAllocatedSection(Namespace, Id);

            // Check if the section is null
            if (section == null)
                return false;

            // Return true
            return true;
        }

        /// <summary>
        /// Updates the data associated with an allocated section. Returns the new section.
        /// </summary>
        /// <param name="section">The allocated section to update.</param>
        /// <param name="sectionData">The new data to write.</param>
        /// <returns>The new allocated section.</returns>
        public AllocatedSection UpdateSection(AllocatedSection section, byte[] sectionData)
        {
            // Make sure the section exists, if not we need to create it
            if (!AllocatedSectionExists(section.Namespace, section.Id))
            {
                // Let's try to create the section
                section = AllocateSection(sectionData.Length, section.Namespace, section.Id);

                // If it failed, we throw and error
                if (section == null)
                    throw new Exception("Unable to update section: unable to create new section.");
            }

            // Make sure we're working with the latest section
            section = GetAllocatedSection(section.Namespace, section.Id);

            // If we're not dealing with the same size we need to find a new section to use
            if (section.Size != sectionData.Length)
            {
                // Let's free the allocated section
                FreeSection(section);

                // Now let's try to find a free section to fit it in
                AllocatedSection newSection = AllocateSection(sectionData.Length, section.Namespace, section.Id);
                if (newSection == null)
                {
                    // We couldn't get a free section that fits it, let's try rebuilding
                    this.Rebuild();

                    // Let's try again
                    newSection = AllocateSection(sectionData.Length, section.Namespace, section.Id);

                    // Hopefully it worked this time
                    if (newSection == null)
                        throw new Exception("Not enough free space to update section.");

                }

                // Let's update the section we were given
                section = newSection;
            }

            // Now let's update the data
            MoveToSectionData(section);
            IO.Writer.Write(sectionData);

            // Sort our sections
            this.AllocatedSections.Sort();
            this.FreeSections.Sort();

            // Return the section
            return section;
        }

    }

    /// <summary>
    /// Interface for allocated sections.
    /// </summary>
    public interface IAllocatedSection
    {
        AllocatedSection Section { get; set; }
        void Read(EndianReader reader);
        void Save(EndianWriter writer);
    }

    /// <summary>
    /// Interface for sections in general.
    /// </summary>
    public interface ISection
    {
        void Read(EndianReader reader);
        void Save(EndianWriter writer);
    }

    /// <summary>
    /// Class for dealing with allocated sections.
    /// </summary>
    public class AllocatedSection : ISection, IComparable<AllocatedSection>
    {

        /// <summary>
        /// The namespace that this allocated section belongs to.
        /// </summary>
        public Namespace Namespace { get; set; }

        /// <summary>
        /// The ID of this section.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// The start offset of this section.
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// The size of this section.
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// Reads in the data from the entry table.
        /// </summary>
        /// <param name="reader">The reader to read from.</param>
        public void Read(EndianReader reader)
        {
            // Read our section
            this.Namespace = (Namespace)reader.ReadInt16();
            this.Id = reader.ReadInt64();
            this.Offset = reader.ReadInt32();
            this.Size = reader.ReadInt32();
        }
        
        /// <summary>
        /// Saves the section data in the entry table.
        /// </summary>
        /// <param name="writer">The writer to write to.</param>
        public void Save(EndianWriter writer)
        {
            // Write our section
            writer.Write((short)this.Namespace);
            writer.Write(this.Id);
            writer.Write(this.Offset);
            writer.Write(this.Size);
        }

        /// <summary>
        /// Compares this allocated section with another.
        /// </summary>
        /// <param name="other">The other allocated section to compare against.</param>
        /// <returns>A compare result.</returns>
        public int CompareTo(AllocatedSection other)
        {
            // Check if namespaces are the same
            if (this.Namespace == other.Namespace)
            {
                // If they are return our comparison result
                return this.Id.CompareTo(other.Id);
            }
            else
            {
                // Return our comparison result
                return this.Namespace.CompareTo(other.Namespace);
            }
        }

    }

    /// <summary>
    /// Free sections are sections that are either empty or no longer being
    /// used by the filesystem.
    /// </summary>
    public class FreeSection : ISection, IComparable<FreeSection>
    {

        /// <summary>
        /// The offset of this section.
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// The size of this section.
        /// </summary>
        public uint Size { get; set; }

        /// <summary>
        /// Reads in this free section from the free section table.
        /// </summary>
        /// <param name="reader">The reader to read from.</param>
        public void Read(EndianReader reader)
        {
            // Read our free section
            this.Offset = reader.ReadInt32();
            this.Size = reader.ReadUInt32();
        }

        /// <summary>
        /// Saves this free section into the free section table.
        /// </summary>
        /// <param name="writer">The writer to write to.</param>
        public void Save(EndianWriter writer)
        {
            // Save our section
            writer.Write(this.Offset);
            writer.Write(this.Size);
        }

        /// <summary>
        /// Compares this free section to another free section.
        /// </summary>
        /// <param name="other">The other free section to compare against.</param>
        /// <returns>A compare result.</returns>
        public int CompareTo(FreeSection other)
        {
            return this.Offset.CompareTo(other.Offset);
        }

    }

    /// <summary>
    /// Namespaces refer to what type of section an allocated
    /// section is. For XDBF files these are fixed, but they can
    /// be anything.
    /// </summary>
    public enum Namespace : short
    {
        Achievement = 0x1,
        Image = 0x2,
        Setting = 0x3,
        Title = 0x4,
        String = 0x5,
        AvatarAward = 0x6
    }

}