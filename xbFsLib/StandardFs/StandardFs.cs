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

namespace xbFsLib.StandardFs
{

    /// <summary>
    /// Allows multiple filesystems to be translated into a single unified implementation.
    /// </summary>
    public abstract class StandardFs : IDisposable
    {

        // Common exceptions that are thrown
        public Exception ReadOnlyException = new Exception("This filesystem is read-only.");
        public Exception EntryNotFoundException = new Exception("The specified path does not exist.");
        public Exception NotADirectoryException = new Exception("The specified path is not a directory.");
        public Exception AlreadyExistsException = new Exception("The specified path already exists.");
        public Exception InvalidNameException = new Exception("Invalid filename.");

        // Some instance variables
        protected string name;

        /// <summary>
        /// Used by local filesystems, like STFS, to close connection to the local file after it's been used.
        /// </summary>
        protected virtual void TempCloseIO()
        { }

        /// <summary>
        /// Creates a new CoreFilesystem.
        /// </summary>
        public StandardFs()
        {
            name = "Unnamed Filesystem";
        }

        /// <summary>
        /// Must call after constructor in classes that inherit this one.
        /// </summary>
        protected void Initialize()
        {
            // Do nothing as of now
        }

        // Methods that must be implemented to allow the filesystem to work
        public abstract bool IsReadOnly(); // Whether or not the file system is read-only
        protected abstract bool IsValidEntryName(string name); // Checks if a filename is valid in this filesystem
        public abstract Entry GetEntry(string path); // Returns an entry given a path to the entry
        protected abstract string GetEntryPath(Entry entry); // Returns the path of a given entry.
        protected abstract bool DeleteEntry(Entry entry); // Deletes an entry
        protected abstract bool CreateEntry(string path, bool directory); // Creates a file or a directory
        protected abstract bool RenameEntry(Entry entry, string newName); // Renames an entry
        protected abstract List<Entry> GetEntries(Entry entry); // Returns a list of entries in a directory
        protected abstract void CloseFilesystem(); // Closes the filesystem and releases all resources.
        protected abstract IO.CoreIO GetEntryStream(string path, System.IO.FileMode mode); // Gets a stream of an entry
        // Entry IO streams should rebuild the filesystem (if necessary) after writing or after close, for convention.

        public abstract long GetUsedSpace();
        public abstract long GetFreeSpace();
        public abstract long GetTotalSpace();

        /// <summary>
        /// Returns a bitmap corresponding to an entry.
        /// </summary>
        /// <param name="entry">The entry to get the image for.</param>
        /// <returns>A bitmap.</returns>
        public virtual EntryExplorerInfo GetEntryExplorerInfo(Entry entry)
        {
            // Let's see if we have this cached, it'll save us time
            if (infoCache.ContainsKey(entry.Path))
                return infoCache[entry.Path];

            // Set up the info to return
            EntryExplorerInfo info = new EntryExplorerInfo();

            // Set the details to nothing so nothing shows
            info.Details1 = "";
            info.Details2 = "";
            if (!entry.IsFolder)
                info.ToolTipText += "Size: " + Utils.SizeToString(entry.Size) + Environment.NewLine;
            info.ToolTipText += "Created: " + entry.Created.ToString() + Environment.NewLine;
            info.ToolTipText += "Modified: " + entry.Modified.ToString();


            // Handle special cases of extensions
            if (UseEnhancedExplorerInfo)
            {
                try
                {
                    if (entry.IsFolder)
                    {
                        if (entry.FriendlyName != entry.Name)
                            info.Details1 = entry.FriendlyName;
                    }
                    else
                    {
                        switch (Utils.GetFilenameExtension(entry.Name))
                        {
                            case "png":
                            case "jpg":
                            case "jpeg":
                            case "gif":
                                info.Details1 = "Image";
                                System.IO.MemoryStream ms = new System.IO.MemoryStream(GetFileData(entry.Path));
                                info.Image = new System.Drawing.Bitmap(ms);
                                ms.Close();
                                break;
                        }
                    }
                }
                catch { }
            }

            // Return the info
            infoCache.Add(entry.Path, info);
            return info;
        }
        private Dictionary<string, EntryExplorerInfo> infoCache = new Dictionary<string, EntryExplorerInfo>();

