# Arronix SDK

Reference `Arronix.Sdk` when authoring an Arronix extension. The package supplies the semantic
`Arronix.Abstractions` contracts together with the compile-time media-shape generator and author diagnostics.

Media packages add only the format and language packages they compose. They do not reference Arronix Host,
Client, loader, generated binding types, or the generator as a separate package.
