using System.Text;
using NLog;
using NLog.Layouts;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Instrumentation;

public class CleansingConsoleLogLayout : Layout
{
    private readonly SimpleLayout _inner;

    public CleansingConsoleLogLayout(string format)
    {
        // Reuse NLog's SimpleLayout for parsing the format string.
        _inner = new SimpleLayout(format);
    }

    protected override void RenderFormattedMessage(LogEventInfo logEvent, StringBuilder target)
    {
        // SimpleLayout.Render is internal through Layout base; use _inner.Render
        _inner.Render(logEvent, target);

        if (RuntimeInfo.IsProduction)
        {
            var cleansed = CleanseLogMessage.Cleanse(target.ToString());
            target.Clear();
            target.Append(cleansed);
        }
    }

    protected override string GetFormattedMessage(LogEventInfo logEvent)
    {
        var raw = _inner.Render(logEvent);
        if (!RuntimeInfo.IsProduction)
        {
            return raw;
        }

        return CleanseLogMessage.Cleanse(raw);
    }
}
