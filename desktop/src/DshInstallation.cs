using System;
using System.IO;

internal sealed class DshInstallation
{
    internal string NodeExe { get; private set; }
    internal string CommandWrapper { get; private set; }
    internal string JsEntry { get; private set; }
    internal string Directory { get; private set; }

    private DshInstallation() { }

    internal static DshInstallation Discover(string pathValue)
    {
        string nodeExe = Locate("node.exe", pathValue, "Node executable not found: node.exe");
        string wrapper = Locate("dsh.cmd", pathValue, "DSH command wrapper not found: dsh.cmd");
        string directory = Path.GetDirectoryName(wrapper);
        string entry = Path.Combine(directory, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        if (!File.Exists(entry))
            throw new FileNotFoundException("DSH JavaScript entry not found: lib\\bin.js", entry);

        return new DshInstallation
        {
            NodeExe = nodeExe,
            CommandWrapper = wrapper,
            JsEntry = Path.GetFullPath(entry),
            Directory = directory
        };
    }

    private static string Locate(string commandName, string pathValue, string message)
    {
        try
        {
            return CommandLocator.Find(commandName, pathValue);
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException(message, commandName);
        }
    }
}
