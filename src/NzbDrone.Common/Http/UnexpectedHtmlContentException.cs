namespace NzbDrone.Common.Http;

public class UnexpectedHtmlContentException(HttpResponse response) : HttpException(response.Request, response, $"Site responded with browser content instead of api data. This disruption may be temporary, please try again later. [{response.Request.Url}]")
{
}
