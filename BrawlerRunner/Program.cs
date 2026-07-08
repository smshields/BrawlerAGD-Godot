using BrawlerSim;

// Headless CLI for BrawlerAGD evolution runs. Commands land in Phase 3;
// this stub exists so the project shape (and CI) is in place from day one.
if (args.Length > 0 && args[0] == "bench") { BrawlerRunner.Bench.Run(args[1]); return 0; }
Console.WriteLine($"BrawlerAGD runner — sim core v{SimInfo.Version} ({SimInfo.TicksPerSecond} Hz)");
Console.WriteLine();
Console.WriteLine("Usage (planned, Phase 3):");
Console.WriteLine("  brawler evolve --seed <n> --pop 100 --generations 300 --out runs/<name>");
Console.WriteLine("  brawler replay --game <game.json> --trace <trace.json>");
Console.WriteLine("  brawler reevaluate --run runs/<name> --fitness <version>");
return 0;
