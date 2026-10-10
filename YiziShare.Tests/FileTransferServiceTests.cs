using YiziShare.Core;

namespace YiziShare.Tests;

public class FileTransferServiceTests : IDisposable
{
    private readonly string _root;

    public FileTransferServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"yizishare-transfer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class DelegateProgress : IProgress<TransferProgress>
    {
        private readonly Action<TransferProgress> _action;
        public DelegateProgress(Action<TransferProgress> action) => _action = action;
        public void Report(TransferProgress value) => _action(value);
    }

    private static byte[] RandomBytes(int length)
    {
        var data = new byte[length];
        new Random(42).NextBytes(data);
        return data;
    }

    [Fact]
    public async Task CopyFile_ReportsMonotonicProgressAndFinalEqualsTotal()
    {
        var srcDir = Path.Combine(_root, "src");
        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(destDir);
        var data = RandomBytes(3 * 1024 * 1024 + 777); // 跨多个 1MiB 分块
        var src = Path.Combine(srcDir, "big.bin");
        await File.WriteAllBytesAsync(src, data);

        var reports = new List<TransferProgress>();
        var progress = new DelegateProgress(p =>
        {
            if (reports.Count == 0 || p.BytesCopied >= reports[^1].BytesCopied)
                reports.Add(p);
        });

        await FileTransferService.CopyFileAsync(src, destDir, progress, CancellationToken.None);

        var dest = Path.Combine(destDir, "big.bin");
        Assert.True(File.Exists(dest));
        Assert.Equal(await File.ReadAllBytesAsync(src), await File.ReadAllBytesAsync(dest));

        var last = reports[^1];
        Assert.Equal(data.Length, last.TotalBytes);
        Assert.Equal(data.Length, last.BytesCopied);
        Assert.True(reports.Count > 1, "应有多条进度上报");
    }

    [Fact]
    public async Task CopyFile_Cancelled_DeletesPartialFileAndThrows()
    {
        var srcDir = Path.Combine(_root, "src");
        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(destDir);
        var data = RandomBytes(16 * 1024 * 1024);
        var src = Path.Combine(srcDir, "huge.bin");
        await File.WriteAllBytesAsync(src, data);

        using var cts = new CancellationTokenSource();
        var progress = new DelegateProgress(p =>
        {
            if (p.BytesCopied >= 1024 * 1024) cts.Cancel();
        });

        var dest = Path.Combine(destDir, "huge.bin");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FileTransferService.CopyFileAsync(src, destDir, progress, cts.Token));

        Assert.False(File.Exists(dest), "取消后应清理半成品文件");
    }

    [Fact]
    public async Task CopyDirectory_CopiesTreeWithAggregatedProgress()
    {
        var srcDir = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(srcDir, "sub"));
        var f1 = RandomBytes(1024 * 100);
        var f2 = RandomBytes(1024 * 200);
        var f3 = RandomBytes(1024 * 50);
        await File.WriteAllBytesAsync(Path.Combine(srcDir, "a.bin"), f1);
        await File.WriteAllBytesAsync(Path.Combine(srcDir, "sub", "b.bin"), f2);
        await File.WriteAllBytesAsync(Path.Combine(srcDir, "sub", "c.bin"), f3);

        var destDir = Path.Combine(_root, "out");
        TransferProgress last = default;
        var progress = new DelegateProgress(p => last = p);

        await FileTransferService.CopyDirectoryAsync(srcDir, destDir, progress, CancellationToken.None);

        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(srcDir, "a.bin")),
                     await File.ReadAllBytesAsync(Path.Combine(destDir, "tree", "a.bin")));
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(srcDir, "sub", "b.bin")),
                     await File.ReadAllBytesAsync(Path.Combine(destDir, "tree", "sub", "b.bin")));
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(srcDir, "sub", "c.bin")),
                     await File.ReadAllBytesAsync(Path.Combine(destDir, "tree", "sub", "c.bin")));

        long expectedTotal = f1.Length + f2.Length + f3.Length;
        Assert.Equal(expectedTotal, last.TotalBytes);
        Assert.Equal(expectedTotal, last.BytesCopied);
    }

    [Fact]
    public async Task CopyFile_EmptySource_CompletesWithFullProgress()
    {
        var srcDir = Path.Combine(_root, "src");
        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(destDir);
        var src = Path.Combine(srcDir, "empty.bin");
        await File.WriteAllBytesAsync(src, Array.Empty<byte>());

        TransferProgress last = default;
        var progress = new DelegateProgress(p => last = p);

        await FileTransferService.CopyFileAsync(src, destDir, progress, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(destDir, "empty.bin")));
        Assert.Equal(0, last.TotalBytes);
        Assert.Equal(0, last.BytesCopied);
    }
}
