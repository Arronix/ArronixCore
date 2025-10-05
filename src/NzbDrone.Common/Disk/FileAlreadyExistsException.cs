using System;

namespace NzbDrone.Common.Disk;

public class FileAlreadyExistsException(string message, string filename) : Exception(message)
{
    public string Filename { get; set; } = filename;
}
