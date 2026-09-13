namespace OblivionMenuSkip;

internal static class StarterFile
{
    public static string Validate(string hostAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(hostAssemblyPath))
            throw new InvalidOperationException("The ReadyM starter location could not be resolved.");
        var dllDirectory = Path.GetDirectoryName(hostAssemblyPath);
        var modDirectory = dllDirectory is null ? null : Directory.GetParent(dllDirectory)?.FullName;
        if (modDirectory is null || !string.Equals(Path.GetFileName(dllDirectory), "dlls", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The ReadyM host has an unsupported starter layout.");
        var starter = new FileInfo(Path.Combine(modDirectory, "world.sav"));
        if (!starter.Exists || starter.Length == 0)
            throw new InvalidOperationException("The ReadyM world starter is missing or empty. Repair the launcher files before retrying.");
        return starter.FullName;
    }
}
