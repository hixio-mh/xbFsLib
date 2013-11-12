xbFsLib
=======

A library containing file system classes used by the Xbox.

CoreIO
--------
CoreIO is a filestream wrapper class that allows for easy and efficent
file reading and writing. Effectively switches between using files, 
temporary cache files, and memory to give the most performance in a
given situation.

FATX
--------
FATX classes are included in this library. FATX is a modified FAT32
file system used on storage devices by the Xbox 360. The classes
allow all necessary operations to explore and edit FATX filesystems.
Fully supports Xbox memory cards, HDD's, and flash drives.

XDBF
--------
XDBF files are Xbox Dashboard Files. They're specified size file systems
that use allocated and free blocks to add, remove, and update data. If too
many blocks are attempting to be allocated, the XDBF class will automatically
make room for more free blocks to make the allocation work.
