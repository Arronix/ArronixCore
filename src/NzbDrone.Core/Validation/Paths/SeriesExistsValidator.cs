using System.Linq;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Validation.Paths
{
    public class SeriesExistsValidator<T> : PropertyValidator<T, int>
    {
        private readonly ISeriesService _seriesService;

        public SeriesExistsValidator(ISeriesService seriesService)
        {
            _seriesService = seriesService;
        }

        public override string Name => "SeriesExistsValidator";

        public override bool IsValid(ValidationContext<T> context, int value)
        {
            return !_seriesService.AllSeriesTvdbIds().Any(s => s == value);
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "This series has already been added";
    }
}