        /// <summary>
        /// Whether or not to read the files to get more info for the explorer.
        /// </summary>
        public virtual bool UseEnhancedExplorerInfo
        {
            get { return true; }
        }

        /// <summary>
        /// Struct that the explorer can use to get info on a particular file.
        /// </summary>
        public struct EntryExplorerInfo
        {
            public string Details1;
            public string Details2;
            public System.Drawing.Bitmap Image;
            public string ToolTipText;
        }

        /// <summary>
        /// The name of the root of this filesystem.
        /// </summary>
        public virtual string RootName
        {
            get { return "Root"; }
        }

        /// <summary>
        /// Creates a node tree of the folders on this filesystem.
        /// </summary>
        /// <param name="basenode">The node currently being worked with.</param>
        /// <returns>The tree of folders as a list of treenodes.</returns>
        private List<System.Windows.Forms.TreeNode> CreateFolderList(System.Windows.Forms.TreeNode basenode = null)
        {
            List<System.Windows.Forms.TreeNode> rtn = new List<System.Windows.Forms.TreeNode>();
            List<Entry> dirEntries = new List<Entry>();
            if (basenode == null)
            {
                dirEntries = GetDirectories((DirectoryEntry)null);
            }
            else
            {
                Entry ent = (Entry)basenode.Tag;
                dirEntries = GetDirectories(ent);
            }
            for (int i = 0; i < dirEntries.Count; i++)
            {
                System.Windows.Forms.TreeNode curNode = new System.Windows.Forms.TreeNode();
                curNode.Text = dirEntries[i].Name;
                curNode.Tag = dirEntries[i];
                curNode.Nodes.AddRange(CreateFolderList(curNode).ToArray());
                rtn.Add(curNode);
            }
            return rtn;
        }

        /// <summary>
        /// Fixes common mistakes with inputted paths.
        /// Normal path example: home\welcome\welcome.txt
        /// </summary>
        /// <param name="path">The path to fix.</param>
        /// <returns>A properly formatted path as a string.</returns>
        public static string FixPath(string path)
        {
            path = path.Replace("/", "\\");
            if (path == "\\")
                path = "";
            if (path.StartsWith("\\"))
                path = path.Substring(1, path.Length - 1);
            if (path.EndsWith("\\"))
                path = path.Substring(0, path.Length - 1);
            return path;
        }

        /// <summary>
        /// Determines if an entry exists at a specified path.
        /// </summary>
        /// <param name="path">The path of the entry.</param>
        /// <returns>True if the entry exists.</returns>
        public virtual bool EntryExists(string path)
        {
            path = FixPath(path);
            if (GetEntry(path) == null)
                return false;
            return true;
        }

        /// <summary>
        /// Determines if a file exists at a specified path.
        /// </summary>
        /// <param name="path">The path of the file.</param>
        /// <returns>True if the file exists.</returns>
        public virtual bool FileExists(string path)
        {
            path = FixPath(path);
            Entry ent = GetEntry(path);
            if (ent == null)
                return false;
            if (ent.IsFolder)
                return false;
            return true;
        }

        /// <summary>
        /// Determines if a folder exists at a specified path.
        /// </summary>
        /// <param name="path">The path of the folder.</param>
        /// <returns>True if the folder exists.</returns>
        public virtual bool DirectoryExists(string path)
        {
            path = FixPath(path);
            if (path == "")
                return true;
            Entry ent = GetEntry(path);
            if (ent == null)
                return false;
            if (!ent.IsFolder)
                return false;
            return true;
        }

        /// <summary>
        /// Creates a directory with the specified path.
        /// </summary>
        /// <param name="path">The path of the directory to create.</param>
        /// <returns>True if created successfully.</returns>
        public bool CreateDirectory(string path)
        {
            if (IsReadOnly())
                throw ReadOnlyException;
            path = FixPath(path);
            if (DirectoryExists(path))
                return true;
            return CreateEntry(path, true);
        }

        /// <summary>
        /// Deletes a directory at the specified path.
        /// </summary>
        /// <param name="path">The path of the directory to delete.</param>
        /// <returns>True if deleted successfully.</returns>
        public bool DeleteDirectory(string path)
        {
            if (IsReadOnly())
                throw ReadOnlyException;
            path = FixPath(path);
            Entry ent = GetEntry(path);
            if (ent == null)
                throw EntryNotFoundException;
            if (!ent.IsFolder)
                throw EntryNotFoundException;
            return DeleteEntry(ent);
        }

