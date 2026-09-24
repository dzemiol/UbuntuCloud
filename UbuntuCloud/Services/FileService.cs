using UbuntuCloud.Models;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;

namespace UbuntuCloud.Services
{
    public class FileService
    {
        private readonly string _baseStoragePath;

        public FileService(IConfiguration configuration)
        {
            _baseStoragePath = configuration["StoragePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "Storage");

            if (!Directory.Exists(_baseStoragePath))
            {
                Directory.CreateDirectory(_baseStoragePath);
            }
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

            // Unikalna ścieżka dla pliku tymczasowego na dysku
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");

            // Pakowanie bezpośrednio na dysk (brak limitu RAM-u)
            ZipFile.CreateFromDirectory(targetDirPath, tempZipPath, CompressionLevel.Fastest, includeBaseDirectory: true);

            return tempZipPath;
        }
    }
}