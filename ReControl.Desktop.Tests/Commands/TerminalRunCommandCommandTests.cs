using System.Collections.Generic;
using System.Threading.Tasks;
using ReControl.Desktop.Commands.Terminal;
using ReControl.Desktop.Models;
using ReControl.Desktop.Services;
using Xunit;

namespace ReControl.Desktop.Tests.Commands;

/// <summary>
/// Tests for TerminalRunCommandCommand, specifically that the handler delegates to the
/// platform shell so that pipes and redirects work (cmd /c on Windows, /bin/sh -c on Linux).
/// </summary>
public class TerminalRunCommandCommandTests
{
    private static LogService NullLog() => new LogService();

    /// <summary>
    /// Proves shell semantics by using a command that only works through the shell:
    ///
    /// Windows: Binary="echo", Args=["beta", "|", "findstr", "beta"] — the joined
    /// command line "echo beta | findstr beta" is run via cmd /c.  Under the OLD
    /// execve code, echo.exe receives "|" as a literal arg and outputs "beta | findstr beta";
    /// findstr never runs, so the output contains "findstr".  Under the NEW shell code, the
    /// pipe is interpreted: findstr filters the echo output and "findstr" does NOT appear in
    /// stdout (only "beta" does).
    ///
    /// Linux: Binary="printf", Args=["alpha\nbeta\n", "|", "grep", "beta"] — joined as
    /// "printf alpha\nbeta\n | grep beta" via /bin/sh -c.  Under the OLD execve code
    /// printf receives "|" as a literal arg and the output contains "alpha"; under the
    /// NEW code grep filters to "beta" only.
    /// </summary>
    [Fact]
    public async Task RunsThroughShell_SoPipesWork()
    {
        var isWin = OperatingSystem.IsWindows();

        var payload = new TerminalRunCommandPayload
        {
            // On Windows we rely on the fact that echo.exe (from Git) passes "|" as a
            // literal arg under old execve, so stdout contains the literal string "findstr".
            // Shell wrapping makes cmd.exe interpret the pipe; stdout is just "beta".
            Binary = isWin ? "echo" : "printf",
            Args = isWin
                ? new List<string> { "beta", "|", "findstr", "beta" }
                : new List<string> { "alpha\\nbeta\\n", "|", "grep", "beta" },
            Cwd = isWin ? "C:\\" : "/"
        };

        var cmd = new TerminalRunCommandCommand(payload, NullLog());
        dynamic result = (await cmd.ExecuteAsync())!;

        Assert.Equal(0, (int)result.exit_code);
        // Both platforms: output contains the matched token.
        Assert.Contains("beta", (string)result.stdout);

        if (isWin)
        {
            // Under shell wrapping the pipe is interpreted: findstr receives "beta" on stdin
            // and outputs only "beta" — the literal string "findstr" must NOT appear in stdout.
            // Under old execve, echo.exe prints "beta | findstr beta" literally so "findstr"
            // WOULD appear — that is the discriminating signal.
            Assert.DoesNotContain("findstr", (string)result.stdout);
        }
        else
        {
            // grep filters out "alpha": only "beta" line passes.
            Assert.DoesNotContain("alpha", (string)result.stdout);
        }
    }
}