        /// <summary>
        /// Creates a file at the specified path.
        /// </summary>
        /// <param name="path">The path of the new file.</param>
        /// <returns>True if created successfully.</returns>
        public bool CreateFile(string path)
        {
            if (IsReadOnly())
                throw ReadOnlyException;
            path = FixPath(path);
            if (EntryExists(path))
                throw AlreadyExistsException;
            return CreateEntry(path, false);
        }

        /// <summary>
        /// Deletes a file at the specified path.
        /// </summary>
        /// <param name="path">The path of the directory to delete.</param>
        /// <returns>True if deleted successfully.</returns>
        public bool DeleteFile(string path)
        {
            if (IsReadOnly())
                throw ReadOnlyException;
            path = FixPath(path);
            Entry ent = GetEntry(path);
            if (ent == null)
                throw EntryNotFoundException;
            if (ent.IsFolder)
                throw EntryNotFoundException;
            return DeleteEntry(ent);
        }

        /// <summary>
        /// Deletes an entry, directory or file, at the specified path.
        /// </summary>
        /// <param name="path">The path of the entry to delete.</param>
        /// <returns>True if deleted successfully.</returns>
        public bool DeleteEntry(string path)
        {
            if (IsReadOnly())
                throw ReadOnlyException;
            path = FixPath(path);
            Entry ent = GetEntry(path);
            if (ent == null)
                throw EntryNotFoundException;
            return DeleteEntry(ent);
        }

        /// <summary>
        /// Copies a file or folder from one path to another new path.
        /// </summary>
        /// <param name="path">The path of the file or folder to copy.</param>
        /// <param name="newPath">The path of the duplicate file.</param>
        /// <param name="progressChanged">The delegate for the progress changing.</param>
        /// <returns>True if successful.</returns>
        public bool CopyEntry(string path, string newPath, IO.CoreIO.FileTransferProgressChanged progressChanged = null, string actionText = "Copying")
        {
            if (IsReadOnly())
                throw ReadOnlyException;
            path = FixPath(path);
            newPath = FixPath(newPath);
            Entry ent = GetEntry(path);
            if (ent == null)
                throw EntryNotFoundException;
            if (EntryExists(newPath))
                throw AlreadyExistsException;

            // Make sure we're not copying a folder into a child of itself, no matter how deep
            // that would recursively copy forever
            if (ent.IsFolder && newPath.ToLower().StartsWith(ent.Path.ToLower()))
                throw new Exception("Cannot copy a directory to a location under that directory.");

            if (ent.IsFolder)
            {
                CreateDirectory(newPath);
                List<Entry> entries = GetEntries(path);
                for (int i = 0; i < entries.Count; i++)
                    CopyEntry(entries[i].Path, newPath + "\\" + entries[i].Name, progressChanged);
            }
            else
            {
                CreateFile(newPath);
                IO.CoreIO source = GetFileIO(path);
                IO.CoreIO dest = GetFileIO(newPath);
                source.TransferTo(dest, progressChanged, actionText + " " + ent.Name);
                source.Close();
                dest.Close();
            }
            return true;
        }

        /// <summary>
        /// Moves a file to a new location.
        /// </summary>
        /// <param name="path">The path of the file to move.</param>
        /// <param name="newPath">The new path of the file after the move.</param>
        /// <param name="progressChanged">The delegate for the progress changing.</param>
        /// <returns>True if successfully moved</returns>
        public virtual bool MoveEntry(string path, string newPath, IO.CoreIO.FileTransferProgressChanged progressChanged = null)
        {
            if (!CopyEntry(path, newPath, progressChanged, "Moving"))
                return false;
            return DeleteEntry(path);
        }

        /// <summary>
        /// Gets a list of entries in a specified directory.
        /// </summary>
        /// <param name="path">The path of the directory.</param>
        /// <returns>A list of all entries in the directory.</returns>
        public List<Entry> GetEntries(string path)
        {
            path = FixPath(path);
            if (path != "" && path != "\\")
            {
                Entry ent = GetEntry(path);
                if (ent == null)
                    throw EntryNotFoundException;
                if (!ent.IsFolder)
                    throw NotADirectoryException;
                List<Entry> rtn = GetEntries(ent);
                rtn.Sort();
                return rtn;
            }
            List<Entry> entries = GetEntries((Entry)null); // null = root
            entries.Sort();
            return entries;
        }

