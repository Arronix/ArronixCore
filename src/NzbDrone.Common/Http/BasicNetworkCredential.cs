using System.Net;

namespace NzbDrone.Common.Http;

public class BasicNetworkCredential(string user, string pass) : NetworkCredential(user, pass)
{
}
