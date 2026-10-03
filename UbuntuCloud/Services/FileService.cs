using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using UbuntuCloud.Models;

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

        private string GetValidFullPath(string subPath, string? fileName = null)
        {
            string combined = string.IsNullOrEmpty(fileName)
                ? Path.Combine(_baseStoragePath, subPath ?? "")
                : Path.Combine(_baseStoragePath, subPath ?? "", fileName);

            string fullPath = Path.GetFullPath(combined);
            string baseFullPath = Path.GetFullPath(_baseStoragePath);

            if (!fullPath.StartsWith(baseFullPath, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Dostęp zabroniony: Próba wyjścia poza katalog bazowy.");
            }

            return fullPath;
        }

        public DiskSpaceInfo GetDiskSpaceInfo()
        {
            try
            {
                var drive = new DriveInfo(_baseStoragePath);
                if (drive.IsReady)
                {
                    return new DiskSpaceInfo
                    {
                        TotalBytes = drive.TotalSize,
                        FreeBytes = drive.AvailableFreeSpace
                    };
                }
            }
            catch
            {
                // Fallback dla specyficznych podmontowanych zasobów Linuksa
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    if (statvfs(_baseStoragePath, out var stat) == 0)
                    {
                        ulong total = stat.f_blocks * stat.f_frsize;
                        ulong free = stat.f_bavail * stat.f_frsize;

                        return new DiskSpaceInfo
                        {
                            TotalBytes = (long)total,
                            FreeBytes = (long)free
                        };
                    }
                }
            }

            return new DiskSpaceInfo { TotalBytes = 0, FreeBytes = 0 };
        }

        public Task<List<FileItem>> GetFilesAsync(string subPath)
        {
            string targetPath = GetValidFullPath(subPath);

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
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }

            return size;
        }

        public async Task SaveFileAsync(string subPath, string fileName, Stream stream, CancellationToken cancellationToken = default)
        {
            string targetDirectory = GetValidFullPath(subPath);
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            string filePath = GetValidFullPath(subPath, fileName);

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
            string fullPath = GetValidFullPath(currentSubPath, relativePath);

            string? directory = Path.GetDirectoryName(fullPath);
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
            string targetPath = GetValidFullPath(subPath, folderName);
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }
            return Task.CompletedTask;
        }

        public Task MoveItemAsync(string sourceSubPath, string itemName, string targetDirectoryName)
        {
            string sourcePath = GetValidFullPath(sourceSubPath, itemName);
            string targetSubPath = Path.Combine(sourceSubPath ?? "", targetDirectoryName);
            string targetPath = GetValidFullPath(targetSubPath, itemName);

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

        public Task MoveItemUpAsync(string currentSubPath, string itemName)
        {
            if (string.IsNullOrEmpty(currentSubPath))
            {
                return Task.CompletedTask;
            }

            string sourcePath = GetValidFullPath(currentSubPath, itemName);
            string parentSubPath = Path.GetDirectoryName(currentSubPath) ?? "";
            string targetPath = GetValidFullPath(parentSubPath, itemName);

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
            string targetPath = GetValidFullPath(subPath, itemName);

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
            return GetValidFullPath(subPath, fileName);
        }

        public string GetFullPath(string subPath, string fileName)
        {
            return GetValidFullPath(subPath, fileName);
        }

        public string CreateFolderZipStream(string subPath, string folderName, CancellationToken cancellationToken = default)
        {
            string targetDirPath = GetValidFullPath(subPath, folderName);

            if (!Directory.Exists(targetDirPath))
            {
                throw new DirectoryNotFoundException($"Katalog '{folderName}' nie istnieje.");
            }

            string tempDir = Path.Combine(_baseStoragePath, ".temp");
            Directory.CreateDirectory(tempDir);

            string tempZipPath = Path.Combine(tempDir, $"{Guid.NewGuid()}.zip");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && IsPigzAvailable())
            {
                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "bash",
                        Arguments = $"-c \"cd \\\"{targetDirPath}\\\" && zip -q -r -1 - . | pigz -p {Environment.ProcessorCount} > \\\"{tempZipPath}\\\"\"",
                        RedirectStandardOutput = false,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(processInfo))
                    {
                        if (process != null)
                        {
                            using (cancellationToken.Register(() => { try { process.Kill(true); } catch { } }))
                            {
                                process.WaitForExit();

                                if (!cancellationToken.IsCancellationRequested && process.ExitCode == 0 && File.Exists(tempZipPath) && new FileInfo(tempZipPath).Length > 0)
                                {
                                    return tempZipPath;
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (File.Exists(tempZipPath))
            {
                try { File.Delete(tempZipPath); } catch { }
            }

            cancellationToken.ThrowIfCancellationRequested();

            ZipFile.CreateFromDirectory(targetDirPath, tempZipPath, CompressionLevel.Fastest, includeBaseDirectory: true);

            return tempZipPath;
        }

        private bool IsPigzAvailable()
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "pigz",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}