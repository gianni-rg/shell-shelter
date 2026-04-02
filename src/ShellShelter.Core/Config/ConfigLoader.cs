namespace ShellShelter.Core.Config;

/// <summary>
/// Loads ShellShelter configuration and dispatches to the supported parser for the detected format.
/// </summary>
public sealed class ConfigLoader
{
    private readonly IniConfigParser _iniConfigParser;
    private readonly JsonConfigParser _jsonConfigParser;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigLoader"/> class.
    /// </summary>
    public ConfigLoader()
        : this(new IniConfigParser(), new JsonConfigParser())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigLoader"/> class.
    /// </summary>
    /// <param name="iniConfigParser">The INI parser to use.</param>
    /// <param name="jsonConfigParser">The JSON parser to use.</param>
    public ConfigLoader(IniConfigParser iniConfigParser, JsonConfigParser jsonConfigParser)
    {
        _iniConfigParser = iniConfigParser ?? throw new ArgumentNullException(nameof(iniConfigParser));
        _jsonConfigParser = jsonConfigParser ?? throw new ArgumentNullException(nameof(jsonConfigParser));
    }

    /// <summary>
    /// Loads a configuration file from disk.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <returns>The parsed shell policy pair.</returns>
    public ShellPolicyPair Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
        }

        string content = File.ReadAllText(path);
        return LoadFromText(content, path);
    }

    /// <summary>
    /// Loads configuration text by detecting the format from the source path or content.
    /// </summary>
    /// <param name="content">The configuration content.</param>
    /// <param name="sourcePath">The optional source path used for extension-based detection.</param>
    /// <returns>The parsed shell policy pair.</returns>
    public ShellPolicyPair LoadFromText(string content, string? sourcePath = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Configuration content cannot be null or whitespace.", nameof(content));
        }

        return DetectFormat(sourcePath, content) switch
        {
            ConfigFormat.Json => _jsonConfigParser.Parse(content),
            ConfigFormat.Ini => _iniConfigParser.Parse(content),
            _ => throw new NotSupportedException("The configuration format is not supported."),
        };
    }

    private static ConfigFormat DetectFormat(string? sourcePath, string content)
    {
        string extension = sourcePath is null ? string.Empty : Path.GetExtension(sourcePath);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigFormat.Json;
        }

        if (extension.Equals(".ini", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigFormat.Ini;
        }

        string trimmed = content.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return ConfigFormat.Json;
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return ConfigFormat.Ini;
        }

        foreach (string line in content.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            string candidate = line.Trim();
            if (candidate.Length == 0 || candidate.StartsWith('#') || candidate.StartsWith(';'))
            {
                continue;
            }

            if (candidate.StartsWith("[", StringComparison.Ordinal) && candidate.EndsWith("]", StringComparison.Ordinal))
            {
                return ConfigFormat.Ini;
            }

            break;
        }

        throw new NotSupportedException("Unable to detect config format.");
    }

    private enum ConfigFormat
    {
        Json,
        Ini,
    }
}
