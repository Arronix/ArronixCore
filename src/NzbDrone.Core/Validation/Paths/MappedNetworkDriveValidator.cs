using System.IO;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Validation.Paths
{
    public class MappedNetworkDriveValidator<T> : PropertyValidator<T, string>
    {
        private readonly IRuntimeInfo _runtimeInfo;
        private readonly IDiskProvider _diskProvider;

        private static readonly Regex DriveRegex = new Regex(@"[a-z]\:\\", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public MappedNetworkDriveValidator(IRuntimeInfo runtimeInfo, IDiskProvider diskProvider)
        {
            _runtimeInfo = runtimeInfo;
            _diskProvider = diskProvider;
        }

        public override string Name => "MappedNetworkDriveValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value == null)
            {
                return false;
            }

            if (OsInfo.IsNotWindows)
            {
                return true;
            }

            if (!_runtimeInfo.IsWindowsService)
            {
                return true;
            }

            if (!DriveRegex.IsMatch(value))
            {
                return true;
            }

            var mount = _diskProvider.GetMount(value);

            return mount is not { DriveType: DriveType.Network };
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Mapped Network Drive and Windows Service";
    }
}
