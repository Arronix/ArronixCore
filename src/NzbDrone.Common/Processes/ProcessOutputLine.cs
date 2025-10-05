using System;

namespace NzbDrone.Common.Processes;

public class ProcessOutputLine(ProcessOutputLevel level, string content)
{
    public ProcessOutputLevel Level { get; set; } = level;
    public string Content { get; set; } = content;
    public DateTime Time { get; set; } = DateTime.UtcNow;

    public override string ToString()
    {
        return string.Format("{0} - {1} - {2}", Time, Level, Content);
    }
}

public enum ProcessOutputLevel
{
    Standard = 0,
    Error = 1
}
