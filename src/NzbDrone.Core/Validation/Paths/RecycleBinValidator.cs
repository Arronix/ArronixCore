using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Validation.Paths
{
    public class RecycleBinValidator<T> : PropertyValidator<T, string>
    {
        private readonly IConfigService _configService;

        public RecycleBinValidator(IConfigService configService)
        {
            _configService = configService;
        }

        public override string Name => "RecycleBinValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            var recycleBin = _configService.RecycleBin;

            if (value == null || recycleBin.IsNullOrWhiteSpace())
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", value);

            if (recycleBin.PathEquals(value))
            {
                context.MessageFormatter.AppendArgument("relationship", "set to");

                return false;
            }

            if (recycleBin.IsParentPath(value))
            {
                context.MessageFormatter.AppendArgument("relationship", "child of");

                return false;
            }

            return true;
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Path '{path}' is {relationship} configured recycle bin folder";
    }
}
