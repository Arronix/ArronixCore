using System.Collections.Generic;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;

namespace Sonarr.Http.Validation
{
    public class EmptyCollectionValidator<T, TProp> : PropertyValidator<T, IEnumerable<TProp>>
    {
        public override string Name => "EmptyCollectionValidator";

        public override bool IsValid(ValidationContext<T> context, IEnumerable<TProp> value)
        {
            if (value == null)
            {
                return true;
            }

            return value.Empty();
        }

        protected override string GetDefaultMessageTemplate(string errorCode) => "Collection Must Be Empty";
    }
}
