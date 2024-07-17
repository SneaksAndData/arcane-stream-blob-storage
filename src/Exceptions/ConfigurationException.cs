using System;

namespace Arcane.Stream.BlobStorage.Exceptions;

/// <summary>
/// Thrown if invalid configuration is provided
/// </summary>
public class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message)
    {

    }
}
