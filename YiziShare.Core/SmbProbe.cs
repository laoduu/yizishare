using System.Runtime.InteropServices;
using System.Text;

namespace YiziShare.Core;

/// <summary>
/// SMB 底层探测（mpr.dll / netapi32 / kernel32 P/Invoke）：
/// IPC$ 连接探测拿真实 Win32 错误码、会话管理、NetShareEnum 共享枚举、OEM 编码获取
/// </summary>
public static class SmbProbe
{
    private const int RESOURCETYPE_DISK = 0x00000001;
    private const int RESOURCE_GLOBALNET = 0x00000002;
    private const int RESOURCEUSAGE_CONNECTABLE = 0x00000001;
    private const int STYPE_DISKTREE = 0;
    private const int STYPE_SPECIAL = unchecked((int)0x80000000);
    private const int STYPE_TEMPORARY = 0x40000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string LocalName;
        public string RemoteName;
        public string Comment;
        public string Provider;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHARE_INFO_1
    {
        public string NetName;
        public int Type;
        public string Remark;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NETRESOURCE netResource,
        string? password, string? userId, int dwFlags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int dwFlags, bool force);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(string serverName, int level, out IntPtr bufPtr,
        int prefMaxLen, out int entriesRead, out int totalEntries, IntPtr resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    /// <summary>
    /// 探测与 \\{ip}\IPC$ 的连接，返回真实 Win32 错误码（0=成功，如 5/86/1326/1219/1244…）。
    /// userName/password 为 null 时使用当前用户凭据；认证成功的会话保留供资源管理器直接复用。
    /// </summary>
    public static Task<int> ProbeIpcAsync(string ip, string? userName = null, string? password = null)
    {
        return Task.Run(() =>
        {
            var resource = new NETRESOURCE
            {
                Scope = RESOURCE_GLOBALNET,
                Type = RESOURCETYPE_DISK,
                Usage = RESOURCEUSAGE_CONNECTABLE,
                LocalName = "",
                RemoteName = $"\\\\{ip}\\IPC$",
                Comment = "",
                Provider = ""
            };
            return WNetAddConnection2(ref resource, password, userName, 0);
        });
    }

    /// <summary>强制断开到该 IP 的现有连接（用于凭据冲突 1219 场景）。返回是否确有连接被删除。</summary>
    public static Task<bool> DeleteSessionAsync(string ip)
    {
        return Task.Run(() =>
        {
            // 设备级断开可清掉该服务器上的所有共享连接；IPC$ 单独再补一次
            int deviceLevel = SafeCancel($"\\\\{ip}");
            int ipcLevel = SafeCancel($"\\\\{ip}\\IPC$");
            return deviceLevel == 0 || ipcLevel == 0;
        });

        static int SafeCancel(string name)
        {
            try { return WNetCancelConnection2(name, 0, true); }
            catch { return -1; }
        }
    }

    /// <summary>
    /// 用 NetShareEnum(level 1) 结构化枚举目标的所有磁盘类共享。
    /// 无文本解析、无编码问题；失败返回空列表（调用方决定是否回退 net view）。
    /// </summary>
    public static Task<List<SharedFolder>> EnumSharesViaNetShareEnumAsync(string ip)
    {
        return Task.Run(() =>
        {
            var shares = new List<SharedFolder>();
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int err = NetShareEnum($"\\\\{ip}", 1, out buffer, -1, out int read, out _, IntPtr.Zero);
                if ((err != 0 && err != 234 /*ERROR_MORE_DATA*/) || read <= 0)
                    return shares;

                int size = Marshal.SizeOf<SHARE_INFO_1>();
                for (int i = 0; i < read; i++)
                {
                    var info = Marshal.PtrToStructure<SHARE_INFO_1>(IntPtr.Add(buffer, i * size));
                    int baseType = info.Type & ~(STYPE_SPECIAL | STYPE_TEMPORARY);
                    if (baseType != STYPE_DISKTREE) continue;   // 只要磁盘类共享
                    if (string.IsNullOrEmpty(info.NetName)) continue;
                    if (info.NetName.EndsWith("$")) continue;   // 过滤管理共享(C$/Admin$/…)

                    shares.Add(new SharedFolder
                    {
                        ShareName = info.NetName,
                        Path = $"\\\\{ip}\\{info.NetName}",
                        DeviceIp = ip,
                        DeviceName = ip,
                        Description = info.Remark ?? "",
                        IsAccessible = true
                    });
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero) NetApiBufferFree(buffer);
            }
            return shares;
        });
    }

    private static bool _encodingRegistered;

    /// <summary>本机 OEM 编码（中文系统为 cp936/GBK），用于解码 net 等控制台工具输出</summary>
    public static Encoding GetOemEncoding()
    {
        try
        {
            if (!_encodingRegistered)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _encodingRegistered = true;
            }
            return Encoding.GetEncoding((int)GetOEMCP());
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}
