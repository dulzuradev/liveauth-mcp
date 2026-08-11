namespace LiveAuthCore.Models.PermitSignal;

public sealed class PermitSignalOptions
{
    public const string SectionName = "PermitSignal";

    public bool SeedDemoData { get; set; } = false;
    public PermitSignalToolOptions Tools { get; set; } = new();
    public PermitSignalSyncOptions Sync { get; set; } = new();
    public PermitSignalScoringOptions Scoring { get; set; } = new();
}

public sealed class PermitSignalToolOptions
{
    public ToolPriceOptions SearchProjects { get; set; } = new() { PriceSats = 5 };
    public ToolPriceOptions FindOpportunities { get; set; } = new() { PriceSats = 10 };
    public ToolPriceOptions AnalyzeProject { get; set; } = new() { PriceSats = 15 };
    public ToolPriceOptions PropertyHistory { get; set; } = new() { PriceSats = 20 };
}

public sealed class ToolPriceOptions
{
    public int PriceSats { get; set; } = 1;
}

public sealed class PermitSignalSyncOptions
{
    public bool Enabled { get; set; }
    public int InitialLookbackDays { get; set; } = 30;
    public int IntervalMinutes { get; set; } = 60;
    public int PageSize { get; set; } = 500;
    public int MaximumPagesPerSource { get; set; } = 10;
}

public sealed class PermitSignalScoringOptions
{
    public int IssuedWithin3Days { get; set; } = 20;
    public int IssuedWithin7Days { get; set; } = 15;
    public int IssuedWithin30Days { get; set; } = 5;
    public int Commercial { get; set; } = 15;
    public int ValueOverOneMillion { get; set; } = 25;
    public int ValueOverTwoHundredFiftyThousand { get; set; } = 15;
    public int ValueOverOneHundredThousand { get; set; } = 8;
    public int StrongTradeMatch { get; set; } = 25;
    public int WeakTradeMatch { get; set; } = 10;
    public int NewConstruction { get; set; } = 15;
    public int Renovation { get; set; } = 5;
}
