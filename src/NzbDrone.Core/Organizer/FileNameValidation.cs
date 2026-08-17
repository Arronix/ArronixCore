using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Organizer
{
    public static class FileNameValidation
    {
        private static readonly Regex SeasonFolderRegex = new Regex(@"(\{season(\:\d+)?\})",
                                                                            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex OriginalTokenRegex = new Regex(@"(\{original[- ._](?:title|filename)\})",
                                                                            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static IRuleBuilderOptions<T, string> ValidEpisodeFormat<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            ruleBuilder.SetValidator(new NotEmptyValidator<T, string>());
            ruleBuilder.SetValidator(new IllegalCharactersValidator<T>());

            return ruleBuilder.SetValidator(new ValidStandardEpisodeFormatValidator<T>());
        }

        public static IRuleBuilderOptions<T, string> ValidDailyEpisodeFormat<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            ruleBuilder.SetValidator(new NotEmptyValidator<T, string>());
            ruleBuilder.SetValidator(new IllegalCharactersValidator<T>());

            return ruleBuilder.SetValidator(new ValidDailyEpisodeFormatValidator<T>());
        }

        public static IRuleBuilderOptions<T, string> ValidAnimeEpisodeFormat<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            ruleBuilder.SetValidator(new NotEmptyValidator<T, string>());
            ruleBuilder.SetValidator(new IllegalCharactersValidator<T>());

            return ruleBuilder.SetValidator(new ValidAnimeEpisodeFormatValidator<T>());
        }

        public static IRuleBuilderOptions<T, string> ValidSeriesFolderFormat<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            ruleBuilder.SetValidator(new NotEmptyValidator<T, string>());
            ruleBuilder.SetValidator(new IllegalCharactersValidator<T>());

            return ruleBuilder.SetValidator(new RegularExpressionValidator<T>(FileNameBuilder.SeriesTitleRegex)).WithMessage("Must contain series title");
        }

        public static IRuleBuilderOptions<T, string> ValidSeasonFolderFormat<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            ruleBuilder.SetValidator(new NotEmptyValidator<T, string>());
            ruleBuilder.SetValidator(new IllegalCharactersValidator<T>());

            return ruleBuilder.SetValidator(new RegularExpressionValidator<T>(SeasonFolderRegex)).WithMessage("Must contain season number");
        }

        public static IRuleBuilderOptions<T, string> ValidSpecialsFolderFormat<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            ruleBuilder.SetValidator(new NotEmptyValidator<T, string>());

            return ruleBuilder.SetValidator(new IllegalCharactersValidator<T>());
        }

        public static IRuleBuilderOptions<T, string> ValidCustomColonReplacement<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            ruleBuilder.SetValidator(new IllegalColonCharactersValidator<T>());

            return ruleBuilder.SetValidator(new IllegalCharactersValidator<T>());
        }
    }

    public class ValidStandardEpisodeFormatValidator<T> : PropertyValidator<T, string>
    {
        public override string Name => "ValidStandardEpisodeFormatValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value == null)
            {
                return false;
            }

            return FileNameBuilder.SeasonEpisodePatternRegex.IsMatch(value) ||
                   (FileNameBuilder.SeasonRegex.IsMatch(value) && FileNameBuilder.EpisodeRegex.IsMatch(value)) ||
                   FileNameValidation.OriginalTokenRegex.IsMatch(value);
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Must contain season and episode numbers OR Original Title";
    }

    public class ValidDailyEpisodeFormatValidator<T> : PropertyValidator<T, string>
    {
        public override string Name => "ValidDailyEpisodeFormatValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value == null)
            {
                return false;
            }

            return FileNameBuilder.SeasonEpisodePatternRegex.IsMatch(value) ||
                   (FileNameBuilder.SeasonRegex.IsMatch(value) && FileNameBuilder.EpisodeRegex.IsMatch(value)) ||
                   FileNameBuilder.AirDateRegex.IsMatch(value) ||
                   FileNameValidation.OriginalTokenRegex.IsMatch(value);
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Must contain Air Date OR Season and Episode OR Original Title";
    }

    public class ValidAnimeEpisodeFormatValidator<T> : PropertyValidator<T, string>
    {
        public override string Name => "ValidAnimeEpisodeFormatValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value == null)
            {
                return false;
            }

            return FileNameBuilder.SeasonEpisodePatternRegex.IsMatch(value) ||
                   (FileNameBuilder.SeasonRegex.IsMatch(value) && FileNameBuilder.EpisodeRegex.IsMatch(value)) ||
                   FileNameBuilder.AbsoluteEpisodePatternRegex.IsMatch(value) ||
                   FileNameValidation.OriginalTokenRegex.IsMatch(value);
        }

        protected override string GetDefaultMessageTemplate(string errorCode) =>
            "Must contain Absolute Episode number OR Season and Episode OR Original Title";
    }

    public class IllegalCharactersValidator<T> : PropertyValidator<T, string>
    {
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        public override string Name => "IllegalCharactersValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return true;
            }

            var invalidCharacters = InvalidPathChars.Where(i => value!.IndexOf(i) >= 0).ToList();

            if (invalidCharacters.Any())
            {
                context.MessageFormatter.AppendArgument("InvalidCharacters", string.Join("", invalidCharacters));
                return false;
            }

            return true;
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Contains illegal characters: {InvalidCharacters}";
    }

    public class IllegalColonCharactersValidator<T> : PropertyValidator<T, string>
    {
        private static readonly string[] InvalidPathChars = FileNameBuilder.BadCharacters.Concat(new[] { ":" }).ToArray();

        public override string Name => "IllegalColonCharactersValidator";

        public override bool IsValid(ValidationContext<T> context, string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return true;
            }

            var invalidCharacters = InvalidPathChars.Where(i => value!.IndexOf(i, StringComparison.Ordinal) >= 0).ToList();

            if (invalidCharacters.Any())
            {
                context.MessageFormatter.AppendArgument("InvalidCharacters", string.Join("", invalidCharacters));
                return false;
            }

            return true;
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Contains illegal characters: {InvalidCharacters}";
    }
}
