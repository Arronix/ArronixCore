namespace NzbDrone.Common.OAuth;

public class WebParameter(string name, string value)
{
    public string Value { get; set; } = value;
    public string Name { get; private set; } = name;
}
