using System.Linq;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Validation.Paths
{
    public class SeriesPathValidator<T> : PropertyValidator<T, string>
    {
        private readonly ISeriesService _seriesService;

        public SeriesPathValidator(ISeriesService seriesService)
        {
            _seriesService = seriesService;
        }

        public override string Name => "SeriesPathValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", value);

            dynamic instance = context.InstanceToValidate;
            var instanceId = (int)instance.Id;

            // Skip the path for this series and any invalid paths
            return !_seriesService.GetAllSeriesPaths().Any(s => s.Key != instanceId &&
                                                                s.Value.IsPathValid(PathValidationType.CurrentOs) &&
                                                                s.Value.PathEquals(value));
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Path '{path}' is already configured for another series";
    }
}
