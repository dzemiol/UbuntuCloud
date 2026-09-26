using UbuntuCloud.Models;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Runtime.InteropServices;

namespace UbuntuCloud.Services
{
    public class DiskSpaceInfo
    {
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public long UsedBytes => TotalBytes - FreeBytes;
        public double UsedPercentage => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
    }

    public class FileService
    {
        private readonly string _baseStoragePath;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct Statvfs
        {
            public ulong f_bsize;
            public ulong f_frsize;
            public ulong f_blocks;
            public ulong f_bfree;
            public ulong f_bavail;
            public ulong f_files;
            public ulong f_ffree;
            public ulong f_favail;
            public ulong f_fsid;
            public ulong f_flag;
            public ulong f_namemax;
        }

        [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int statvfs(string path, out Statvfs buf);

        public FileService(IConfiguration configuration)
        {
            _baseStoragePath = configuration["StoragePath"] ?? "/mnt/dysk500gb/UbuntuCloud";

            if (!Directory.Exists(_baseStoragePath))
            {
                Directory.CreateDirectory(_baseStoragePath);
            }
        }

        public DiskSpaceInfo GetDiskSpaceInfo()
        {
            try
            {
                string targetPath = "/var/ubuntucloud/storage";

                if (!Directory.Exists(targetPath))
                {
                    Directory.CreateDirectory(targetPath);
                }

                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                {
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "df",
                            Arguments = $"-B1 \"{targetPath}\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length >= 2)
                    {
                        var parts = lines[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4 &&
                            long.TryParse(parts[1], out long total) &&
                            long.TryParse(parts[3], out long free))
                        {
                            return new DiskSpaceInfo
                            {
                                TotalBytes = total,
                                FreeBytes = free
                            };
                        }
                    }
                }
            }
            catch { }

            return new DiskSpaceInfo { TotalBytes = 0, FreeBytes = 0 };
        }

        public Task<List<FileItem>> GetFilesAsync(string subPath)
        {
            var targetPath = Path.Combine(_baseStoragePath, subPath);

            if (!Path.GetFullPath(targetPath).StartsWith(Path.GetFullPath(_baseStoragePath)))
            {
                throw new UnauthorizedAccessException("Dostęp zabroniony.");
            }

            if (!Directory.Exists(targetPath))
            {
                return Task.FromResult(new List<FileItem>());
            }

            var dirInfo = new DirectoryInfo(targetPath);
            var items = new List<FileItem>();

            foreach (var dir in dirInfo.GetDirectories())
            {
                items.Add(new FileItem
                {
                    Name = dir.Name,
                    IsDirectory = true,
                    LastModified = dir.LastWriteTime,
                    Size = GetDirectorySize(dir)
                });
            }

            foreach (var file in dirInfo.GetFiles())
            {
                items.Add(new FileItem
                {
                    Name = file.Name,
                    IsDirectory = false,
                    LastModified = file.LastWriteTime,
                    Size = file.Length
                });
            }

            return Task.FromResult(items);
        }

        private long GetDirectorySize(DirectoryInfo dirInfo)
        {
            long size = 0;

            try
            {
                foreach (var file in dirInfo.GetFiles())
                {
                    size += file.Length;
                }

                foreach (var subDir in dirInfo.GetDirectories())
                {
                    size += GetDirectorySize(subDir);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }

            return size;
        }

        public async Task SaveFileAsync(string subPath, string fileName, Stream stream, CancellationToken cancellationToken = default)
        {
            var targetDirectory = Path.Combine(_baseStoragePath, subPath);
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var filePath = Path.Combine(targetDirectory, fileName);

            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await stream.CopyToAsync(fileStream, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(filePath))
                {
                    try { File.Delete(filePath); } catch { }
                }
                throw;
            }
        }

        public async Task SaveFileWithRelativePathAsync(string currentSubPath, string relativePath, Stream stream, CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(_baseStoragePath, currentSubPath, relativePath);

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await stream.CopyToAsync(fileStream, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(fullPath))
                {
                    try { File.Delete(fullPath); } catch { }
                }
                throw;
            }
        }

