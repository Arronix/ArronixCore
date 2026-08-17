using System.Linq;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Tv
{
    public class SeriesTitleSlugValidator<T> : PropertyValidator<T, string>
    {
        private readonly ISeriesService _seriesService;

        public SeriesTitleSlugValidator(ISeriesService seriesService)
        {
            _seriesService = seriesService;
        }

        public override string Name => "SeriesTitleSlugValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value == null)
            {
                return true;
            }

            dynamic instance = context.InstanceToValidate;
            var instanceId = (int)instance.Id;

            var conflictingSeries = _seriesService.GetAllSeries()
                                                  .FirstOrDefault(s => s.TitleSlug.IsNotNullOrWhiteSpace() &&
                                                              s.TitleSlug.Equals(value) &&
                                                              s.Id != instanceId);

            if (conflictingSeries == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("slug", value);
            context.MessageFormatter.AppendArgument("seriesTitle", conflictingSeries.Title);

            return false;
        }

        protected override string GetDefaultMessageTemplate(string errorCode) =>
            "Title slug '{slug}' is in use by series '{seriesTitle}'. Check the FAQ for more information";
    }
}
