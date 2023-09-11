namespace System.IO
{
    internal static partial class ArchivingUtils
    {
        public static bool IsDirEmpty(string directoryFullName)
        {
            using (IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(directoryFullName).GetEnumerator())
                return !enumerator.MoveNext();
        }
    }
}