        /// <summary>
        /// Gets a list of all file entries in a specified directory.
        /// </summary>
        /// <param name="path">The path of the directory.</param>
        /// <returns>A list of all file entries in the directory.</returns>
        public List<Entry> GetFiles(string path)
        {
            path = FixPath(path);
            List<Entry> entries = GetEntries(path);
            List<Entry> fentries = new List<Entry>();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].IsFolder == false)
                    fentries.Add(entries[i]);
            return fentries;
        }

        /// <summary>
        /// Gets a list of all file entries in a specified directory.
        /// </summary>
        /// <param name="entry">The directory entry to look in.</param>
        /// <returns>A list of all file entries in the directory.</returns>
        public List<Entry> GetFiles(Entry entry)
        {
            List<Entry> entries = GetEntries(entry);
            List<Entry> fentries = new List<Entry>();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].IsFolder == false)
                    fentries.Add(entries[i]);
            return fentries;
        }

        /// <summary>
        /// Gets a list of all directory entries in a specified directory.
        /// </summary>
        /// <param name="path">The path of the directory.</param>
        /// <returns>A list of all directory entries in the directory.</returns>
        public List<Entry> GetDirectories(string path)
        {
            path = FixPath(path);
            List<Entry> entries = GetEntries(path);
            List<Entry> fentries = new List<Entry>();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].IsFolder)
                    fentries.Add(entries[i]);
            return fentries;
        }

        /// <summary>
        /// Gets a list of all directory entries under a directory entry.
        /// </summary>
        /// <param name="entry">The directory entry to look in.</param>
        /// <returns>A list of all directory entries in the directory.</returns>
        public List<Entry> GetDirectories(Entry entry)
        {
            List<Entry> entries = GetEntries(entry);
            List<Entry> fentries = new List<Entry>();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].IsFolder)
                    fentries.Add(entries[i]);
            return fentries;
        }

        /// <summary>
        /// Renames an entry with a new specified name.
        /// </summary>
        /// <param name="path">The path of the entry to rename.</param>
        /// <param name="newName">The new name of the entry.</param>
        /// <returns>True if renamed successfully.</returns>
        public bool RenameEntry(string path, string newName)
        {
            if (IsReadOnly())
                throw ReadOnlyException;
            path = FixPath(path);
            if (!IsValidEntryName(newName))
                throw InvalidNameException;
            Entry ent = GetEntry(path);
            if (ent == null)
                throw EntryNotFoundException;
            return RenameEntry(ent, newName);
        }

        /// <summary>
        /// Returns the full path of an entry.
        /// </summary>
        /// <param name="entry">The entry to get the full path of.</param>
        /// <returns>The full path of an entry as a string.</returns>
        public string GetPath(Entry entry)
        {
            return GetEntryPath(entry);
        }

        /// <summary>
        /// Sets the data of a file to the specified data.
        /// </summary>
        /// <param name="filename">The file to set.</param>
        /// <param name="newData">The data to set to.</param>
        /// <returns>True if set successfully.</returns>
        public bool SetFileData(string path, byte[] newData)
        {
            if (IsReadOnly())
                return false;
            IO.CoreIO stream = new IO.CoreIO(newData, EndianStyle.BigEndian);
            AddFile(stream, path, true);
            stream.Close();
            return true;
        }

        /// <summary>
        /// Adds or overwrites a file into the filesystem with a specified path on the filesystem.
        /// </summary>
        /// <param name="fileIo">The CoreIO of the file we're adding.</param>
        /// <param name="filenameOnFilesystem">The full path filename on the filesystem we want to use.</param>
        /// <param name="overwrite">Overwrite the file if it already exists.</param>
        /// <returns>True if added successfully.</returns>
        public virtual bool AddFile(IO.CoreIO fileIo, string filenameOnFilesystem, bool overwrite, IO.CoreIO.FileTransferProgressChanged progressChanged = null)
        {
            if (IsReadOnly())
                return false;

            filenameOnFilesystem = FixPath(filenameOnFilesystem);
            if (!overwrite)
            {
                if (EntryExists(filenameOnFilesystem))
                    throw AlreadyExistsException;
            }
            else
            {
                if (DirectoryExists(filenameOnFilesystem))
                    throw AlreadyExistsException; // we cant overwrite a folder with a file
            }

            if (FileExists(filenameOnFilesystem))
            {
                DeleteFile(filenameOnFilesystem);
                CreateFile(filenameOnFilesystem);
            }
            IO.CoreIO stream = GetEntryStream(filenameOnFilesystem, System.IO.FileMode.Create);
            fileIo.TransferTo(stream, progressChanged, "Injecting " + Utils.GetNameFromPath(filenameOnFilesystem));
            stream.Close();
            TempCloseIO();
            return true;
        }


        /// <summary>
        /// Adds or overwrites a file into the filesystem with a specified path on the filesystem.
        /// </summary>
        /// <param name="filenameOnDisk">The filename on the computer.</param>
        /// <param name="fileNameOnFilesystem">The full path filename on the filesystem we want to use.</param>
        /// <param name="overwrite">Overwrite the file if it already exists.</param>
        /// <returns>True if added successfully.</returns>
        public bool AddFile(string filenameOnDisk, string fileNameOnFilesystem, bool overwrite, IO.CoreIO.FileTransferProgressChanged progressChanged = null)
        {
            if (IsReadOnly())
                return false;
            if (!System.IO.File.Exists(filenameOnDisk))
                throw new System.IO.FileNotFoundException();
            fileNameOnFilesystem = FixPath(fileNameOnFilesystem);
            IO.CoreIO stream = new IO.CoreIO(filenameOnDisk, IO.EndianStyle.BigEndian);
            try
            {
                bool rtn = AddFile(stream, fileNameOnFilesystem, overwrite, progressChanged);
                stream.Close();
                return rtn;
            }
            catch (Exception ex) { stream.Close(); throw ex; }
        }

        /// <summary>
        /// Injects a file or folder from the computer into a path on the filesystem.
        /// </summary>
        /// <param name="filename">The filename on the computer.</param>
        /// <param name="basePath">The path to inject the file into.</param>
        /// <param name="overwrite">Overwrite the file if it already exists.</param>
        /// <returns>True if injected successfully.</returns>
        public bool InjectFromComputer(string filename, string basePath, bool overwrite, IO.CoreIO.FileTransferProgressChanged progressChanged = null)
        {
            if (IsReadOnly())
                return false;
            basePath = FixPath(basePath);
            if (basePath != "")
                basePath += "\\";
            if (System.IO.File.Exists(filename))
            {
                System.IO.FileInfo info = new System.IO.FileInfo(filename);
                IO.CoreIO stream = new IO.CoreIO(filename, EndianStyle.BigEndian);
                AddFile(stream, basePath + info.Name, overwrite, progressChanged);
                stream.Close();
                TempCloseIO();
                return true;
            }
            else if (System.IO.Directory.Exists(filename))
            {
                System.IO.DirectoryInfo info = new System.IO.DirectoryInfo(filename);
                if (!EntryExists(basePath + info.Name))
                    CreateDirectory(basePath + info.Name);
                else if (FileExists(basePath + info.Name))
                    return false;
                string[] files = System.IO.Directory.GetFiles(filename);
                for (int i = 0; i < files.Length; i++)
                    InjectFromComputer(files[i], basePath + info.Name, overwrite, progressChanged);
                string[] folders = System.IO.Directory.GetDirectories(filename);
                for (int i = 0; i < folders.Length; i++)
                    InjectFromComputer(files[i], basePath + info.Name, overwrite, progressChanged);
                return true;
            }
            else
            {
                return false;
            }   
        }

        /// <summary>
        /// Extracts a file or directory from the filesystem and writes it to the computer.
        /// </summary>
        /// <param name="pathOnFilesystem">The path on the filesystem to use.</param>
        /// <param name="pathOnComputer">The base directory on the computer to save to.</param>
        /// <returns>The path written to.</returns>
        public virtual string ExtractToComputerDirectory(string pathOnFilesystem, string directoryOnComputer, IO.CoreIO.FileTransferProgressChanged progressChanged = null)
        {
            // Make sure the path exists on the filesystem
            pathOnFilesystem = FixPath(pathOnFilesystem);
            if (!EntryExists(pathOnFilesystem))
                return null;

            // Make sure the base directory exists on the computer, if not create it
            if (!System.IO.Directory.Exists(directoryOnComputer))
                System.IO.Directory.CreateDirectory(directoryOnComputer);

            // Get the entry we're extracting
            Entry ent = GetEntry(pathOnFilesystem);
            if (ent.IsFolder)
            {
                // It's a directory so lets loop through it
                List<Entry> subEntries = GetEntries(ent);
                for (int i = 0; i < subEntries.Count; i++)
                    ExtractToComputerDirectory(subEntries[i].Path, directoryOnComputer + "\\" + ent.Name, progressChanged);
            }
            else
            {
                // It's just a file so let's extract that
                //System.IO.File.WriteAllBytes(directoryOnComputer + "\\" + ent.Name, GetFileData(pathOnFilesystem));
                IO.CoreIO output = new CoreIO(directoryOnComputer + "\\" + ent.Name, EndianStyle.BigEndian, System.IO.FileMode.Create);
                IO.CoreIO ioOnFs = GetFileIO(pathOnFilesystem);
                ioOnFs.TransferTo(output, progressChanged, "Extracting " + ent.Name);
                ioOnFs.Close();
                output.Close();
            }

            TempCloseIO();

            return directoryOnComputer + "\\" + ent.Name;
        }

        /// <summary>
        /// Extracts a file or directory from the filesystem and writes it to the computer.
        /// </summary>
        /// <param name="pathOnFilesystem">The path on the filesystem to use.</param>
        /// <param name="pathOnComputer">The base directory on the computer to save to.</param>
        /// <returns>The path written to.</returns>
        public virtual string ExtractFileToComputer(string pathOnFilesystem, string computerFilename, IO.CoreIO.FileTransferProgressChanged progressChanged = null)
        {
            // Make sure the path exists on the filesystem
            pathOnFilesystem = FixPath(pathOnFilesystem);
            if (!EntryExists(pathOnFilesystem))
                return null;

            string baseDirectory = computerFilename.Substring(0,
                computerFilename.Length - Utils.GetNameFromPath(computerFilename).Length);
            if (!System.IO.Directory.Exists(baseDirectory))
                System.IO.Directory.CreateDirectory(baseDirectory);

            // Get the entry we're extracting
            Entry ent = GetEntry(pathOnFilesystem);
            if (ent.IsFolder)
            {
                // It's a directory so lets loop through it
                List<Entry> subEntries = GetEntries(ent);
                for (int i = 0; i < subEntries.Count; i++)
                    ExtractToComputerDirectory(subEntries[i].Path, computerFilename + "\\" + ent.Name, progressChanged);
            }
            else
            {
                // It's just a file so let's extract that
                //System.IO.File.WriteAllBytes(directoryOnComputer + "\\" + ent.Name, GetFileData(pathOnFilesystem));
                IO.CoreIO output = new CoreIO(computerFilename, EndianStyle.BigEndian, System.IO.FileMode.Create);
                IO.CoreIO ioOnFs = GetFileIO(pathOnFilesystem);
                ioOnFs.TransferTo(output, progressChanged, "Extracting " + ent.Name);
                ioOnFs.Close();
                output.Close();
            }

            TempCloseIO();

            return computerFilename;
        }

        /// <summary>
        /// Gets a stream of a file entry.
        /// </summary>
        /// <param name="path">The path of the entry.</param>
        /// <returns>A stream of the entry's contents.</returns>
        protected IO.CoreIO GetFileIO(string path)
        {
            path = FixPath(path);
            if (!FileExists(path))
                throw EntryNotFoundException;
            return GetEntryStream(path, System.IO.FileMode.Open);
        }

        /// <summary>
        /// Reads all of the data in a file and returns it as a byte array.
        /// </summary>
        /// <param name="path">The path of the file.</param>
        /// <returns>The data in the file as a byte array.</returns>
        public byte[] GetFileData(string path)
        {
            return GetFileData(path, -1);
        }

        /// <summary>
        /// Reads an amount of data in a file and returns it as a byte array.
        /// </summary>
        /// <param name="path">the path of the file.</param>
        /// <param name="length">The length to read in.</param>
        /// <returns>The data in the file as a byte array.</returns>
        public byte[] GetFileData(string path, int length)
        {
            path = FixPath(path);

            // Create the streams
            IO.CoreIO stream = GetFileIO(path);
            IO.CoreIO temp = new IO.CoreIO();

            // Copy the data over
            stream.CopyTo(temp);
            temp.Position = 0;

            // Clean up
            stream.Close();
            byte[] rtn = { };
            if (length < 0)
                rtn = temp.ToArray();
            else if (length > 0)
                rtn = temp.Reader.ReadBytes(Math.Min(length, (int)temp.Length));

            temp.Close();
            TempCloseIO();
            return rtn;
        }

        /// <summary>
        /// Closes the filesystem. No way of opening it back up. Close it and leave it.
        /// The filesystem will not know it's been closed.
        /// </summary>
        public void Close()
        {
            CloseFilesystem();
        }

        /// <summary>
        /// Closes the filesystem when called to dispose. Helps out if you forgot to close the filesystem
        /// and the garbage collector steps in.
        /// </summary>
        public void Dispose()
        {
            // Let's make sure our info cache has been disposed
            for (int i = 0; i < infoCache.Count; i++)
                if (infoCache.ElementAt(i).Value.Image != null)
                    infoCache.ElementAt(i).Value.Image.Dispose();
            Close();
        }

    }

    /// <summary>
    /// An entry in the filesystem. Can be a file or a directory.
    /// </summary>
    public abstract class Entry : IComparable<Entry>
    {

        protected string basePath = "";
        protected string name = "";
        protected string friendlyName = "";
        protected long size = 0;
        protected DateTime modified = DateTime.Now;
        protected DateTime created = DateTime.Now;
        protected System.Drawing.Bitmap image;

        /// <summary>
        /// Creates a new entry.
        /// </summary>
        /// <param name="basePath">The path of the directory that this entry is located in.</param>
        public Entry(string basePath)
        {
            this.basePath = StandardFs.FixPath(basePath);
        }

        /// <summary>
        /// The name of this entry.
        /// </summary>
        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        /// <summary>
        /// The full path of this entry.
        /// </summary>
        public string Path
        {
            get
            {
                if (basePath == "")
                    return name;
                return basePath + "\\" + name;
            }
        }

        /// <summary>
        /// The path that this entry belongs to.
        /// </summary>
        public string BasePath
        {
            get { return this.basePath; }
        }

        /// <summary>
        /// An alias/nickname for this entry.
        /// </summary>
        public string FriendlyName
        {
            get 
            {
                if (friendlyName == "")
                    return name;
                return friendlyName; 
            }
            set { friendlyName = value; }
        }

        /// <summary>
        /// The size of this entry.
        /// </summary>
        public long Size
        {
            get { return size; }
        }

        /// <summary>
        /// The time this entry was last modified.
        /// </summary>
        public DateTime Modified
        {
            get { return modified; }
        }

        /// <summary>
        /// The time this entry was created.
        /// </summary>
        public DateTime Created
        {
            get { return created; }
        }

        /// <summary>
        /// The image for this entry.
        /// </summary>
        public System.Drawing.Bitmap Image
        {
            get { return image; }
            set { image = value; }
        }

        public enum EntryType
        {
            File = 0,
            Folder = 1
        }

        /// <summary>
        /// The type of this entry.
        /// </summary>
        public abstract EntryType Type
        {
            get;
        }

        /// <summary>
        /// True if this entry is a folder.
        /// </summary>
        public bool IsFolder
        {
            get 
            {
                if (this.Type == EntryType.Folder)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Compares this entry to another one.
        /// </summary>
        /// <param name="other">The entry to compare to.</param>
        /// <returns>The result of the comparison.</returns>
        public int CompareTo(Entry other)
        {
            return this.name.CompareTo(other.name);
        }

    }

    /// <summary>
    /// An entry in the filesystem that is a file.
    /// </summary>
    public abstract class FileEntry : Entry
    {

        public FileEntry(string basePath) : base(basePath)
        {
        }

        public override EntryType Type
        {
            get { return EntryType.File;  }
        }

    }

    /// <summary>
    /// An entry in the filesystem that is a directory.
    /// </summary>
    public abstract class DirectoryEntry : Entry
    {

        public DirectoryEntry(string basePath) : base(basePath)
        {
        }

        public override Entry.EntryType Type
        {
            get { return EntryType.Folder; }
        }

    }

}
