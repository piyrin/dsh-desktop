using System;
using System.IO;

internal static class CommandLocator
{
    internal static string Find(string commandName, string pathValue)
    {
        if (String.IsNullOrEmpty(commandName))
            throw new ArgumentException("Command name is required.", "commandName");

        string value = pathValue ?? String.Empty;
        string[] entries = value.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string entry in entries)
        {
            string candidate = Path.Combine(entry, commandName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new FileNotFoundException("Command not found: " + commandName, commandName);
    }
}
