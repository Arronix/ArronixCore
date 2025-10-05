using System.Net;

namespace NzbDrone.Common.Http;

public class TlsFailureException(WebRequest request, WebException innerException) : WebException("Failed to establish secure https connection to '" + request.RequestUri + "'.", innerException, WebExceptionStatus.SecureChannelFailure, innerException.Response)
{
}
