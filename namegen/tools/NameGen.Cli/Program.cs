using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NameGen;
using NameGen.Core;

namespace NameGen.Cli
{
    /// <summary>
    /// Batch review tool. Generates random genomes and dumps names + provenance so
    /// database tuning is a read-the-CSV loop, not a guess-and-recompile loop.
    ///
    ///   dotnet run --project tools/NameGen.Cli -- demo
    ///   dotnet run --project tools/NameGen.Cli -- dump --count 500 --out names.csv
    ///   dotnet run --project tools/NameGen.Cli -- dump --count 200 --register horror --kind stage
    ///   dotnet run --project tools/NameGen.Cli -- dump --count 100 --data ./src/NameGen/Data   (JSON overrides, no recompile)
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "help")
            {
                Console.WriteLine("commands: demo | dump [--count N] [--register R] [--kind character|stage] [--shape single|full] [--seed S] [--out FILE] [--data DIR]");
                return 0;
            }

            var opts = ParseArgs(args.Skip(1).ToList());
            var generator = opts.TryGetValue("data", out var dataDir)
                ? NameGenerator.CreateFromDirectory(dataDir)
                : NameGenerator.CreateDefault();

            return args[0] switch
            {
                "demo" => Demo(generator),
                "dump" => Dump(generator, opts),
                _ => Fail($"unknown command '{args[0]}'"),
            };
        }

        private static int Demo(NameGenerator generator)
        {
            var rng = new Pcg32((ulong)Environment.TickCount64);
            foreach (var register in new[] { "fantasy", "scifi", "horror", "normal" })
            {
                Console.WriteLine($"== {register} ==");
                for (int i = 0; i < 8; i++)
                {
                    var genome = GenomeFactory.RandomCharacter(rng);
                    var name = generator.GenerateCharacterName(genome, new NameOptions { Seed = rng.NextUInt(), Register = register });
                    var traits = string.Join("+", name.SalientTraits.Select(t => t.Name));
                    Console.WriteLine($"  {name.Display,-28} [{name.Shape}] {traits}");
                }
                for (int i = 0; i < 4; i++)
                {
                    var stage = GenomeFactory.RandomStage(rng);
                    var name = generator.GenerateStageName(stage, new NameOptions { Seed = rng.NextUInt(), Register = register });
                    var traits = string.Join("+", name.SalientTraits.Select(t => t.Name));
                    Console.WriteLine($"  {name.Display,-28} [stage]  {traits}");
                }
                Console.WriteLine();
            }
            return 0;
        }

        private static int Dump(NameGenerator generator, Dictionary<string, string> opts)
        {
            int count = opts.TryGetValue("count", out var c) ? int.Parse(c) : 200;
            string kind = opts.TryGetValue("kind", out var k) ? k : "character";
            ulong baseSeed = opts.TryGetValue("seed", out var s) ? ulong.Parse(s) : (ulong)Environment.TickCount64;
            string? register = opts.TryGetValue("register", out var r) ? r : null;
            NameShape? shape = opts.TryGetValue("shape", out var sh)
                ? (sh == "single" ? NameShape.Single : NameShape.Full) : (NameShape?)null;

            var rng = new Pcg32(baseSeed);
            var sb = new StringBuilder();
            sb.AppendLine("name,register,shape,salient_traits,parts,glosses,bleed,mundane");

            for (int i = 0; i < count; i++)
            {
                var options = new NameOptions { Seed = baseSeed + (ulong)i, Register = register, Shape = shape };
                NameResult result = kind == "stage"
                    ? generator.GenerateStageName(GenomeFactory.RandomStage(rng), options)
                    : generator.GenerateCharacterName(GenomeFactory.RandomCharacter(rng), options);

                sb.Append(Csv(result.Display)).Append(',')
                  .Append(result.Register).Append(',')
                  .Append(result.Shape).Append(',')
                  .Append(Csv(string.Join("+", result.SalientTraits.Select(t => $"{t.Name}:{t.Score:0.00}")))).Append(',')
                  .Append(Csv(string.Join("|", result.Parts.Select(p => p.Form)))).Append(',')
                  .Append(Csv(string.Join("|", result.Parts.Select(p => p.Gloss)))).Append(',')
                  .Append(result.Parts.Any(p => p.IsBleed) ? "y" : "").Append(',')
                  .AppendLine(result.Parts.Any(p => p.IsMundane) ? "y" : "");
            }

            if (opts.TryGetValue("out", out var path))
            {
                File.WriteAllText(path, sb.ToString());
                Console.WriteLine($"wrote {count} rows to {path}");
            }
            else Console.Write(sb.ToString());
            return 0;
        }

        private static string Csv(string s) => s.Contains(',') || s.Contains('"')
            ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        private static Dictionary<string, string> ParseArgs(List<string> args)
        {
            var result = new Dictionary<string, string>();
            for (int i = 0; i < args.Count - 1; i++)
                if (args[i].StartsWith("--")) result[args[i].Substring(2)] = args[i + 1];
            return result;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            return 1;
        }
    }

    /// <summary>Uniform random genomes over the schema ranges, standing in for the game's evolver.</summary>
    public static class GenomeFactory
    {
        public static CharacterGenome RandomCharacter(Pcg32 rng)
        {
            float U(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            var p = new Dictionary<string, float>
            {
                ["maxGroundSpeed"] = U(2, 10), ["maxAirSpeed"] = U(2, 10),
                ["groundAccelerationFactor"] = U(0, 1), ["airAccelerationFactor"] = U(0, 1),
                ["groundJumpForce"] = U(1, 15), ["airJumpForce"] = U(1, 15),
                ["mass"] = U(0.5f, 2.5f), ["drag"] = U(1, 6),
                ["widthScalar"] = U(0.7f, 1.5f), ["heightScalar"] = U(0.5f, 1.5f),
                ["gravityScalar"] = U(0.3f, 1.3f), ["hitstunDamageScalar"] = U(0.1f, 0.3f),
                ["fastFallAcceleration"] = U(0, 15),
                ["crouchHeightRatio"] = U(0.4f, 0.9f), ["crouchMoveSpeed"] = U(0.3f, 1.5f),
                ["directionalInfluence"] = rng.NextBool(0.5) ? U(0.02f, 0.10f) : 0f,
            };
            var moves = new List<MoveGenome>();
            for (int i = 0; i < 3; i++)
                moves.Add(new MoveGenome(MoveKind.Melee, new Dictionary<string, float>
                {
                    ["moveDist"] = U(0.8f, 1.5f), ["damageFactor"] = U(0, 10),
                    ["knockbackScalar"] = U(1, 16), ["knockbackModY"] = U(-1, 1),
                    ["warmUpDuration"] = U(0.1f, 0.6f), ["coolDownDuration"] = U(0.1f, 0.6f),
                    ["hitstunDuration"] = U(0, 1),
                }));
            if (rng.NextBool(0.5))
                moves.Add(new MoveGenome(MoveKind.Projectile, new Dictionary<string, float>
                {
                    ["pathShape"] = (float)(rng.NextDouble() * 3), ["velocity"] = U(3, 15),
                    ["damageFactor"] = U(0, 10), ["doesRotate"] = (float)rng.NextDouble(),
                    ["hitsSelf"] = (float)rng.NextDouble(),
                }));
            if (rng.NextBool(0.7))
                moves.Add(new MoveGenome(MoveKind.Shield, new Dictionary<string, float>
                {
                    ["initialSize"] = U(0.5f, 2f), ["regenRate"] = U(0.05f, 0.5f),
                    ["holdDegradationRate"] = U(0.05f, 0.4f), ["reflect"] = (float)rng.NextDouble(),
                }));
            if (rng.NextBool(0.7))
                moves.Add(new MoveGenome(MoveKind.Dash, new Dictionary<string, float>
                {
                    ["acceleration"] = U(6, 18),
                    ["warmUpInvulnerable"] = (float)rng.NextDouble(),
                    ["durationInvulnerable"] = (float)rng.NextDouble(),
                    ["reflect"] = (float)rng.NextDouble(),
                }));
            return new CharacterGenome(p, moves);
        }

        public static StageGenome RandomStage(Pcg32 rng)
        {
            float U(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            return new StageGenome(new Dictionary<string, float>
            {
                ["visibleHalfWidth"] = U(4.9f, 48.9f), ["visibleHalfHeight"] = U(2.75f, 27.5f),
                ["koMarginFraction"] = U(0.05f, 0.25f), ["platformCount"] = U(2, 16),
                ["maxPlatformSize"] = U(3, 14), ["mirrored"] = (float)rng.NextDouble(),
                ["platformSpawnDuration"] = rng.NextBool(0.5) ? U(1, 5) : 0f,
                ["spawnInvulnDuration"] = rng.NextBool(0.5) ? U(1, 3) : 0f,
            });
        }
    }
}
