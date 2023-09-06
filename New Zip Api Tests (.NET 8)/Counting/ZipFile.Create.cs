// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Enumeration;

namespace Counting
{
    public static partial class ZipFile
    {
        private static FileSystemEnumerable<(string, CreateEntryType)> CreateEnumerableForCreate(string directoryFullPath)
            => new FileSystemEnumerable<(string, CreateEntryType)>(directoryFullPath,
                static (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.IsDirectory ? CreateEntryType.Directory : CreateEntryType.File),
                new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false });

        public static void CreateFromDirectory(string sourceDirectoryName, PositionWrapperStream destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite)
            {
                throw new ArgumentException(nameof(destination));
            }

            sourceDirectoryName = Path.GetFullPath(sourceDirectoryName);

            using ZipArchive archive = new(destination, true, null);

            DirectoryInfo di = new(sourceDirectoryName);

            string basePath = di.FullName;

            FileSystemEnumerable<(string, CreateEntryType)> fse = CreateEnumerableForCreate(di.FullName);

            foreach ((string fullPath, CreateEntryType type) in fse)
            {
                switch (type)
                {
                    case CreateEntryType.File:
                        {
                            string entryName = ArchivingUtils.EntryFromPath(fullPath.AsSpan(basePath.Length));
                            ZipFileExtensions.DoCreateEntryFromFile(archive, fullPath, entryName);
                        }
                        break;
                    case CreateEntryType.Directory:
                        if (ArchivingUtils.IsDirEmpty(fullPath))
                        {
                            string entryName = ArchivingUtils.EntryFromPath(fullPath.AsSpan(basePath.Length), appendPathSeparator: true);
                            archive.CreateEntry(entryName);
                        }
                        break;
                    case CreateEntryType.Unsupported:
                    default:
                        throw new IOException();
                }
            }
        }

        private enum CreateEntryType
        {
            File,
            Directory,
            Unsupported
        }
    }
}
