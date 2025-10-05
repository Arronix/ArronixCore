using System.Collections.Generic;

namespace NzbDrone.Common.Disk;

public class MountOptions(Dictionary<string, string> options)
{
    private readonly Dictionary<string, string> _options = options;

    public bool IsReadOnly => _options.ContainsKey("ro");
}
