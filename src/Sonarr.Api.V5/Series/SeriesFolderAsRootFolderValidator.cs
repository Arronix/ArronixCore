using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;

namespace Sonarr.Api.V5.Series
{
    public class SeriesFolderAsRootFolderValidator : PropertyValidator<SeriesResource, string>
    {
        private readonly IBuildFileNames _fileNameBuilder;

        public SeriesFolderAsRootFolderValidator(IBuildFileNames fileNameBuilder)
        {
            _fileNameBuilder = fileNameBuilder;
        }

        public override string Name => "SeriesFolderAsRootFolderValidator";

        public override bool IsValid(ValidationContext<SeriesResource> context, string value)
        {
            if (value == null)
            {
                return true;
            }

            if (value.IsNullOrWhiteSpace())
            {
                return true;
            }

            var rootFolder = new DirectoryInfo(value).Name;
            var series = context.InstanceToValidate.ToModel();
            var seriesFolder = _fileNameBuilder.GetSeriesFolder(series);

            context.MessageFormatter.AppendArgument("rootFolderPath", value);
            context.MessageFormatter.AppendArgument("seriesFolder", seriesFolder);

            if (seriesFolder == rootFolder)
            {
                return false;
            }

            var distance = seriesFolder.LevenshteinDistance(rootFolder);

            return distance >= Math.Max(1, seriesFolder.Length * 0.2);
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Root folder path '{rootFolderPath}' contains series folder '{seriesFolder}'";
    }
}
