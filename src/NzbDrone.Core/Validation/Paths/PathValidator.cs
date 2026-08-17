using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Validation.Paths
{
    public static class PathValidation
    {
        public static IRuleBuilderOptions<T, string> IsValidPath<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new PathValidator<T>());
        }
    }

    public class PathValidator<T> : PropertyValidator<T, string>
    {
        public override string Name => "PathValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value == null)
            {
                return false;
            }

            context.MessageFormatter.AppendArgument("path", value);

            return value.IsPathValid(PathValidationType.CurrentOs);
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Invalid Path: '{path}'";
    }
}
