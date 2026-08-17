using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Validation
{
    public class FolderChmodValidator<T> : PropertyValidator<T, string>
    {
        private readonly IDiskProvider _diskProvider;

        public FolderChmodValidator(IDiskProvider diskProvider)
        {
            _diskProvider = diskProvider;
        }

        public override string Name => "FolderChmodValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value == null)
            {
                return false;
            }

            return _diskProvider.IsValidFolderPermissionMask(value);
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Must contain a valid Unix permissions octal";
    }
}
