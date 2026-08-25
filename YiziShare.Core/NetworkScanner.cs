using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace YiziShare.Core;

/// <summary>
/// 局域网扫描器 - 多种探测方式
/// </summary>
public class NetworkScanner
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int SendArp(uint DestIp, uint SrcIp, byte[] MacAddr, ref uint MacAddrLen);

    // 键统一为 6 位无冒号大写 OUI 前缀（查询端已去冒号）
    private static readonly Dictionary<string, string> OemTable = new(StringComparer.OrdinalIgnoreCase)
    {
        // 虚拟化
        { "000C29", "VMware" }, { "005056", "VMware" }, { "001C14", "VMware" },
        { "080027", "VirtualBox" }, { "001C42", "Parallels" }, { "00155D", "Microsoft Hyper-V" },
        // 品牌整机
        { "0003FF", "HP" }, { "000D3A", "HP" }, { "001F29", "HP" }, { "3CD92B", "HP" },
        { "001B21", "Dell" }, { "001E58", "Dell" }, { "F07959", "Dell" }, { "00219B", "Dell" },
        { "54EE75", "Lenovo" },
        { "002264", "Apple" }, { "B00090", "Apple" }, { "001EC2", "Apple" },
        { "002500", "Apple" }, { "040CCE", "Apple" },
        { "002354", "ASUS" }, { "04D9F5", "ASUS" }, { "AC220B", "ASUS" },
        // NAS / 存储
        { "001132", "Synology" }, { "245EBE", "QNAP" },
        // 路由器 / 网络设备
        { "001A4D", "D-Link" }, { "340804", "D-Link" },
        { "001E52", "Tenda" },
        { "001F5B", "TP-LINK" }, { "001C10", "TP-LINK" }, { "50C7BF", "TP-LINK" },
        { "002219", "Netgear" }, { "002402", "Netgear" }, { "9C3DCF", "Netgear" },
        { "F09FC2", "Ubiquiti" }, { "245A4C", "Ubiquiti" },
        { "001310", "Belkin" },
        { "00E04C", "Realtek" }, { "525400", "Realtek" },
        // 开发板 / 物联网
        { "B827EB", "Raspberry Pi" }, { "DCA632", "Raspberry Pi" }, { "E45F01", "Raspberry Pi" },
        { "240AC4", "Espressif" }, { "5CCF7F", "Espressif" }, { "30AEA4", "Espressif" },
        // 消费电子
        { "286C07", "Xiaomi" }, { "640980", "Xiaomi" },
        { "001882", "Huawei" },
        { "000409", "NVIDIA" },
    };

    public event EventHandler<Device>? DeviceFound;
    public event EventHandler<string>? LogMessage;

    protected virtual void OnLog(string msg) => LogMessage?.Invoke(this, msg);
    protected virtual void OnDeviceFound(Device device) => DeviceFound?.Invoke(this, device);

    public async Task<List<Device>> ScanAsync(string subnet, int timeoutMs = 5000)
    {
        var devices = new ConcurrentDictionary<string, Device>(StringComparer.OrdinalIgnoreCase);

        OnLog($"开始扫描子网: {subnet}");

        // 方法1: 读取现有 ARP 缓存
        ScanArpCache(devices);
        OnLog($"从 ARP 缓存发现 {devices.Count} 台设备");

        // 方法2: 主动 Ping 扫描
        await PingScanAsync(devices, subnet);

        // 方法3: 端口探测 - 重点扫描 SMB 端口
        await PortScanAsync(devices, subnet, timeoutMs);

        // 过滤：只返回有共享文件夹的设备
        var devicesWithShares = devices.Values.Where(d => d.HasShares || d.SharedFolders.Count > 0).ToList();
        
        OnLog($"扫描完成，发现 {devices.Count} 台设备，其中 {devicesWithShares.Count} 台有共享");
        return devicesWithShares;
    }

    private void ScanArpCache(ConcurrentDictionary<string, Device> devices)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && IsValidIPv4(parts[0]))
                {
                    var mac = parts[1].Replace("-", ":");
                    if (IsValidMac(mac))
                    {
                        var ip = IPAddress.Parse(parts[0]);
                        if (!devices.ContainsKey(ip.ToString()))
                        {
                            devices.TryAdd(ip.ToString(), new Device
                            {
                                IpAddress = ip.ToString(),
                                MacAddress = mac,
                                HostName = ip.ToString(),
                                Manufacturer = GetManufacturer(mac),
                                IsReachable = true
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OnLog($"读取 ARP 缓存失败: {ex.Message}");
        }
    }

    private async Task PingScanAsync(ConcurrentDictionary<string, Device> devices, string subnet)
    {
        try
        {
            var (network, prefixLen) = IpRangeUtil.ParseSubnet(subnet);
            var ips = IpRangeUtil.GenerateHostIps(network, prefixLen);

            OnLog($"开始 Ping 扫描 ({ips.Count} 个IP)...");

            var semaphore = new SemaphoreSlim(128);
            var tasks = ips.Select(async ip =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // Ping 实例非线程安全，必须每个任务独立实例
                    using var ping = new System.Net.NetworkInformation.Ping();
                    var reply = await ping.SendPingAsync(ip, 500);
                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    {
                        var mac = await GetMacAsync(ip);
                        var hostname = await TryResolveHostname(ip);

                        if (!devices.ContainsKey(ip.ToString()))
                        {
                            var device = new Device
                            {
                                IpAddress = ip.ToString(),
                                MacAddress = mac ?? "",
                                HostName = hostname ?? ip.ToString(),
                                Manufacturer = GetManufacturer(mac ?? ""),
                                IsReachable = true
                            };
                            devices.TryAdd(ip.ToString(), device);
                            OnDeviceFound(device);
                        }
                    }
                }
                catch { }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
            OnLog($"Ping 扫描完成，共 {devices.Count} 台设备");
        }
        catch (Exception ex)
        {
            OnLog($"Ping 扫描失败: {ex.Message}");
        }
    }

    private async Task PortScanAsync(ConcurrentDictionary<string, Device> devices, string subnet, int timeoutMs)
    {
        try
        {
            var (network, prefixLen) = IpRangeUtil.ParseSubnet(subnet);
            var ips = IpRangeUtil.GenerateHostIps(network, prefixLen);

            OnLog($"开始 SMB 端口扫描 (445)...");

            var semaphore = new SemaphoreSlim(256);
            var tasks = ips.Select(async ip =>
            {
                await semaphore.WaitAsync();
                try
                {
                    using var client = new TcpClient();
                    var success = await ConnectWithTimeoutAsync(client, ip.ToString(), 445, timeoutMs / 2);

                    if (success && client.Connected)
                    {
                        client.Close();
                        var mac = await GetMacAsync(ip);
                        var hostname = await TryResolveHostname(ip);

                        if (!devices.ContainsKey(ip.ToString()))
                        {
                            var device = new Device
                            {
                                IpAddress = ip.ToString(),
                                MacAddress = mac ?? "",
                                HostName = hostname ?? ip.ToString(),
                                Manufacturer = GetManufacturer(mac ?? ""),
                                IsReachable = true,
                                HasShares = true
                            };
                            devices.TryAdd(ip.ToString(), device);
                            OnDeviceFound(device);
                            OnLog($"[+] 发现 SMB 设备: {hostname ?? ip.ToString()} | {ip}");
                        }
                        else
                        {
                            // 标记已有设备有共享
                            if (devices.TryGetValue(ip.ToString(), out var existingDevice))
                            {
                                existingDevice.HasShares = true;
                            }
                        }
                    }
                }
                catch { }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
            OnLog($"端口扫描完成，共 {devices.Count} 台设备");
        }
        catch (Exception ex)
        {
            OnLog($"端口扫描失败: {ex.Message}");
        }
    }

    private static async Task<bool> ConnectWithTimeoutAsync(TcpClient client, string host, int port, int timeoutMs)
    {
        try
        {
            var task = client.ConnectAsync(host, port);
            if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
            {
                return client.Connected;
            }
            client.Close();
            return false;
        }
        catch { return false; }
    }

    private static Task<string?> GetMacAsync(IPAddress ip)
    {
        return Task.Run(() =>
        {
            try
            {
                byte[] macBuf = new byte[6];
                uint macLen = 6;
                var ipBytes = ip.GetAddressBytes();
                uint ipInt = (uint)(ipBytes[0] | (ipBytes[1] << 8) | (ipBytes[2] << 16) | (ipBytes[3] << 24));

                int hr = SendArp(ipInt, 0, macBuf, ref macLen);
                if (hr != 0 || macBuf[0] == 0) return null;

                return string.Join(":", macBuf.Select(b => b.ToString("X2")));
            }
            catch { return null; }
        });
    }

    private static async Task<string?> TryResolveHostname(IPAddress ip)
    {
        try
        {
            var result = await Dns.GetHostEntryAsync(ip);
            return result?.HostName;
        }
        catch { return null; }
    }

    public static string GetManufacturer(string mac)
    {
        if (string.IsNullOrEmpty(mac)) return "";
        var prefix = mac.Replace(":", "").Substring(0, Math.Min(6, mac.Replace(":", "").Length)).ToUpper();
        return OemTable.GetValueOrDefault(prefix, "");
    }

    private static bool IsValidIPv4(string ip) => IPAddress.TryParse(ip, out _);

    private static bool IsValidMac(string mac) => 
        System.Text.RegularExpressions.Regex.IsMatch(mac, @"^[0-9A-Fa-f]{2}[:-][0-9A-Fa-f]{2}[:-][0-9A-Fa-f]{2}[:-][0-9A-Fa-f]{2}[:-][0-9A-Fa-f]{2}[:-][0-9A-Fa-f]{2}$");

    /// <summary>
    /// 获取本机所在子网：优先选「已启用 + 有 IPv4 网关」的网卡（可排除 Hyper-V/WSL 等虚拟网卡与 APIPA 地址）
    /// </summary>
    public string GetLocalSubnet()
    {
        try
        {
            var usable = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsUsableIpv4Nic)
                .OrderBy(n =>
                {
                    try { return n.GetIPProperties().GetIPv4Properties()?.Index ?? int.MaxValue; }
                    catch { return int.MaxValue; }
                })
                .ToList();

            foreach (var nic in usable)
            {
                var ip = nic.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                         && !IpRangeUtil.IsApipa(u.Address))
                    ?.Address;
                if (ip != null) return IpRangeUtil.SubnetOf(ip);
            }

            // 回退：DNS 反解本机名
            var dnsIp = Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IpRangeUtil.IsApipa(a));
            if (dnsIp != null) return IpRangeUtil.SubnetOf(dnsIp);
        }
        catch { }
        return "192.168.0.0/24";
    }

    /// <summary>网卡是否可用于子网探测：已启用、非环回、非 APIPA 地址且配置了 IPv4 网关</summary>
    public static bool IsUsableIpv4Nic(NetworkInterface ni)
    {
        if (ni.OperationalStatus != OperationalStatus.Up) return false;
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) return false;

        var props = ni.GetIPProperties();
        if (!IpRangeUtil.HasIpv4Gateway(props.GatewayAddresses.Select(g => g.Address))) return false;

        return props.UnicastAddresses.Any(u =>
            u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && !IpRangeUtil.IsApipa(u.Address));
    }
}
