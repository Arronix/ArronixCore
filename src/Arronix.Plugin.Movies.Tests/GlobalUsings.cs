global using System;
global using System.Collections.Generic;

// The movies media domain now ships in its own assembly and its own namespace, so the tests name it once
// here rather than in every file. The alternative - a using in each file - would have rewritten the
// regression sources whose exact bytes the compatibility ledger locks.
global using Arronix.Media.Movies;
global using NUnit.Framework;
