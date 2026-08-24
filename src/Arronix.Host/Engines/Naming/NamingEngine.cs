using System.IO;
using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;
using Arronix.Common.Naming;
using Arronix.Host.Languages;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// The host naming engine: renders a kind's declared templates from shape-derived tokens and file facts.
/// </summary>
/// <remarks>
/// <para>
/// Engine E7 of <c>docs/design/declarative-media-kinds.md</c> §2.7, executing a
/// <see cref="NamingDeclaration"/>: default templates per slot, template-selection rows over the closed
/// predicate vocabulary, the folder spine with optional segments, and token fallback rows. The grammar,
/// modifiers, truncation, sanitization and substitution live in this engine and
/// <see cref="Arronix.Common.Naming.TokenSanitizer"/>/<see cref="Arronix.Common.Naming.TextFolding"/> —
/// written once, deleting the four per-plugin formatter copies.
/// </para>
/// <para>
/// Selection-rule predicates reach the subjects <c>fields.&lt;id&gt;</c>, <c>options.&lt;id&gt;</c>,
/// <c>file.&lt;property&gt;</c>, <c>units.count</c> and <c>token.&lt;canonical&gt;</c>; fallback-rule
/// paths reach <c>file.SceneName</c>, <c>file.OriginalFileName</c> and <c>file.Path</c>. Both
/// vocabularies are closed here, exactly as the exhibit uses them.
/// </para>
/// </remarks>
internal sealed class NamingEngine
{
    private readonly NamingDeclaration _declaration;
    private readonly TemplateRenderer _renderer;
    private readonly Dictionary<string, CompiledNamingTemplate> _templates = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="NamingEngine"/> class, compiling every declared
    /// template.
    /// </summary>
    /// <param name="declaration">The kind's naming declaration.</param>
    /// <param name="options">The render options.</param>
    /// <param name="languages">The installed language operations.</param>
    /// <exception cref="ArgumentException">
    /// A declared default template does not parse. A plugin default that fails compilation is a plugin
    /// defect and must not reach a settings page (naming design resolution #19).
    /// </exception>
    public NamingEngine(
        NamingDeclaration declaration,
        RenderOptions? options = null,
        LanguageTextService? languages = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        _declaration = declaration;
        _renderer = new TemplateRenderer(options, languages);

        foreach (var (slotId, text) in declaration.DefaultTemplates)
        {
            var compiled = NamingTemplateParser.Parse(text);

            if (!compiled.IsValid)
            {
                throw new ArgumentException(
                    $"The declared template for slot '{slotId}' is invalid: {string.Join(" ", compiled.Errors)}",
                    nameof(declaration));
            }

            _templates[slotId] = compiled;
        }
    }

    /// <summary>
    /// Selects the template slot for a render: the first selection row whose predicate holds, else the
    /// named default slot.
    /// </summary>
    /// <param name="defaultSlotId">The slot rendered when no row claims the render.</param>
    /// <param name="context">The predicate subject lookup.</param>
    /// <param name="bindings">The resolved tokens, for missing-token degradation.</param>
    /// <returns>The chosen slot identifier, or null when neither a row nor the default slot exists.</returns>
    /// <remarks>
    /// Rows run in declared order and the first passing row wins — rule order is semantic and no engine
    /// may sort a rule table (<c>declarative-media-kinds.md</c> §1.3). A row's
    /// <see cref="TemplateSelectionRule.FallbackTemplateId"/> is taken when the chosen template
    /// references a token with no binding — the declared missing-coordinate degradation.
    /// </remarks>
    public string? SelectSlot(
        string defaultSlotId,
        Func<string, IReadOnlyList<string>?> context,
        NamingTokenBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(defaultSlotId);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bindings);

        foreach (var rule in _declaration.Selection)
        {
            if (rule.TemplateId is null || !DeclarationPredicate.Holds(rule.When, context))
            {
                continue;
            }

            if (rule.FallbackTemplateId is not null
                && _templates.TryGetValue(rule.TemplateId, out var chosen)
                && !TokensAllBound(chosen, bindings))
            {
                return rule.FallbackTemplateId;
            }

            return rule.TemplateId;
        }

