using YiziShare.Core;

namespace YiziShare.Tests;

public class RepairServiceTests
{
    [Fact]
    public void ExecuteAllAsync_EmptyActions_ReturnsSuccess()
    {
        var service = new RepairService();
        var result = service.ExecuteAllAsync(new List<FixAction>()).GetAwaiter().GetResult();

        Assert.True(result.Success);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public void ExecuteAllAsync_OpenCredentialDialogAction_IsSkipped()
    {
        var service = new RepairService();
        var actions = new List<FixAction>
        {
            new() { Title = "Test", Kind = FixActionKind.OpenCredentialDialog, Command = "" }
        };
        var result = service.ExecuteAllAsync(actions).GetAwaiter().GetResult();

        Assert.True(result.Success);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public void ExecuteAllAsync_OpenExplorerAction_IsSkipped()
    {
        var service = new RepairService();
        var actions = new List<FixAction>
        {
            new() { Title = "Test", Kind = FixActionKind.OpenExplorer, Command = "" }
        };
        var result = service.ExecuteAllAsync(actions).GetAwaiter().GetResult();

        Assert.True(result.Success);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public void ExecuteAllAsync_DeleteNetSessionAction_ExecutesWithoutElevation()
    {
        var service = new RepairService();
        var actions = new List<FixAction>
        {
            new() { Title = "Test", Kind = FixActionKind.DeleteNetSession, Command = "net use \\\\192.168.1.1 /delete /y" }
        };
        var result = service.ExecuteAllAsync(actions).GetAwaiter().GetResult();

        Assert.Single(result.Steps);
        Assert.Equal(FixActionKind.DeleteNetSession, result.Steps[0].Action.Kind);
    }

    [Fact]
    public void ExecuteAllAsync_FirstFailure_StopsExecution()
    {
        var service = new RepairService();
        var actions = new List<FixAction>
        {
            new() { Title = "Fail", Kind = FixActionKind.Cmd, Command = "exit 1" },
            new() { Title = "Never", Kind = FixActionKind.Cmd, Command = "exit 0" }
        };
        var result = service.ExecuteAllAsync(actions).GetAwaiter().GetResult();

        Assert.False(result.Success);
        Assert.Single(result.Steps);
    }
}
