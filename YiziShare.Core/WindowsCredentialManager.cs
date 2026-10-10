using System.ComponentModel;
using System.Runtime.InteropServices;

namespace YiziShare.Core;

/// <summary>
/// Windows 凭据管理器封装：按目标（设备 IP/主机名）保存 SMB 登录凭据，
/// 等价于 `cmdkey /add:目标 /user:用户 /pass:密码`（CRED_TYPE_DOMAIN_PASSWORD）。
/// </summary>
public static class WindowsCredentialManager
{
    private const int CRED_TYPE_DOMAIN_PASSWORD = 2;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    /// <summary>保存的凭据</summary>
    public readonly record struct SavedCredential(string UserName, string Password);

    /// <summary>保存凭据；成功返回 true。用户名中的目标前缀会被规范化。</summary>
    public static bool Save(string target, string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(userName))
            return false;

        string storedUser = NormalizeStoredUserName(target, userName);
        IntPtr blob = Marshal.StringToCoTaskMemUni(password ?? "");
        try
        {
            var credential = new CREDENTIAL
            {
                Flags = 0,
                Type = CRED_TYPE_DOMAIN_PASSWORD,
                TargetName = target,
                Comment = "YiziShare",
                CredentialBlobSize = (password?.Length ?? 0) * sizeof(char),
                CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = null,
                UserName = storedUser
            };
            return CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    /// <summary>读取保存的凭据；不存在或不可读返回 null</summary>
    public static SavedCredential? GetSavedCredential(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;

        if (!CredRead(target, CRED_TYPE_DOMAIN_PASSWORD, 0, out IntPtr credPtr))
            return null;

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            string password = "";
            if (credential.CredentialBlobSize > 0 && credential.CredentialBlob != IntPtr.Zero)
            {
                password = Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / sizeof(char)) ?? "";
            }
            return new SavedCredential(credential.UserName ?? "", password);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    /// <summary>是否存在已保存的凭据</summary>
    public static bool HasCredential(string target) => GetSavedCredential(target) != null;

    /// <summary>删除保存的凭据</summary>
    public static bool Delete(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        return CredDelete(target, CRED_TYPE_DOMAIN_PASSWORD, 0);
    }

    /// <summary>
    /// 规范化存入凭据管理器的用户名：
    /// "目标\用户" → "用户"（目标已由 TargetName 绑定）；".\用户" → "用户"；域账号保持原样。
    /// </summary>
    public static string NormalizeStoredUserName(string target, string userName)
    {
        var trimmed = userName.Trim();
        int idx = trimmed.IndexOf('\\');
        if (idx <= 0 || idx == trimmed.Length - 1) return trimmed;

        var domain = trimmed[..idx];
        var user = trimmed[(idx + 1)..];

        if (domain == "." || string.Equals(domain, target, StringComparison.OrdinalIgnoreCase))
            return user;

        return trimmed;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int reservedFlags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
