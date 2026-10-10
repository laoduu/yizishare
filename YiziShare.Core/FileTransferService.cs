namespace YiziShare.Core;

/// <summary>单文件传输进度快照</summary>
public readonly record struct TransferProgress(long BytesCopied, long TotalBytes);

/// <summary>
/// 文件/目录传输服务：分块异步复制，支持进度回调与取消。
/// 取消或失败时清理半成品目标文件。
/// </summary>
public static class FileTransferService
{
    private const int CopyBufferSize = 1 * 1024 * 1024; // 1 MiB

    /// <summary>复制单个文件到目标目录（保留原文件名，覆盖已有文件）</summary>
    public static async Task CopyFileAsync(
        string sourcePath, string destDir,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var destPath = Path.Combine(destDir, Path.GetFileName(sourcePath));
        string? destParent = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destParent))
            Directory.CreateDirectory(destParent);

        long total = new FileInfo(sourcePath).Length;
        try
        {
            await CopyStreamAsync(sourcePath, destPath, baseOffset: 0, total, progress, ct).ConfigureAwait(false);
            progress?.Report(new TransferProgress(total, total));
        }
        catch (OperationCanceledException)
        {
            TryDelete(destPath);
            throw;
        }
        catch
        {
            TryDelete(destPath);
            throw;
        }
    }

    /// <summary>递归复制目录到 destDir 下的同名子目录，进度为所有文件汇总</summary>
    public static async Task CopyDirectoryAsync(
        string sourceDir, string destDir,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        string rootName = Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string rootDest = Path.Combine(destDir, rootName);
        Directory.CreateDirectory(rootDest);

        var pending = new List<(string Src, string Dest)>();
        CollectFiles(sourceDir, rootDest, pending);
        long total = pending.Sum(f => new FileInfo(f.Src).Length);

        long copied = 0;
        try
        {
            foreach (var (src, dest) in pending)
            {
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                copied += await CopyStreamAsync(src, dest, baseOffset: copied, total, progress, ct).ConfigureAwait(false);
                progress?.Report(new TransferProgress(copied, total));
            }
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(rootDest);
            throw;
        }
        catch
        {
            TryDeleteDirectory(rootDest);
            throw;
        }
    }

    private static void CollectFiles(string srcDir, string destDir, List<(string Src, string Dest)> pending)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(srcDir))
            pending.Add((file, Path.Combine(destDir, Path.GetFileName(file))));
        foreach (var dir in Directory.GetDirectories(srcDir))
            CollectFiles(dir, Path.Combine(destDir, Path.GetFileName(dir)), pending);
    }

    /// <summary>分块复制；进度 = baseOffset + 已复制字节（保证整体单调递增）</summary>
    private static async Task<long> CopyStreamAsync(
        string sourcePath, string destPath,
        long baseOffset, long total,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
        await using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);

        var buffer = new byte[CopyBufferSize];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            copied += read;
            progress?.Report(new TransferProgress(baseOffset + copied, total));
            ct.ThrowIfCancellationRequested();
        }
        return copied;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 清理失败不掩盖原始异常 */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* 清理失败不掩盖原始异常 */ }
    }
}
