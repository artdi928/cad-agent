using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CadAgent.Tests;

[TestClass]
public sealed class SafetyScanTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "SendStringToExecute",
        "Editor.Command",
        "Database.Save",
        "Document.Save",
        "SaveAs",
        "Process.Start",
        "ProcessStartInfo",
        "PowerShell",
        "System.Management.Automation",
        "InvokeMember",
        "vl-load-com",
        "vlax-",
        "acedCommand",
        "acedPostCommand"
    ];

    [TestMethod]
    public void SourceCodeDoesNotContainForbiddenExecutionOrSaveMethods()
    {
        var repoRoot = GetRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        Assert.IsTrue(Directory.Exists(srcDir), $"Source directory '{srcDir}' must exist.");

        var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\") && !f.Contains(@"/obj/") &&
                        !f.Contains(@"\bin\") && !f.Contains(@"/bin/"))
            .ToArray();

        Assert.IsTrue(csFiles.Length > 0, "Expected to find C# source files under src/.");

        var violations = new List<string>();

        foreach (var file in csFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                    continue;

                foreach (var token in ForbiddenTokens)
                {
                    if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{Path.GetRelativePath(repoRoot, file)}:{i + 1} contains forbidden token '{token}' -> '{trimmed}'");
                    }
                }
            }
        }

        if (violations.Count > 0)
        {
            Assert.Fail("Static safety scan detected forbidden mutation/execution primitives:\n" +
                        string.Join("\n", violations));
        }
    }

    [TestMethod]
    public void NamedPipeServerStream_EnforcesCurrentUserOnly()
    {
        var repoRoot = GetRepoRoot();
        var pipeServerFile = Path.Combine(repoRoot, "src", "CadAgent.Plugin", "PipeServer.cs");
        Assert.IsTrue(File.Exists(pipeServerFile), "PipeServer.cs must exist.");

        var content = File.ReadAllText(pipeServerFile);
        Assert.IsTrue(content.Contains("PipeOptions.CurrentUserOnly"),
            "NamedPipeServerStream must specify PipeOptions.CurrentUserOnly for security isolation.");
    }

    [TestMethod]
    public void PlanExecutor_MTextAndMLeader_RemainInspectOnly()
    {
        var repoRoot = GetRepoRoot();
        var planExecutorFile = Path.Combine(repoRoot, "src", "CadAgent.Plugin", "PlanExecutor.cs");
        Assert.IsTrue(File.Exists(planExecutorFile), "PlanExecutor.cs must exist.");

        var content = File.ReadAllText(planExecutorFile);
        Assert.IsFalse(content.Contains("MText.Contents =") || content.Contains("mText.Contents =") ||
                       content.Contains("MText.Text =") || content.Contains("mText.Text =") ||
                       content.Contains("mLeader.Text ="),
            "MText and MLeader must remain inspect-only; mutation in v1 is forbidden.");
    }

    [TestMethod]
    public void McpProject_DoesNotReferenceAutodeskAssemblies()
    {
        var repoRoot = GetRepoRoot();
        var mcpProjFile = Path.Combine(repoRoot, "src", "CadAgent.Mcp", "CadAgent.Mcp.csproj");
        Assert.IsTrue(File.Exists(mcpProjFile), "CadAgent.Mcp.csproj must exist.");

        var projContent = File.ReadAllText(mcpProjFile);
        Assert.IsFalse(projContent.Contains("Autodesk", StringComparison.OrdinalIgnoreCase),
            "CadAgent.Mcp must not reference Autodesk assemblies.");
        Assert.IsFalse(projContent.Contains("acdbmgd", StringComparison.OrdinalIgnoreCase) ||
                       projContent.Contains("accoremgd", StringComparison.OrdinalIgnoreCase) ||
                       projContent.Contains("acmgd", StringComparison.OrdinalIgnoreCase),
            "CadAgent.Mcp must not reference AutoCAD native managed DLLs.");
    }

    [TestMethod]
    public void McpProject_DoesNotContainDirectMutationOrAutoCADWriteLogic()
    {
        var repoRoot = GetRepoRoot();
        var mcpDir = Path.Combine(repoRoot, "src", "CadAgent.Mcp");
        Assert.IsTrue(Directory.Exists(mcpDir), "CadAgent.Mcp directory must exist.");

        var csFiles = Directory.GetFiles(mcpDir, "*.cs", SearchOption.AllDirectories);
        string[] forbiddenCadTokens = ["OpenMode.ForWrite", "ForWrite", "StartTransaction", "GetObject", "TransactionManager", "Commit()"];

        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenCadTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase),
                    $"CadAgent.Mcp file '{Path.GetFileName(file)}' must not contain direct CAD write/transaction token '{token}'. MCP must delegate all mutations to bridge.");
            }
        }
    }

    private static string GetRepoRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(baseDir);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CadAgent.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException($"Could not locate repo root starting from '{baseDir}'.");
    }
}