        return _templates.ContainsKey(defaultSlotId) ? defaultSlotId : null;
    }

    /// <summary>
    /// Renders one slot's template.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="bindings">The resolved tokens.</param>
    /// <param name="file">The file being named, feeding the declared fallback rows.</param>
    /// <returns>The rendered component, empty when the slot does not exist.</returns>
    public string RenderSlot(string slotId, NamingTokenBindings bindings, MediaFileFacts? file)
    {
        ArgumentNullException.ThrowIfNull(slotId);
        ArgumentNullException.ThrowIfNull(bindings);

        if (!_templates.TryGetValue(slotId, out var template))
        {
            return string.Empty;
        }

        ApplyFallbacks(bindings, file);

        var rendered = _renderer.RenderComponent(template, bindings);

        if (rendered.Length == 0 && file is not null && FileNameFallback(file) is { Length: > 0 } fallback)
        {
            // A template rendering to nothing falls back to the sanitized original stem — losing the
            // file to a bad template would be worse (exhibit section 6; MoviesNaming.cs:755-760).
            return fallback;
        }

        return rendered;
    }

    /// <summary>
    /// Renders an ad hoc template text, as the rename preview does for a user-typed template.
    /// </summary>
    /// <param name="templateText">The template.</param>
    /// <param name="bindings">The resolved tokens.</param>
    /// <param name="file">The file being named.</param>
    /// <returns>The rendered component.</returns>
    /// <exception cref="ArgumentException">The template does not parse.</exception>
    public string RenderTemplate(string templateText, NamingTokenBindings bindings, MediaFileFacts? file)
    {
        ArgumentNullException.ThrowIfNull(templateText);
        ArgumentNullException.ThrowIfNull(bindings);

        var compiled = NamingTemplateParser.Parse(templateText);

        if (!compiled.IsValid)
        {
            throw new ArgumentException(
                $"The template '{templateText}' is invalid: {string.Join(" ", compiled.Errors)}",
                nameof(templateText));
        }

        ApplyFallbacks(bindings, file);

        var rendered = _renderer.RenderComponent(compiled, bindings);

        if (rendered.Length == 0 && file is not null && FileNameFallback(file) is { Length: > 0 } fallback)
        {
            return fallback;
        }

        return rendered;
    }

    /// <summary>
    /// Renders the folder spine into path components under the root.
    /// </summary>
    /// <param name="context">The predicate subject lookup deciding optional segments.</param>
    /// <param name="bindings">The resolved tokens.</param>
    /// <param name="file">The file, feeding fallback rows.</param>
    /// <returns>The folder components, root-most first, excluding the root itself.</returns>
    /// <remarks>
    /// The spine grammar is the declared skeleton — <c>{root}/[optional-segment/]{folder}</c> — where a
    /// bare or braced name references a template slot and a bracketed segment renders only when a
    /// selection row inserted it (<see cref="TemplateSelectionRule.InsertSpineSegment"/>).
    /// </remarks>
    public IReadOnlyList<string> RenderSpine(
        Func<string, IReadOnlyList<string>?> context,
        NamingTokenBindings bindings,
        MediaFileFacts? file)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bindings);

        var inserted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in _declaration.Selection)
        {
            if (rule.InsertSpineSegment is { Length: > 0 } segment && DeclarationPredicate.Holds(rule.When, context))
            {
                inserted.Add(segment);
            }
        }

        var components = new List<string>();

        // An optional segment carries its slash inside the brackets ("[collection-folder/]"); move the
        // slash outside before splitting so the bracket pair survives segmentation.
        var spine = _declaration.FolderSpine.Replace("/]", "]/", StringComparison.Ordinal);

        foreach (var rawSegment in spine.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Trim();
            var optional = segment.StartsWith('[') && segment.EndsWith(']');

            if (optional)
            {
                segment = segment[1..^1].Trim();
            }

            var braced = segment.StartsWith('{') && segment.EndsWith('}');

            if (braced)
            {
                segment = segment[1..^1].Trim();
            }

            if (string.Equals(segment, "root", StringComparison.OrdinalIgnoreCase))
            {
                // The root is host library configuration, never rendered here.
                continue;
            }

            if (optional && !inserted.Contains(segment))
            {
                continue;
            }

            var rendered = RenderSlot(segment, bindings, file);

            if (rendered.Length > 0)
            {
                components.Add(rendered);
            }
        }

        return components;
    }

    private static bool TokensAllBound(CompiledNamingTemplate template, NamingTokenBindings bindings) =>
        template.ReferencedTokens.All(token =>
            bindings.TryGet(token, out var binding) && binding.Values.Any(value => value.Length > 0));

    private void ApplyFallbacks(NamingTokenBindings bindings, MediaFileFacts? file)
    {
        foreach (var rule in _declaration.Fallbacks)
        {
            var canonical = NamingTokenName.Canonicalize(rule.Token);

            if (canonical.Length == 0)
            {
                // The whole-name fallback row ("*file-name*") is applied by the render wrappers.
                continue;
            }

            if (bindings.TryGet(canonical, out var bound) && bound.Values.Any(value => value.Length > 0))
            {
                continue;
            }

            foreach (var path in rule.Order)
            {
                if (ResolveFallbackPath(path, file) is { Length: > 0 } value)
                {
                    bindings.Set(TokenBinding.Of(rule.Token.Trim('{', '}', '*'), value, TokenElasticity.Elastic));
                    break;
                }
            }
        }
    }

    private static string? ResolveFallbackPath(string path, MediaFileFacts? file) => path switch
    {
        "file.SceneName" => file?.SceneName,
        "file.OriginalFileName" => Stem(file?.OriginalFileName),
        "file.Path" => Stem(file?.Path),
        _ => null,
    };

    private static string? FileNameFallback(MediaFileFacts file)
    {
        var stem = Stem(file.OriginalFileName) ?? Stem(file.Path);

        return stem is { Length: > 0 } ? TokenSanitizer.SanitizeComponent(stem) : null;
    }

    private static string? Stem(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFileNameWithoutExtension(path);
}
