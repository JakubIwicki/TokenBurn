var command = args.Length > 0 ? args[0] : "";

if (string.Equals(command, "backfill", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Collector backfill — implemented in Phase 1");
    return;
}

Console.WriteLine("Usage: TokenBurn.Collector backfill");
