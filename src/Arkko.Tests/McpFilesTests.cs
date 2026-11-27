using Arkko.Core;

namespace Arkko.Tests;

public sealed class McpFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcpfiles-" + Guid.NewGuid().ToString("N"));

    public McpFilesTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, McpFiles.Folder));
        File.WriteAllText(Path.Combine(_root, McpFiles.Folder, "page-1.yml"), "- button \"Guardar\"");
        File.WriteAllText(Path.Combine(_root, "secreto.txt"), "no");
        File.WriteAllText(Path.Combine(_root, McpFiles.Folder, "captura.exe"), "no");
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(".playwright-mcp\\page-1.yml")]
    [InlineData(".playwright-mcp/page-1.yml")]
    public void Lee_un_fichero_de_la_carpeta_de_la_MCP(string relative)
    {
        var file = McpFiles.Read(_root, relative);
        Assert.Null(file.Error);
        Assert.Equal("- button \"Guardar\"", file.Text);
        Assert.False(file.Truncated);
    }

    [Theory]
    [InlineData(".playwright-mcp\\..\\secreto.txt")]
    [InlineData("secreto.txt")]
    [InlineData(".playwright-mcp\\captura.exe")]
    [InlineData("")]
    public void No_lee_fuera_de_la_carpeta_ni_lo_que_no_es_texto(string relative)
    {
        var file = McpFiles.Read(_root, relative);
        Assert.Null(file.Text);
        Assert.NotNull(file.Error);
    }

    [Fact]
    public void No_lee_una_ruta_absoluta_aunque_apunte_dentro()
    {
        var file = McpFiles.Read(_root, Path.Combine(_root, McpFiles.Folder, "page-1.yml"));
        Assert.Null(file.Text);
    }

    [Fact]
    public void Un_fichero_que_ya_no_esta_lo_dice()
    {
        var file = McpFiles.Read(_root, ".playwright-mcp\\page-2.yml");
        Assert.Null(file.Text);
        Assert.Contains("ya no está", file.Error);
    }

    [Fact]
    public void Corta_un_fichero_demasiado_largo()
    {
        File.WriteAllText(Path.Combine(_root, McpFiles.Folder, "console-1.log"), new string('x', McpFiles.MaxBytes + 10));
        var file = McpFiles.Read(_root, ".playwright-mcp\\console-1.log");
        Assert.True(file.Truncated);
        Assert.Equal(McpFiles.MaxBytes, file.Text!.Length);
    }

    [Fact]
    public void Sin_carpeta_de_trabajo_no_lee_nada() => Assert.NotNull(McpFiles.Read(null, ".playwright-mcp\\page-1.yml").Error);

    [Fact]
    public void En_Windows_la_ruta_de_Git_Bash_pasa_a_la_de_Windows()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(@"C:\Users\ana\repo", McpFiles.Root("/c/Users/ana/repo"));
        Assert.Equal(@"C:\Users\ana\repo", McpFiles.Root(@"C:\Users\ana\repo"));
    }
}
