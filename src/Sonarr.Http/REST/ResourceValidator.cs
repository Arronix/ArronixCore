using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FluentValidation;
using Sonarr.Http.ClientSchema;

namespace Sonarr.Http.REST
{
    public class ResourceValidator<TResource> : AbstractValidator<TResource>
    {
        public IRuleBuilderInitial<TResource, TProperty> RuleForField<TProperty>(Expression<Func<TResource, IEnumerable<Field>>> fieldListAccessor, string fieldName)
        {
            var accessor = fieldListAccessor.Compile();
            var ruleBuilder = RuleFor(c => (TProperty)GetValue(c, accessor, fieldName));

            // FluentValidation 12 no longer exposes PropertyRule/RuleBuilder publicly, so the rule that
            // RuleFor just appended is retrieved from the validator itself to be named after the field.
            var rule = this.Last();
            rule.PropertyName = fieldName;
            rule.SetDisplayName(fieldName);

            return ruleBuilder;
        }

        private static object GetValue(object container, Func<TResource, IEnumerable<Field>> fieldListAccessor, string fieldName)
        {
            var resource = fieldListAccessor((TResource)container).SingleOrDefault(c => c.Name == fieldName);

            return resource?.Value;
        }
    }
}
