namespace NzbDrone.Common.EnsureThat;

public abstract class Param(string name)
{
    public const string DefaultName = "";

    public readonly string Name = name;
}

public class Param<T> : Param
{
    public readonly T Value;

    internal Param(string name, T value)
        : base(name)
    {
        Value = value;
    }
}
