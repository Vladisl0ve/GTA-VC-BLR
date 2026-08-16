using System.Diagnostics;
using System.IO;

namespace GTA_GXT_Editor.Services;

public interface IInstallerCompiler
{
    Task<string> CompileAsync(
        string scriptPath,
        string outputDirectory,
        string outputBaseName,
        CancellationToken cancellationToken);
}

public sealed class InnoInstallerCompiler : IInstallerCompiler
{
    private readonly string _compilerPath;

    public InnoInstallerCompiler(string? compilerPath = null)
    {
        _compilerPath = compilerPath ?? Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "InnoSetup",
            "7.0.2-x86",
            "ISCC.exe");
    }

    public async Task<string> CompileAsync(
        string scriptPath,
        string outputDirectory,
        string outputBaseName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_compilerPath))
        {
            throw new FileNotFoundException(
                LocalizationProvider.Current.Format("Installer.CompilerMissing", _compilerPath),
                _compilerPath);
        }

        Directory.CreateDirectory(outputDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = _compilerPath,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/Qp");
        startInfo.ArgumentList.Add($"/O{outputDirectory}");
        startInfo.ArgumentList.Add($"/F{outputBaseName}");
        startInfo.ArgumentList.Add(scriptPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException(LocalizationProvider.Current.Get("Installer.CompilerStartFailed"));
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            var details = string.Join(
                Environment.NewLine,
                new[] { output.Trim(), error.Trim() }.Where(value => value.Length > 0));
            throw new InvalidOperationException(LocalizationProvider.Current.Format(
                "Installer.CompilerFailed",
                process.ExitCode,
                details));
        }

        var resultPath = Path.Combine(outputDirectory, $"{outputBaseName}.exe");
        if (!File.Exists(resultPath))
        {
            throw new InvalidDataException(LocalizationProvider.Current.Get("Installer.CompilerNoOutput"));
        }

        return resultPath;
    }
}
