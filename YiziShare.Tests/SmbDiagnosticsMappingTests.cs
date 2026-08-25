using SmbIssue = YiziShare.Core.SmbDiagnostics.SmbIssueType;
using YiziShare.Core;

namespace YiziShare.Tests;

public class SmbDiagnosticsMappingTests
{
    private const string Ip = "192.168.1.50";

    [Theory]
    [InlineData(1326)]
    [InlineData(86)]
    public void BuildFromErrorCode_LogonFailure_ReturnsCredentialsScopeWithCredentialAction(int code)
    {
        var result = SmbDiagnostics.BuildFromErrorCode(code, Ip);
        Assert.False(result.IsAccessible);
        Assert.Equal(SmbIssue.LogonFailure, result.IssueType);
        Assert.Equal(FixScope.Credentials, result.Scope);
        Assert.Contains(result.Actions, a => a.Kind == FixActionKind.OpenCredentialDialog);
    }

    [Fact]
    public void BuildFromErrorCode_1219_OffersDeleteSessionCommand()
    {
        var result = SmbDiagnostics.BuildFromErrorCode(1219, Ip);
        Assert.Equal(SmbIssue.CredentialsConflict, result.IssueType);
        Assert.Equal(FixScope.LocalElevated, result.Scope);
        var action = Assert.Single(result.Actions);
        Assert.Equal(FixActionKind.DeleteNetSession, action.Kind);
        Assert.Contains("/delete", action.Command);
        // 删会话无需提权：命令本身不应包含任何提权包装
        Assert.DoesNotContain("runas", action.Command);
    }

    [Fact]
    public void BuildFromErrorCode_1244_GuestBlocked_HasRegistryFixRiskNoteAndRollbackHint()
    {
        var result = SmbDiagnostics.BuildFromErrorCode(1244, Ip);
        Assert.Equal(SmbIssue.GuestAuthBlocked, result.IssueType);
        Assert.Equal(FixScope.LocalElevated, result.Scope);

        var fix = Assert.Single(result.Actions.Where(a => a.Kind == FixActionKind.Cmd));
        Assert.Contains("AllowInsecureGuestAuth", fix.Command);
        Assert.Contains("/d 1", fix.Command);
        Assert.False(string.IsNullOrEmpty(fix.RiskNote)); // 必须有风险提示

        // 回滚方式必须写进说明文字
        Assert.Contains("AllowInsecureGuestAuth", result.Solution);
        Assert.Contains("/d 0", result.Solution);
    }

    [Fact]
    public void BuildFromErrorCode_53_OffersProfileAndFirewallActions()
    {
        var result = SmbDiagnostics.BuildFromErrorCode(53, Ip);
        Assert.Equal(FixScope.LocalElevated, result.Scope);

        Assert.Contains(result.Actions, a =>
            a.Kind == FixActionKind.PowerShell && a.Command.Contains("NetworkCategory Private"));
        Assert.Contains(result.Actions, a =>
            a.Kind == FixActionKind.Cmd && a.Command.Contains("@firewallapi.dll,-28502"));
    }

    [Theory]
    [InlineData(58)]
    [InlineData(64)]
    public void BuildFromErrorCode_Smb1OnlyServer_IsGuidedRemoteNotLocalFix(int code)
    {
        var result = SmbDiagnostics.BuildFromErrorCode(code, Ip);
        Assert.Equal(SmbIssue.Smb1OnlyServer, result.IssueType);
        Assert.Equal(FixScope.GuidedRemote, result.Scope);
        Assert.Empty(result.Actions); // 不给本机一键执行 SMB1 启用
        Assert.NotEmpty(result.ManualSteps);
    }

    [Fact]
    public void BuildFromErrorCode_1396_IsGuidedRemoteNotLocal()
    {
        var result = SmbDiagnostics.BuildFromErrorCode(1396, Ip);
        Assert.Equal(FixScope.GuidedRemote, result.Scope);
        Assert.Empty(result.Actions);
        Assert.NotEmpty(result.ManualSteps);
    }

    [Fact]
    public void BuildFromErrorCode_Unknown_FallsBackWithOpenExplorer()
    {
        var result = SmbDiagnostics.BuildFromErrorCode(9999, Ip);
        Assert.Equal(SmbIssue.Unknown, result.IssueType);
        Assert.Equal(FixScope.GuidedRemote, result.Scope);
        Assert.Contains(result.Actions, a => a.Kind == FixActionKind.OpenExplorer);
    }

    [Fact]
    public void BuildPortResult_NetbiosOnly_NeverMentionsRdma()
    {
        var result = SmbDiagnostics.BuildPortResult(netbiosOnly: true, Ip);
        Assert.Equal(SmbIssue.LegacyNetBiosOnly, result.IssueType);
        Assert.Equal(FixScope.GuidedRemote, result.Scope);

        // 回归：旧文案错误地引导用户启用「SMB Direct(RDMA)」
        var allText = result.Title + result.Description + result.Solution + string.Join("", result.ManualSteps);
        Assert.DoesNotContain("RDMA", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SMB2", result.ManualSteps[0]);
    }

    [Fact]
    public void BuildPortResult_Closed_GivesTargetSideStepsOnly()
    {
        var result = SmbDiagnostics.BuildPortResult(netbiosOnly: false, Ip);
        Assert.Equal(SmbIssue.PortNotOpen, result.IssueType);
        Assert.Empty(result.Actions); // 目标侧问题不允许本机执行命令
        Assert.NotEmpty(result.ManualSteps);
    }

    [Fact]
    public void AllMappedErrors_ActionsAreAtomic_NoShellChaining()
    {
        int[] codes = [5, 86, 1326, 1219, 1244, 53, 1231, 1222, 1396, 58, 64, 9999];
        foreach (var code in codes)
        {
            var result = SmbDiagnostics.BuildFromErrorCode(code, Ip);
            foreach (var action in result.Actions)
            {
                // cmd 的 && 与 PowerShell 的 ; 跨宿主语义不同，禁止拼接
                Assert.DoesNotContain("&&", action.Command);
                Assert.False(action.Command.TrimEnd().EndsWith(";"));
            }
        }
    }
}
