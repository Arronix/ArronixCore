using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Common.Composition;

public class KnownTypes(List<Type> loadedTypes)
{
    private List<Type> _knownTypes = loadedTypes;

    // So unity can resolve for tests
    public KnownTypes()
        : this(new List<Type>())
    {
    }

    public IEnumerable<Type> GetImplementations(Type contractType)
    {
        return _knownTypes
            .Where(implementation =>
                contractType.IsAssignableFrom(implementation) &&
                !implementation.IsInterface &&
                !implementation.IsAbstract);
    }
}
