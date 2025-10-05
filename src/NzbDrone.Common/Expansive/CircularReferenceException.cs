using System;

namespace NzbDrone.Common.Expansive;

public class CircularReferenceException(string message) : Exception(message)
{
}
