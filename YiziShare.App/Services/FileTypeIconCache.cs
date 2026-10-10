using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YiziShare.App.Services;

/// <summary>
/// 文件类型图标缓存：按扩展名从 Windows Shell 提取系统关联图标（资源管理器同源）。<br/>
/// 使用 SHGFI_USEFILEATTRIBUTES 只读扩展名、不触碰文件本身——UNC 不可达也不会卡顿；<br/>
/// 结果按扩展名缓存并 Freeze，跨线程安全。
/// </summary>
public static class FileTypeIconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>获取某文件名对应类型的 Shell 图标；失败返回 null（调用方回退矢量图标）</summary>
    public static ImageSource? GetIconImage(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return Cache.GetOrAdd(ext, ExtractForExtension);
    }

    private static ImageSource? ExtractForExtension(string ext)
    {
        try
        {
            var shinfo = new SHFILEINFO();
            // USEFILEATTRIBUTES：只按扩展名查注册表图标，不访问磁盘
            var result = SHGetFileInfo(
                "file" + ext,
                FILE_ATTRIBUTE_NORMAL,
                ref shinfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);

            if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally
            {
                _ = DestroyIcon(shinfo.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint fileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
