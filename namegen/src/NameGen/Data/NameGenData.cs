using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NameGen.Data
{
    /// <summary>
    /// The loaded database. Default content ships as embedded resources inside the DLL,
    /// so the library is a single-reference drop-in. Any file can be overridden from an
    /// external directory for iteration without recompiling.
    /// </summary>
    public sealed class NameGenData
    {
        public IReadOnlyList<RegisterDef> Registers { get; }
        public TraitConfigDef Traits { get; }
        public SchemaRangesDef Ranges { get; }
        public MundaneDef Mundane { get; }
        public BlocklistDef Blocklist { get; }

        private static readonly string[] RegisterFiles = { "fantasy", "scifi", "horror", "normal" };

        public NameGenData(IReadOnlyList<RegisterDef> registers, TraitConfigDef traits,
            SchemaRangesDef ranges, MundaneDef mundane, BlocklistDef blocklist)
        {
            Registers = registers;
            Traits = traits;
            Ranges = ranges;
            Mundane = mundane;
            Blocklist = blocklist;
            Validate();
        }

        /// <summary>Load the database shipped inside the assembly.</summary>
        public static NameGenData LoadEmbedded() => Load(null);

        /// <summary>
        /// Load with external overrides: for each data file, if
        /// <paramref name="overrideDirectory"/> contains a file of the same name
        /// (registers under a "registers" subdirectory), it replaces the embedded one.
        /// </summary>
        public static NameGenData LoadFromDirectory(string overrideDirectory) => Load(overrideDirectory);

        private static NameGenData Load(string? dir)
        {
            var registers = RegisterFiles
                .Select(n => DataMapper.MapRegister(ReadText($"registers/{n}.json", dir), n))
                .ToList();
            var traits = DataMapper.MapTraits(ReadText("traits.json", dir), "traits");
            var ranges = DataMapper.MapRanges(ReadText("schema-ranges.json", dir), "schema-ranges");
            var mundane = DataMapper.MapMundane(ReadText("mundane.json", dir), "mundane");
            var blocklist = DataMapper.MapBlocklist(ReadText("blocklist.json", dir), "blocklist");
            return new NameGenData(registers, traits, ranges, mundane, blocklist);
        }

        private static string ReadText(string relativePath, string? dir)
        {
            if (dir != null)
            {
                string external = Path.Combine(dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(external))
                    return File.ReadAllText(external);
            }
            return ReadEmbedded(relativePath);
        }

        private static string ReadEmbedded(string relativePath)
        {
            var asm = typeof(NameGenData).GetTypeInfo().Assembly;
            // EmbeddedResource names: NameGen.Data.<path with dots>. The registers
            // subfolder becomes "...Data.registers.<name>.json".
            string resourceName = "NameGen.Data." + relativePath.Replace('/', '.');
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Embedded resource '{resourceName}' not found. " +
                    $"Available: {string.Join(", ", asm.GetManifestResourceNames())}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>Referential integrity checks; throws on authoring errors so they surface at load, not mid-game.</summary>
        private void Validate()
        {
            var traitNames = new HashSet<string>(
                Traits.Character.Select(t => t.Name).Concat(Traits.Stage.Select(t => t.Name)),
                StringComparer.OrdinalIgnoreCase);
            var validPositions = new HashSet<string>(new[] { "prefix", "suffix", "standalone", "given", "family", "adjective", "place" },
                StringComparer.OrdinalIgnoreCase);
            var validJoins = new HashSet<string>(new[] { "fuse", "space", "hyphen", "apostrophe" },
                StringComparer.OrdinalIgnoreCase);

            var errors = new List<string>();

            foreach (var reg in Registers)
            {
                if (reg.Morphemes.Count == 0) errors.Add($"register '{reg.Name}' has no morphemes");
                if (reg.Templates.Count == 0) errors.Add($"register '{reg.Name}' has no templates");

                foreach (var m in reg.Morphemes)
                {
                    if (string.IsNullOrWhiteSpace(m.Form)) errors.Add($"register '{reg.Name}' has a morpheme with empty form");
                    foreach (var p in m.Positions)
                        if (!validPositions.Contains(p)) errors.Add($"morpheme '{m.Form}' ({reg.Name}): unknown position '{p}'");
                    foreach (var t in m.Tags)
                        if (!traitNames.Contains(t)) errors.Add($"morpheme '{m.Form}' ({reg.Name}): unknown tag '{t}'");
                }

                if (!reg.Templates.Any(t => t.Kind == "character"))
                    errors.Add($"register '{reg.Name}' has no character templates");
                if (!reg.Templates.Any(t => t.Kind == "stage"))
                    errors.Add($"register '{reg.Name}' has no stage templates");

                foreach (var t in reg.Templates)
                {
                    if (t.Shape != "single" && t.Shape != "full")
                        errors.Add($"register '{reg.Name}' template: unknown shape '{t.Shape}'");
                    if (t.Kind != "character" && t.Kind != "stage")
                        errors.Add($"register '{reg.Name}' template: unknown kind '{t.Kind}'");
                    foreach (var s in t.Slots)
                    {
                        if (!validPositions.Contains(s.Position)) errors.Add($"register '{reg.Name}' template slot: unknown position '{s.Position}'");
                        if (!validJoins.Contains(s.Join)) errors.Add($"register '{reg.Name}' template slot: unknown join '{s.Join}'");
                        if (s.Literal == null &&
                            !reg.Morphemes.Any(m => m.Positions.Contains(s.Position, StringComparer.OrdinalIgnoreCase)))
                            errors.Add($"register '{reg.Name}': no morphemes fill position '{s.Position}' required by a template");
                    }
                    // Shape sanity: every template must produce at least one part.
                    if (t.Slots.Count == 0) errors.Add($"register '{reg.Name}' has a template with no slots");
                }
            }

            foreach (var m in Mundane.Morphemes)
                foreach (var t in m.Tags)
                    if (!traitNames.Contains(t)) errors.Add($"mundane morpheme '{m.Form}': unknown tag '{t}'");

            if (errors.Count > 0)
                throw new InvalidDataException("NameGen data validation failed:\n  " + string.Join("\n  ", errors));
        }
    }
}
