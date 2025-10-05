using System;

namespace NzbDrone.Common.Http;

public class HttpException(HttpRequest request, HttpResponse response, string message) : Exception(message)
{
    public HttpRequest Request { get; private set; } = request;
    public HttpResponse Response { get; private set; } = response;

    public HttpException(HttpRequest request, HttpResponse response)
        : this(request, response, string.Format("HTTP request failed: [{0}:{1}] [{2}] at [{3}]", (int)response.StatusCode, response.StatusCode, request.Method, request.Url))
    {
    }

    public HttpException(HttpResponse response)
        : this(response.Request, response)
    {
    }

    public override string ToString()
    {
        if (Response != null && Response.ResponseData != null)
        {
            return base.ToString() + Environment.NewLine + Response.Content;
        }

        return base.ToString();
    }
}