        public Task CreateDirectoryAsync(string subPath, string folderName)
        {
            var targetPath = Path.Combine(_baseStoragePath, subPath, folderName);
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }
            return Task.CompletedTask;
        }

        public Task MoveItemAsync(string sourceSubPath, string itemName, string targetDirectoryName)
        {
            var sourcePath = Path.Combine(_baseStoragePath, sourceSubPath, itemName);
            var targetDirectoryPath = Path.Combine(_baseStoragePath, sourceSubPath, targetDirectoryName, itemName);

            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, targetDirectoryPath);
            }
            else if (Directory.Exists(sourcePath))
            {
                Directory.Move(sourcePath, targetDirectoryPath);
            }

            return Task.CompletedTask;
        }

        public Task MoveItemUpAsync(string currentSubPath, string itemName)
        {
            if (string.IsNullOrEmpty(currentSubPath))
            {
                return Task.CompletedTask;
            }

            var sourcePath = Path.Combine(_baseStoragePath, currentSubPath, itemName);
            var parentSubPath = Path.GetDirectoryName(currentSubPath) ?? "";
            var targetPath = Path.Combine(_baseStoragePath, parentSubPath, itemName);

            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, targetPath);
            }
            else if (Directory.Exists(sourcePath))
            {
                Directory.Move(sourcePath, targetPath);
            }

            return Task.CompletedTask;
        }

        public async Task DeleteItemAsync(string subPath, string itemName)
        {
            var targetPath = Path.Combine(_baseStoragePath, subPath, itemName);

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                        break;
                    }
                    else if (Directory.Exists(targetPath))
                    {
                        Directory.Delete(targetPath, recursive: true);
                        break;
                    }
                    else
                    {
                        break;
                    }
                }
                catch (IOException)
                {
                    await Task.Delay(200);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(200);
                }
            }
        }

        public string GetPhysicalPath(string subPath, string fileName)
        {
            return Path.Combine(_baseStoragePath, subPath, fileName);
        }

        public string GetFullPath(string subPath, string fileName)
        {
            return string.IsNullOrEmpty(subPath)
                ? Path.Combine(_baseStoragePath, fileName)
                : Path.Combine(_baseStoragePath, subPath, fileName);
        }

        public async Task<byte[]> GetFolderZipBytesAsync(string subPath, string folderName)
        {
            var folderPath = Path.Combine(_baseStoragePath, subPath, folderName);

            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException("Folder nie istnieje.");
            }

            using (var memoryStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    AddDirectoryToZip(archive, folderPath, folderName);
                }

                return memoryStream.ToArray();
            }
        }

        private void AddDirectoryToZip(ZipArchive archive, string sourceDir, string entryPrefix)
        {
            var dirInfo = new DirectoryInfo(sourceDir);

            foreach (var file in dirInfo.GetFiles())
            {
                var entryName = Path.Combine(entryPrefix, file.Name).Replace('\\', '/');
                archive.CreateEntryFromFile(file.FullName, entryName, CompressionLevel.Fastest);
            }

            foreach (var subDir in dirInfo.GetDirectories())
            {
                AddDirectoryToZip(archive, subDir.FullName, Path.Combine(entryPrefix, subDir.Name));
            }
        }

        public string CreateFolderZipStream(string subPath, string folderName)
        {
            string targetDirPath = string.IsNullOrEmpty(subPath)
                ? Path.Combine(_baseStoragePath, folderName)
                : Path.Combine(_baseStoragePath, subPath, folderName);

            string tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");

            ZipFile.CreateFromDirectory(targetDirPath, tempZipPath, CompressionLevel.Fastest, includeBaseDirectory: true);

            return tempZipPath;
        }
    }
}