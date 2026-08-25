# G06 authoring SDK boundary

## Decision

An extension author references `Arronix.Sdk` plus the format, language, and media-domain packages the
extension composes. `Arronix.Sdk` is a packaging-only metapackage: its sole runtime dependency is
`Arronix.Abstractions`, and it carries `Arronix.Generators` as an analyzer asset. It contributes no runtime
assembly and therefore creates no second CLR identity or loader lifecycle.

The typed CLR definition remains the only authoring model. Generated shapes and erased registration are a
one-way bridge from that definition into Host. They are not moved into a second public schema or replay
builder merely to make the surface look smaller.

## Boundary

The ordinary media author writes a partial class derived from
`MediaType<TItem,TTarget,TRelease,TParser>` and registers it once with `AddMediaType<TMediaType>()`. The
generator supplies `CompiledShapes` and marks it `EditorBrowsable(Never)`. Its public CLR visibility is
retained because the published G01 executable-generator sentinel is immutable and reads that generated
catalog from a separately compiled assembly; authors neither implement nor call it. Capture is an explicit
operation on a hidden interface and does not appear on the concrete media type's public surface.

Some bridge contracts must remain public CLR types because generated code and Host live in different
assemblies, or because a public closed provider interface cannot inherit a less accessible family marker.
Those types and erased members are marked `EditorBrowsable(Never)`. Concrete semantic records implement
visitor dispatch and `Type` carriers explicitly, leaving only their typed constructors and semantic values on
the normal author surface.

No separate binding runtime assembly was added. Such an assembly would add a package and CLR-identity edge
without removing the need for cross-assembly visibility. Keeping the bridge in the bottom contract assembly,
while hiding it from ordinary authoring and enforcing its one-way use, preserves the existing loader identity
model.

## Author diagnostic

`ARX1003` is an error at a media declaration which omits `partial`:

```text
Media type 'Samples' must be declared partial so Arronix can generate its compiled shape
```

The diagnostic tells the author how to satisfy the declaration contract. It does not ask them to implement
the generated projection.

## Enforced properties

Architecture tests now require all of the following:

- binding types and erased bridge members are hidden from ordinary completion lists;
- concrete media types expose no public `Capture`, and their generated `CompiledShapes` override is hidden
  from ordinary completion lists;
- concrete semantic values expose no public visitor, group-type, or row-type carrier;
- typed media extension source contains no binding vocabulary;
- each typed media module performs exactly one media registration;
- `Arronix.Sdk` depends at runtime only on Abstractions, carries Generators only as an analyzer reference,
  packages that analyzer under `analyzers/dotnet/cs`, and emits no runtime SDK DLL;
- the generator project is not independently packable.

## Package and consumer proof

The real `0.9.0` packages were built with `dotnet pack` and inspected. `Arronix.Sdk.0.9.0.nupkg` contains:

```text
README.md
analyzers/dotnet/cs/Arronix.Generators.dll
lib/net11.0/_._
```

Its NuGet dependency group contains only `Arronix.Abstractions 0.9.0`; there is no `Arronix.Sdk.dll` in the
package. A project created outside the repository then restored using only the packed package source and this
reference:

```xml
<PackageReference Include="Arronix.Sdk" Version="0.9.0" />
```

Its partial typed media definition built with zero warnings and zero errors. Removing `partial` failed at the
definition with `ARX1003`; restoring it returned the project to a clean build. This proves the G06 package and
author-diagnostic boundary. G07A retains the stronger permanent proof of packaging, installation, admission,
exact browser type identity, and rendering in an unmodified Host and Client.

## Focused verification

- generator and author-diagnostic tests: 8 passed, 0 failed;
- architecture tests: 369 passed, 1 registered skip, 0 failed;
- architecture build: warnings-as-errors, 0 warnings and 0 errors;
- external package consumer: clean positive build and expected `ARX1003` negative build;
- full solution rail: 2,764 passed, 302 registered skips, 0 failed, and 0 inconclusive from 3,066 cases
  across 12 test projects.
