using System.Linq;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Validation.Paths
{
    public class SeriesAncestorValidator<T> : PropertyValidator<T, string>
    {
        private readonly ISeriesService _seriesService;

        public SeriesAncestorValidator(ISeriesService seriesService)
        {
            _seriesService = seriesService;
        }

        public override string Name => "SeriesAncestorValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", value);

            return !_seriesService.GetAllSeriesPaths().Any(s => value.IsParentPath(s.Value));
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Path '{path}' is an ancestor of an existing series";
    }
}
