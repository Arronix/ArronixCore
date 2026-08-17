using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace Sonarr.Api.V3.Series
{
    public class SeriesEditorValidator : AbstractValidator<NzbDrone.Core.Tv.Series>
    {
        public SeriesEditorValidator(RootFolderExistsValidator<NzbDrone.Core.Tv.Series> rootFolderExistsValidator, QualityProfileExistsValidator<NzbDrone.Core.Tv.Series> qualityProfileExistsValidator)
        {
            RuleFor(s => s.RootFolderPath).Cascade(CascadeMode.Stop)
                .IsValidPath()
                .SetValidator(rootFolderExistsValidator)
                .When(s => s.RootFolderPath.IsNotNullOrWhiteSpace());

            RuleFor(c => c.QualityProfileId).Cascade(CascadeMode.Stop)
                .ValidId()
                .SetValidator(qualityProfileExistsValidator);
        }
    }
}
