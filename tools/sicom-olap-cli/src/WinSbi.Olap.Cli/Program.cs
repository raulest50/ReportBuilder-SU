using System.CommandLine;
using WinSbi.Olap.Core;

var root = new RootCommand("Cross-platform SICOM OLAP CLI for XMLA/MDX over msmdpump.dll.");

var sourceOption = new Option<string>("--source")
{
    Description = "Secret-backed endpoint to use: liqs or agents.",
    DefaultValueFactory = _ => "liqs"
};
var urlOption = new Option<string?>("--url")
{
    Description = "XMLA msmdpump.dll URL. Overrides --source."
};
var userOption = new Option<string?>("--user")
{
    Description = "XMLA username. Defaults to USER_CUBO_SICOM."
};
var passwordOption = new Option<string?>("--password")
{
    Description = "XMLA password. Defaults to PASSWORD_CUBO_SICOM."
};
var authOption = new Option<string>("--auth")
{
    Description = "Auth mode: basic or challenge.",
    DefaultValueFactory = _ => "basic"
};
var timeoutOption = new Option<int>("--timeout")
{
    Description = "XMLA server timeout in seconds.",
    DefaultValueFactory = _ => 120
};
var formatOption = new Option<string>("--format")
{
    Description = "Output format: table, json, or csv.",
    DefaultValueFactory = _ => "table"
};
var outputOption = new Option<string?>("--output")
{
    Description = "Write output to this path instead of stdout."
};
var xmlaRequestOutputOption = new Option<string?>("--xmla-request-output")
{
    Description = "Write XMLA request XML for debugging. Never contains the password."
};
var xmlaResponseOutputOption = new Option<string?>("--xmla-response-output")
{
    Description = "Write XMLA response XML for debugging."
};

var catalogOption = new Option<string>("--catalog")
{
    Description = "Analysis Services catalog/database name."
};
var cubeOption = new Option<string>("--cube")
{
    Description = "Cube name."
};
var mdxOption = new Option<string?>("--mdx")
{
    Description = "Inline MDX statement."
};
var mdxFileOption = new Option<string?>("--mdx-file")
{
    Description = "Read MDX statement from a file."
};
var yearOption = new Option<int?>("--year")
{
    Description = "Report year, for example 2026."
};
var startYearOption = new Option<int?>("--start-year")
{
    Description = "Historical report start year, for example 2011."
};
var startMonthOption = new Option<int>("--start-month")
{
    Description = "Historical report start month: 1 through 12.",
    DefaultValueFactory = _ => 1
};
var endYearOption = new Option<int?>("--end-year")
{
    Description = "Report end year, for example 2026."
};
var endMonthOption = new Option<int?>("--end-month")
{
    Description = "Report end month: 1 through 12."
};
var quarterOption = new Option<int?>("--quarter")
{
    Description = "Calendar quarter: 1, 2, 3, or 4."
};
var semesterOption = new Option<int?>("--semester")
{
    Description = "Calendar semester: 1 or 2."
};
var annualOption = new Option<bool>("--annual")
{
    Description = "Use the complete calendar year (January through December)."
};
var monthsOption = new Option<string?>("--months")
{
    Description = "Comma-separated month numbers, for example 4,5,6."
};
var reportMeasureOption = new Option<string>("--measure")
{
    Description = "Report measure: accepted, dispatched, or both.",
    DefaultValueFactory = _ => "accepted"
};
var historicalMeasureOption = new Option<string>("--measure")
{
    Description = "Historical market-share measure: dispatched or accepted.",
    DefaultValueFactory = _ => "dispatched"
};
var reportProductOption = new Option<string>("--product")
{
    Description = "Report product: all, corriente, extra, or diesel.",
    DefaultValueFactory = _ => "all"
};
var buyerScopeOption = new Option<string>("--buyer-scope")
{
    Description = "Buyer subtype scope for eds_percentiles: eds, eds_fluvial, or eds_fluvial_industrial.",
    DefaultValueFactory = _ => "eds"
};
var historicalBuyerScopeOption = new Option<string>("--buyer-scope")
{
    Description = "Historical buyer scope: eds, eds_fluvial, or eds_fluvial_industrial.",
    DefaultValueFactory = _ => "eds_fluvial"
};
var resumeOption = new Option<bool>("--resume")
{
    Description = "Reuse compatible completed yearly parts from the historical manifest."
};
var outputDirOption = new Option<string?>("--output-dir")
{
    Description = "Directory where generated report CSV files will be written."
};
var topCitiesOption = new Option<int>("--top-cities")
{
    Description = "Number of top cities by accepted volume to include.",
    DefaultValueFactory = _ => 7
};
var topEdsOption = new Option<int>("--top-eds")
{
    Description = "Number of top automotive service stations by accepted volume to include.",
    DefaultValueFactory = _ => 20
};
var edsTopMeasureOption = new Option<string>("--measure")
{
    Description = "Top EDS ranking measure: dispatched or accepted.",
    DefaultValueFactory = _ => "dispatched"
};
var topOption = new Option<int>("--top")
{
    Description = "Number of automotive service stations to include.",
    DefaultValueFactory = _ => 20
};

var diagnose = new Command("diagnose", "Probe the endpoint and validate XMLA discovery.");
AddCommonOptions(diagnose, includeFormat: true);
diagnose.SetAction(async (parseResult, cancellationToken) =>
{
    var context = CreateContext(parseResult);
    var client = XmlaClient.Create(context.Connection);
    var metadata = new OlapMetadataService();

    EndpointProbeResult? probe = null;
    try
    {
        probe = await client.ProbeAsync(cancellationToken);
    }
    catch
    {
        probe = null;
    }

    DiagnoseResult result;
    try
    {
        var catalogs = await metadata.ListCatalogsAsync(
            client,
            debugOptions: context.DebugOptions,
            cancellationToken: cancellationToken);
        result = new DiagnoseResult(context.Connection.Source, context.Connection.AuthMode, probe, true, catalogs.Count, null);
    }
    catch (Exception ex)
    {
        result = new DiagnoseResult(context.Connection.Source, context.Connection.AuthMode, probe, false, 0, SanitizeError(ex));
    }

    await WriteFormattedAsync(result, DiagnoseTable(result), context.Format, context.OutputPath, cancellationToken);
    return result.DiscoverSucceeded ? 0 : 1;
});

var catalogs = new Command("catalogs", "List XMLA catalogs.");
AddCommonOptions(catalogs, includeFormat: true);
catalogs.SetAction(async (parseResult, cancellationToken) =>
{
    var context = CreateContext(parseResult);
    var client = XmlaClient.Create(context.Connection);
    var metadata = new OlapMetadataService();
    var result = await metadata.ListCatalogsAsync(
        client,
        debugOptions: context.DebugOptions,
        cancellationToken: cancellationToken);
    await WriteFormattedAsync(result, OutputFormatters.TableFromCatalogs(result), context.Format, context.OutputPath, cancellationToken);
    return 0;
});

var cubes = new Command("cubes", "List cubes for a catalog.");
cubes.Options.Add(catalogOption);
AddCommonOptions(cubes, includeFormat: true);
cubes.SetAction(async (parseResult, cancellationToken) =>
{
    var context = CreateContext(parseResult);
    var catalogName = Require(parseResult.GetValue(catalogOption), "--catalog");
    var client = XmlaClient.Create(context.Connection);
    var metadata = new OlapMetadataService();
    var result = await metadata.ListCubesAsync(
        client,
        new CatalogInfo(catalogName, "", ""),
        context.DebugOptions,
        cancellationToken);
    await WriteFormattedAsync(result, OutputFormatters.TableFromCubes(result), context.Format, context.OutputPath, cancellationToken);
    return 0;
});

var profile = new Command("profile", "Inspect dimensions, levels, measures, KPIs, measure groups, and fields for one cube.");
profile.Options.Add(catalogOption);
profile.Options.Add(cubeOption);
AddCommonOptions(profile, includeFormat: true);
profile.SetAction(async (parseResult, cancellationToken) =>
{
    var context = CreateContext(parseResult);
    var catalogName = Require(parseResult.GetValue(catalogOption), "--catalog");
    var cubeName = Require(parseResult.GetValue(cubeOption), "--cube");
    var client = XmlaClient.Create(context.Connection);
    var metadata = new OlapMetadataService();
    var result = await metadata.GetCubeProfileAsync(
        client,
        new CatalogInfo(catalogName, "", ""),
        new CubeInfo(cubeName, "", "", ""),
        context.DebugOptions,
        cancellationToken);
    await WriteFormattedAsync(result, OutputFormatters.TableFromFields(result.Fields), context.Format, context.OutputPath, cancellationToken);
    return 0;
});

var mdx = new Command("mdx", "Execute an MDX statement.");
mdx.Options.Add(catalogOption);
mdx.Options.Add(mdxOption);
mdx.Options.Add(mdxFileOption);
AddCommonOptions(mdx, includeFormat: true);
mdx.SetAction(async (parseResult, cancellationToken) =>
{
    var context = CreateContext(parseResult);
    var catalogName = Require(parseResult.GetValue(catalogOption), "--catalog");
    var statement = ResolveMdxStatement(parseResult.GetValue(mdxOption), parseResult.GetValue(mdxFileOption));
    var client = XmlaClient.Create(context.Connection);
    var result = await client.ExecuteMdxAsync(
        catalogName,
        statement,
        debugOptions: context.DebugOptions,
        cancellationToken: cancellationToken);
    var table = MdxTableBuilder.BuildTable(result);
    await WriteFormattedAsync(result, table, context.Format, context.OutputPath, cancellationToken);
    return 0;
});

var report = new Command("report", "Generate opinionated reusable reports.");
var reportMayoristas = new Command("mayoristas", "Generate market-share CSVs for wholesale fuel providers.");
reportMayoristas.Options.Add(yearOption);
reportMayoristas.Options.Add(quarterOption);
reportMayoristas.Options.Add(semesterOption);
reportMayoristas.Options.Add(annualOption);
reportMayoristas.Options.Add(monthsOption);
reportMayoristas.Options.Add(reportMeasureOption);
reportMayoristas.Options.Add(reportProductOption);
reportMayoristas.Options.Add(outputDirOption);
AddConnectionOptions(reportMayoristas, includeDebug: true);
reportMayoristas.SetAction(async (parseResult, cancellationToken) =>
{
    var year = parseResult.GetValue(yearOption) ??
               throw new ArgumentException("--year is required for report mayoristas.");
    var options = MayoristasReport.CreateOptions(
        year,
        parseResult.GetValue(quarterOption),
        parseResult.GetValue(semesterOption),
        parseResult.GetValue(annualOption),
        parseResult.GetValue(monthsOption),
        parseResult.GetValue(reportMeasureOption) ?? "accepted",
        parseResult.GetValue(reportProductOption) ?? "all");
    var outputDirectory = parseResult.GetValue(outputDirOption) ?? MayoristasReport.DefaultOutputDirectory(options);
    var context = CreateContext(parseResult, format: "table", outputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = MayoristasReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await MayoristasReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintMayoristasReportResult(result);
    return 0;
});
var reportMayoristasHistorico = new Command(
    "mayoristas_historico",
    "Generate auditable monthly historical market-share CSVs for wholesale fuel providers.");
reportMayoristasHistorico.Options.Add(startYearOption);
reportMayoristasHistorico.Options.Add(startMonthOption);
reportMayoristasHistorico.Options.Add(endYearOption);
reportMayoristasHistorico.Options.Add(endMonthOption);
reportMayoristasHistorico.Options.Add(historicalMeasureOption);
reportMayoristasHistorico.Options.Add(reportProductOption);
reportMayoristasHistorico.Options.Add(historicalBuyerScopeOption);
reportMayoristasHistorico.Options.Add(resumeOption);
reportMayoristasHistorico.Options.Add(outputDirOption);
AddConnectionOptions(reportMayoristasHistorico, includeDebug: true);
reportMayoristasHistorico.SetAction(async (parseResult, cancellationToken) =>
{
    var startYear = parseResult.GetValue(startYearOption) ??
                    throw new ArgumentException("--start-year is required for report mayoristas_historico.");
    var endYear = parseResult.GetValue(endYearOption) ??
                  throw new ArgumentException("--end-year is required for report mayoristas_historico.");
    var endMonth = parseResult.GetValue(endMonthOption) ??
                   throw new ArgumentException("--end-month is required for report mayoristas_historico.");
    var options = MayoristasHistoricoReport.CreateOptions(
        startYear,
        parseResult.GetValue(startMonthOption),
        endYear,
        endMonth,
        parseResult.GetValue(historicalMeasureOption) ?? "dispatched",
        parseResult.GetValue(reportProductOption) ?? "all",
        parseResult.GetValue(historicalBuyerScopeOption) ?? "eds_fluvial",
        parseResult.GetValue(resumeOption));
    var outputDirectory = parseResult.GetValue(outputDirOption) ?? MayoristasHistoricoReport.DefaultOutputDirectory(options);
    var context = CreateContext(parseResult, format: "table", outputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = MayoristasHistoricoReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await MayoristasHistoricoReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintMayoristasHistoricoReportResult(result);
    return 0;
});
var reportEdsMunicipios = new Command("eds_municipios", "Generate accepted-volume CSVs for automotive service stations by municipality.");
reportEdsMunicipios.Options.Add(yearOption);
reportEdsMunicipios.Options.Add(quarterOption);
reportEdsMunicipios.Options.Add(semesterOption);
reportEdsMunicipios.Options.Add(annualOption);
reportEdsMunicipios.Options.Add(monthsOption);
reportEdsMunicipios.Options.Add(reportProductOption);
reportEdsMunicipios.Options.Add(outputDirOption);
AddConnectionOptions(reportEdsMunicipios, includeDebug: true);
reportEdsMunicipios.SetAction(async (parseResult, cancellationToken) =>
{
    var year = parseResult.GetValue(yearOption) ??
               throw new ArgumentException("--year is required for report eds_municipios.");
    var options = EdsMunicipiosReport.CreateOptions(
        year,
        parseResult.GetValue(quarterOption),
        parseResult.GetValue(semesterOption),
        parseResult.GetValue(annualOption),
        parseResult.GetValue(monthsOption),
        parseResult.GetValue(reportProductOption) ?? "all");
    var outputDirectory = parseResult.GetValue(outputDirOption) ?? EdsMunicipiosReport.DefaultOutputDirectory(options);
    var context = CreateContext(parseResult, format: "table", outputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = EdsMunicipiosReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await EdsMunicipiosReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintEdsMunicipiosReportResult(result);
    return 0;
});
var reportEdsInsights = new Command("eds_insights", "Generate Power BI CSVs for automotive service station insights.");
reportEdsInsights.Options.Add(yearOption);
reportEdsInsights.Options.Add(quarterOption);
reportEdsInsights.Options.Add(semesterOption);
reportEdsInsights.Options.Add(annualOption);
reportEdsInsights.Options.Add(monthsOption);
reportEdsInsights.Options.Add(reportProductOption);
reportEdsInsights.Options.Add(topCitiesOption);
reportEdsInsights.Options.Add(topEdsOption);
reportEdsInsights.Options.Add(outputDirOption);
AddConnectionOptions(reportEdsInsights, includeDebug: true);
reportEdsInsights.SetAction(async (parseResult, cancellationToken) =>
{
    var year = parseResult.GetValue(yearOption) ??
               throw new ArgumentException("--year is required for report eds_insights.");
    var options = EdsInsightsReport.CreateOptions(
        year,
        parseResult.GetValue(quarterOption),
        parseResult.GetValue(semesterOption),
        parseResult.GetValue(annualOption),
        parseResult.GetValue(monthsOption),
        parseResult.GetValue(reportProductOption) ?? "all",
        parseResult.GetValue(topCitiesOption),
        parseResult.GetValue(topEdsOption));
    var outputDirectory = parseResult.GetValue(outputDirOption) ?? EdsInsightsReport.DefaultOutputDirectory(options);
    var context = CreateContext(parseResult, format: "table", outputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = EdsInsightsReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await EdsInsightsReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintEdsInsightsReportResult(result);
    return 0;
});
var reportEdsFrontera = new Command("eds_frontera", "Generate focused border-zone CSVs for the SU report.");
reportEdsFrontera.Options.Add(yearOption);
reportEdsFrontera.Options.Add(quarterOption);
reportEdsFrontera.Options.Add(semesterOption);
reportEdsFrontera.Options.Add(annualOption);
reportEdsFrontera.Options.Add(monthsOption);
reportEdsFrontera.Options.Add(outputDirOption);
AddConnectionOptions(reportEdsFrontera, includeDebug: true);
reportEdsFrontera.SetAction(async (parseResult, cancellationToken) =>
{
    var year = parseResult.GetValue(yearOption) ??
               throw new ArgumentException("--year is required for report eds_frontera.");
    var options = EdsFronteraReport.CreateOptions(
        year,
        parseResult.GetValue(quarterOption),
        parseResult.GetValue(semesterOption),
        parseResult.GetValue(annualOption),
        parseResult.GetValue(monthsOption));
    var outputDirectory = parseResult.GetValue(outputDirOption) ??
                          EdsFronteraReport.DefaultOutputDirectory(options);
    var context = CreateContext(parseResult, format: "table", outputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = EdsFronteraReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await EdsFronteraReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintEdsFronteraReportResult(result);
    return 0;
});
var reportEdsTop = new Command("eds_top", "Generate a measure-consistent Top EDS CSV for report charts.");
reportEdsTop.Options.Add(yearOption);
reportEdsTop.Options.Add(quarterOption);
reportEdsTop.Options.Add(semesterOption);
reportEdsTop.Options.Add(annualOption);
reportEdsTop.Options.Add(monthsOption);
reportEdsTop.Options.Add(edsTopMeasureOption);
reportEdsTop.Options.Add(reportProductOption);
reportEdsTop.Options.Add(topOption);
reportEdsTop.Options.Add(outputDirOption);
AddConnectionOptions(reportEdsTop, includeDebug: true);
reportEdsTop.SetAction(async (parseResult, cancellationToken) =>
{
    var year = parseResult.GetValue(yearOption) ??
               throw new ArgumentException("--year is required for report eds_top.");
    var options = EdsTopReport.CreateOptions(
        year,
        parseResult.GetValue(quarterOption),
        parseResult.GetValue(semesterOption),
        parseResult.GetValue(annualOption),
        parseResult.GetValue(monthsOption),
        parseResult.GetValue(edsTopMeasureOption) ?? "dispatched",
        parseResult.GetValue(reportProductOption) ?? "all",
        parseResult.GetValue(topOption));
    var outputDirectory = parseResult.GetValue(outputDirOption) ?? EdsTopReport.DefaultOutputDirectory(options);
    var context = CreateContext(parseResult, format: "table", outputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = EdsTopReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await EdsTopReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintEdsTopReportResult(result);
    return 0;
});
var reportEdsPercentiles = new Command("eds_percentiles", "Generate rolling 12-month EDS gal/month percentile CSVs.");
reportEdsPercentiles.Options.Add(endYearOption);
reportEdsPercentiles.Options.Add(endMonthOption);
reportEdsPercentiles.Options.Add(reportProductOption);
reportEdsPercentiles.Options.Add(buyerScopeOption);
reportEdsPercentiles.Options.Add(outputDirOption);
AddConnectionOptions(reportEdsPercentiles, includeDebug: true);
reportEdsPercentiles.SetAction(async (parseResult, cancellationToken) =>
{
    var endYear = parseResult.GetValue(endYearOption) ??
                  throw new ArgumentException("--end-year is required for report eds_percentiles.");
    var endMonth = parseResult.GetValue(endMonthOption) ??
                   throw new ArgumentException("--end-month is required for report eds_percentiles.");
    var options = EdsPercentilesReport.CreateOptions(
        endYear,
        endMonth,
        parseResult.GetValue(reportProductOption) ?? "all",
        parseResult.GetValue(buyerScopeOption) ?? "eds");
    var outputDirectory = parseResult.GetValue(outputDirOption) ?? EdsPercentilesReport.DefaultOutputDirectory(options);
    var context = CreateContext(parseResult, format: "table", outputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = EdsPercentilesReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await EdsPercentilesReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintEdsPercentilesReportResult(result);
    return 0;
});
report.Subcommands.Add(reportMayoristas);
report.Subcommands.Add(reportMayoristasHistorico);
report.Subcommands.Add(reportEdsMunicipios);
report.Subcommands.Add(reportEdsInsights);
report.Subcommands.Add(reportEdsFrontera);
report.Subcommands.Add(reportEdsTop);
report.Subcommands.Add(reportEdsPercentiles);

root.Subcommands.Add(diagnose);
root.Subcommands.Add(catalogs);
root.Subcommands.Add(cubes);
root.Subcommands.Add(profile);
root.Subcommands.Add(mdx);
root.Subcommands.Add(report);
root.SetAction(async (_, cancellationToken) => await RunInteractiveAsync(cancellationToken));

return root.Parse(args).Invoke();

void AddCommonOptions(Command command, bool includeFormat)
{
    AddConnectionOptions(command, includeDebug: false);
    if (includeFormat)
    {
        command.Options.Add(formatOption);
    }

    command.Options.Add(outputOption);
    command.Options.Add(xmlaRequestOutputOption);
    command.Options.Add(xmlaResponseOutputOption);
}

void AddConnectionOptions(Command command, bool includeDebug)
{
    command.Options.Add(sourceOption);
    command.Options.Add(urlOption);
    command.Options.Add(userOption);
    command.Options.Add(passwordOption);
    command.Options.Add(authOption);
    command.Options.Add(timeoutOption);
    if (includeDebug)
    {
        command.Options.Add(xmlaRequestOutputOption);
        command.Options.Add(xmlaResponseOutputOption);
    }
}

CliContext CreateContext(ParseResult parseResult, string? format = null, string? outputPath = null)
{
    return CreateContextFromValues(
        parseResult.GetValue(sourceOption) ?? "liqs",
        parseResult.GetValue(urlOption),
        parseResult.GetValue(userOption),
        parseResult.GetValue(passwordOption),
        parseResult.GetValue(authOption) ?? "basic",
        parseResult.GetValue(timeoutOption),
        format ?? NormalizeFormat(parseResult.GetValue(formatOption) ?? "table"),
        outputPath ?? parseResult.GetValue(outputOption),
        parseResult.GetValue(xmlaRequestOutputOption),
        parseResult.GetValue(xmlaResponseOutputOption));
}

CliContext CreateContextFromValues(
    string source,
    string? url,
    string? user,
    string? password,
    string authMode,
    int timeoutSeconds,
    string format,
    string? outputPath,
    string? xmlaRequestOutputPath,
    string? xmlaResponseOutputPath)
{
    var secrets = SecretStore.Load(Directory.GetCurrentDirectory());
    var resolver = new SourceResolver(secrets.Values);
    var connection = resolver.ResolveConnection(
        source,
        url,
        user,
        password,
        authMode,
        timeoutSeconds);
    return new CliContext(
        connection,
        format,
        outputPath,
        new XmlaDebugOptions(xmlaRequestOutputPath, xmlaResponseOutputPath));
}

async Task WriteFormattedAsync<T>(
    T jsonValue,
    MdxTable table,
    string format,
    string? outputPath,
    CancellationToken cancellationToken)
{
    var text = format switch
    {
        "json" => OutputFormatters.ToJson(jsonValue),
        "csv" => OutputFormatters.ToCsv(table),
        "table" => OutputFormatters.ToText(table),
        _ => throw new ArgumentException("Output format must be 'table', 'json', or 'csv'.")
    };

    if (string.IsNullOrWhiteSpace(outputPath))
    {
        Console.WriteLine(text);
        return;
    }

    var fullPath = Path.GetFullPath(outputPath);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    await File.WriteAllTextAsync(fullPath, text, cancellationToken);
    Console.WriteLine($"Output written: {fullPath}");
}

MdxTable DiagnoseTable(DiagnoseResult result)
{
    return new MdxTable(
        ["Field", "Value"],
        [
            ["Source", result.Source.Name],
            ["Url", result.Source.Url],
            ["AuthMode", result.AuthMode],
            ["HeadStatus", result.Probe is null ? "" : $"{result.Probe.StatusCode} {result.Probe.ReasonPhrase}"],
            ["WwwAuthenticate", result.Probe?.WwwAuthenticate ?? ""],
            ["Server", result.Probe?.Server ?? ""],
            ["DiscoverSucceeded", result.DiscoverSucceeded.ToString()],
            ["CatalogCount", result.CatalogCount.ToString()],
            ["Error", result.Error ?? ""]
        ],
        null);
}

string ResolveMdxStatement(string? inlineMdx, string? mdxFile)
{
    var hasInline = !string.IsNullOrWhiteSpace(inlineMdx);
    var hasFile = !string.IsNullOrWhiteSpace(mdxFile);
    if (hasInline == hasFile)
    {
        throw new ArgumentException("MDX mode requires exactly one of --mdx <statement> or --mdx-file <path>.");
    }

    var statement = hasInline ? inlineMdx! : File.ReadAllText(mdxFile!);
    if (string.IsNullOrWhiteSpace(statement))
    {
        throw new ArgumentException("MDX query is empty.");
    }

    return statement;
}

string Require(string? value, string optionName)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new ArgumentException($"{optionName} is required.");
    }

    return value;
}

string NormalizeFormat(string format)
{
    var normalized = format.Trim().ToLowerInvariant();
    return normalized is "table" or "json" or "csv"
        ? normalized
        : throw new ArgumentException("Output format must be 'table', 'json', or 'csv'.");
}

string SanitizeError(Exception exception)
{
    return exception.Message.ReplaceLineEndings(" ");
}

async Task<int> RunInteractiveAsync(CancellationToken cancellationToken)
{
    Console.WriteLine("sicom-olap-cli");
    Console.WriteLine();
    Console.WriteLine("1. Reportes");
    Console.WriteLine("2. MDX manual");
    Console.WriteLine("3. Diagnostico");
    Console.WriteLine();
    var choice = Prompt("Seleccione una opcion", "1");
    Console.WriteLine();

    return choice switch
    {
        "1" => await RunInteractiveReportsAsync(cancellationToken),
        "2" => await RunInteractiveMdxAsync(cancellationToken),
        "3" => await RunInteractiveDiagnoseAsync(cancellationToken),
        _ => InvalidChoice()
    };
}

async Task<int> RunInteractiveReportsAsync(CancellationToken cancellationToken)
{
    Console.WriteLine("Reportes disponibles");
    Console.WriteLine("1. Mayoristas");
    Console.WriteLine("2. EDS municipios");
    Console.WriteLine("3. EDS insights");
    Console.WriteLine("4. EDS percentiles");
    Console.WriteLine("5. Mayoristas historico");
    Console.WriteLine();
    var choice = Prompt("Seleccione un reporte", "1");
    Console.WriteLine();
    return choice switch
    {
        "1" => await RunInteractiveMayoristasReportAsync(cancellationToken),
        "2" => await RunInteractiveEdsMunicipiosReportAsync(cancellationToken),
        "3" => await RunInteractiveEdsInsightsReportAsync(cancellationToken),
        "4" => await RunInteractiveEdsPercentilesReportAsync(cancellationToken),
        "5" => await RunInteractiveMayoristasHistoricoReportAsync(cancellationToken),
        _ => InvalidChoice()
    };
}

async Task<int> RunInteractiveMayoristasReportAsync(CancellationToken cancellationToken)
{
    var (defaultYear, defaultQuarter) = PreviousCompletedQuarter(DateTime.Today);
    var defaultSemester = defaultQuarter <= 2 ? 1 : 2;
    var year = PromptInt("Ano", defaultYear);
    var periodMode = Prompt("Periodo: 1=trimestre, 2=semestre, 3=meses manuales", "1");
    int? quarter = null;
    int? semester = null;
    string? months = null;
    if (periodMode == "1")
    {
        quarter = PromptInt("Trimestre", defaultQuarter);
    }
    else if (periodMode == "2")
    {
        semester = PromptInt("Semestre", defaultSemester);
    }
    else if (periodMode == "3")
    {
        months = Prompt("Meses separados por coma", string.Join(",", MayoristasReport.MonthsForQuarter(defaultQuarter)));
    }
    else
    {
        return InvalidChoice();
    }

    var measure = Prompt("Medida: accepted, dispatched, both", "accepted");
    var product = Prompt("Producto: all, corriente, extra, diesel", "all");
    var options = MayoristasReport.CreateOptions(year, quarter, semester, months, measure, product);
    var defaultOutputDirectory = InteractiveReportOutputDirectory("mayoristas", options.PeriodLabel);
    var outputDirectory = Prompt("Carpeta de salida", defaultOutputDirectory);
    var timeout = PromptInt("Timeout XMLA en segundos", 600);
    var context = CreateContextFromValues(
        source: "liqs",
        url: null,
        user: null,
        password: null,
        authMode: "basic",
        timeoutSeconds: timeout,
        format: "table",
        outputPath: null,
        xmlaRequestOutputPath: null,
        xmlaResponseOutputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = MayoristasReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await MayoristasReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintMayoristasReportResult(result);
    return 0;
}

async Task<int> RunInteractiveMayoristasHistoricoReportAsync(CancellationToken cancellationToken)
{
    var (defaultEndYear, defaultEndMonth) = PreviousCompletedMonth(DateTime.Today);
    var startYear = PromptInt("Ano inicial", 2011);
    var startMonth = PromptInt("Mes inicial", 1);
    var endYear = PromptInt("Ano final", defaultEndYear);
    var endMonth = PromptInt("Mes final", defaultEndMonth);
    var measure = Prompt("Medida: dispatched, accepted", "dispatched");
    var product = Prompt("Producto: all, corriente, extra, diesel", "all");
    var options = MayoristasHistoricoReport.CreateOptions(
        startYear,
        startMonth,
        endYear,
        endMonth,
        measure,
        product,
        "eds_fluvial",
        resume: true);
    var defaultOutputDirectory = InteractiveReportOutputDirectory("mayoristas_historico", options.PeriodLabel);
    var outputDirectory = Prompt("Carpeta de salida", defaultOutputDirectory);
    var timeout = PromptInt("Timeout XMLA en segundos", 600);
    var context = CreateContextFromValues(
        source: "liqs",
        url: null,
        user: null,
        password: null,
        authMode: "basic",
        timeoutSeconds: timeout,
        format: "table",
        outputPath: null,
        xmlaRequestOutputPath: null,
        xmlaResponseOutputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = MayoristasHistoricoReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await MayoristasHistoricoReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintMayoristasHistoricoReportResult(result);
    return 0;
}

async Task<int> RunInteractiveEdsMunicipiosReportAsync(CancellationToken cancellationToken)
{
    var (defaultYear, defaultQuarter) = PreviousCompletedQuarter(DateTime.Today);
    var defaultSemester = defaultQuarter <= 2 ? 1 : 2;
    var year = PromptInt("Ano", defaultYear);
    var periodMode = Prompt("Periodo: 1=trimestre, 2=semestre, 3=meses manuales", "1");
    int? quarter = null;
    int? semester = null;
    string? months = null;
    if (periodMode == "1")
    {
        quarter = PromptInt("Trimestre", defaultQuarter);
    }
    else if (periodMode == "2")
    {
        semester = PromptInt("Semestre", defaultSemester);
    }
    else if (periodMode == "3")
    {
        months = Prompt("Meses separados por coma", string.Join(",", MayoristasReport.MonthsForQuarter(defaultQuarter)));
    }
    else
    {
        return InvalidChoice();
    }

    var product = Prompt("Producto: all, corriente, extra, diesel", "all");
    var options = EdsMunicipiosReport.CreateOptions(year, quarter, semester, months, product);
    var defaultOutputDirectory = InteractiveReportOutputDirectory("eds_municipios", options.PeriodLabel);
    var outputDirectory = Prompt("Carpeta de salida", defaultOutputDirectory);
    var timeout = PromptInt("Timeout XMLA en segundos", 600);
    var context = CreateContextFromValues(
        source: "liqs",
        url: null,
        user: null,
        password: null,
        authMode: "basic",
        timeoutSeconds: timeout,
        format: "table",
        outputPath: null,
        xmlaRequestOutputPath: null,
        xmlaResponseOutputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = EdsMunicipiosReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await EdsMunicipiosReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintEdsMunicipiosReportResult(result);
    return 0;
}

async Task<int> RunInteractiveEdsInsightsReportAsync(CancellationToken cancellationToken)
{
    var (defaultYear, defaultQuarter) = PreviousCompletedQuarter(DateTime.Today);
    var defaultSemester = defaultQuarter <= 2 ? 1 : 2;
    var year = PromptInt("Ano", defaultYear);
    var periodMode = Prompt("Periodo: 1=trimestre, 2=semestre, 3=meses manuales", "1");
    int? quarter = null;
    int? semester = null;
    string? months = null;
    if (periodMode == "1")
    {
        quarter = PromptInt("Trimestre", defaultQuarter);
    }
    else if (periodMode == "2")
    {
        semester = PromptInt("Semestre", defaultSemester);
    }
    else if (periodMode == "3")
    {
        months = Prompt("Meses separados por coma", string.Join(",", MayoristasReport.MonthsForQuarter(defaultQuarter)));
    }
    else
    {
        return InvalidChoice();
    }

    var product = Prompt("Producto: all, corriente, extra, diesel", "all");
    var topCities = PromptInt("Top ciudades", 7);
    var topEds = PromptInt("Top EDS", 20);
    var options = EdsInsightsReport.CreateOptions(year, quarter, semester, months, product, topCities, topEds);
    var defaultOutputDirectory = InteractiveReportOutputDirectory("eds_insights", options.PeriodLabel);
    var outputDirectory = Prompt("Carpeta de salida", defaultOutputDirectory);
    var timeout = PromptInt("Timeout XMLA en segundos", 600);
    var context = CreateContextFromValues(
        source: "liqs",
        url: null,
        user: null,
        password: null,
        authMode: "basic",
        timeoutSeconds: timeout,
        format: "table",
        outputPath: null,
        xmlaRequestOutputPath: null,
        xmlaResponseOutputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = EdsInsightsReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await EdsInsightsReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintEdsInsightsReportResult(result);
    return 0;
}

async Task<int> RunInteractiveEdsPercentilesReportAsync(CancellationToken cancellationToken)
{
    var (defaultYear, defaultMonth) = PreviousCompletedMonth(DateTime.Today);
    var endYear = PromptInt("Ano final", defaultYear);
    var endMonth = PromptInt("Mes final", defaultMonth);
    var product = Prompt("Producto: all, corriente, extra, diesel", "all");
    Console.WriteLine("Alcance compradores");
    Console.WriteLine("1. Solo EDS automotriz");
    Console.WriteLine("2. EDS automotriz + fluvial");
    Console.WriteLine("3. EDS automotriz + fluvial + comercializador industrial");
    var buyerScopeChoice = Prompt("Seleccione alcance", "1");
    string buyerScope;
    if (buyerScopeChoice == "1")
    {
        buyerScope = "eds";
    }
    else if (buyerScopeChoice == "2")
    {
        buyerScope = "eds_fluvial";
    }
    else if (buyerScopeChoice == "3")
    {
        buyerScope = "eds_fluvial_industrial";
    }
    else
    {
        return InvalidChoice();
    }

    var options = EdsPercentilesReport.CreateOptions(endYear, endMonth, product, buyerScope);
    var defaultOutputDirectory = InteractiveReportOutputDirectory("eds_percentiles", options.PeriodLabel);
    var outputDirectory = Prompt("Carpeta de salida", defaultOutputDirectory);
    var timeout = PromptInt("Timeout XMLA en segundos", 600);
    var context = CreateContextFromValues(
        source: "liqs",
        url: null,
        user: null,
        password: null,
        authMode: "basic",
        timeoutSeconds: timeout,
        format: "table",
        outputPath: null,
        xmlaRequestOutputPath: null,
        xmlaResponseOutputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var equivalentCommand = EdsPercentilesReport.BuildEquivalentCommand(options, outputDirectory);
    var result = await EdsPercentilesReport.GenerateAsync(
        client,
        options,
        outputDirectory,
        equivalentCommand,
        context.DebugOptions,
        cancellationToken);
    PrintEdsPercentilesReportResult(result);
    return 0;
}

async Task<int> RunInteractiveMdxAsync(CancellationToken cancellationToken)
{
    var catalogName = Prompt("Catalogo", MayoristasReport.CatalogName);
    var mdxFile = Prompt("Ruta del archivo MDX", "");
    if (string.IsNullOrWhiteSpace(mdxFile))
    {
        Console.Error.WriteLine("Se requiere un archivo MDX.");
        return 1;
    }

    var format = Prompt("Formato: table, json, csv", "csv");
    var outputPath = Prompt("Archivo de salida opcional", "");
    var timeout = PromptInt("Timeout XMLA en segundos", 600);
    var context = CreateContextFromValues(
        source: "liqs",
        url: null,
        user: null,
        password: null,
        authMode: "basic",
        timeoutSeconds: timeout,
        format: NormalizeFormat(format),
        outputPath: string.IsNullOrWhiteSpace(outputPath) ? null : outputPath,
        xmlaRequestOutputPath: null,
        xmlaResponseOutputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var result = await client.ExecuteMdxAsync(
        catalogName,
        ResolveMdxStatement(null, mdxFile),
        debugOptions: context.DebugOptions,
        cancellationToken: cancellationToken);
    var table = MdxTableBuilder.BuildTable(result);
    await WriteFormattedAsync(result, table, context.Format, context.OutputPath, cancellationToken);
    return 0;
}

async Task<int> RunInteractiveDiagnoseAsync(CancellationToken cancellationToken)
{
    var context = CreateContextFromValues(
        source: "liqs",
        url: null,
        user: null,
        password: null,
        authMode: "basic",
        timeoutSeconds: 120,
        format: "table",
        outputPath: null,
        xmlaRequestOutputPath: null,
        xmlaResponseOutputPath: null);
    var client = XmlaClient.Create(context.Connection);
    var metadata = new OlapMetadataService();
    EndpointProbeResult? probe = null;
    try
    {
        probe = await client.ProbeAsync(cancellationToken);
    }
    catch
    {
        probe = null;
    }

    DiagnoseResult diagnoseResult;
    try
    {
        var catalogList = await metadata.ListCatalogsAsync(
            client,
            debugOptions: context.DebugOptions,
            cancellationToken: cancellationToken);
        diagnoseResult = new DiagnoseResult(context.Connection.Source, context.Connection.AuthMode, probe, true, catalogList.Count, null);
    }
    catch (Exception ex)
    {
        diagnoseResult = new DiagnoseResult(context.Connection.Source, context.Connection.AuthMode, probe, false, 0, SanitizeError(ex));
    }

    await WriteFormattedAsync(diagnoseResult, DiagnoseTable(diagnoseResult), context.Format, context.OutputPath, cancellationToken);
    return diagnoseResult.DiscoverSucceeded ? 0 : 1;
}

void PrintMayoristasReportResult(MayoristasReportResult result)
{
    Console.WriteLine("Reporte generado.");
    Console.WriteLine($"Detalle: {Path.GetFullPath(result.DetailCsvPath)}");
    Console.WriteLine($"Resumen mayoristas: {Path.GetFullPath(result.ProviderSummaryCsvPath)}");
    if (!string.IsNullOrWhiteSpace(result.ProductSummaryCsvPath))
    {
        Console.WriteLine($"Resumen productos: {Path.GetFullPath(result.ProductSummaryCsvPath)}");
    }

    Console.WriteLine();
    Console.WriteLine("Comando equivalente:");
    Console.WriteLine(result.EquivalentCommand);
    Console.WriteLine();
    Console.WriteLine(OutputFormatters.ToText(result.ProviderSummaryTable));
}

void PrintMayoristasHistoricoReportResult(MayoristasHistoricoReportResult result)
{
    Console.WriteLine("Reporte historico generado.");
    Console.WriteLine($"Detalle: {Path.GetFullPath(result.DetailCsvPath)}");
    Console.WriteLine($"Mensual: {Path.GetFullPath(result.MonthlyCsvPath)}");
    Console.WriteLine($"Manifiesto: {Path.GetFullPath(result.ManifestJsonPath)}");
    Console.WriteLine($"Partes anuales: {result.PartCsvPaths.Count}");
    Console.WriteLine($"Filas detalle: {result.DetailTable.Rows.Count}");
    Console.WriteLine($"Filas mensuales: {result.MonthlyTable.Rows.Count}");
    Console.WriteLine();
    Console.WriteLine("Comando equivalente:");
    Console.WriteLine(result.EquivalentCommand);
    Console.WriteLine();
    Console.WriteLine("Primeras filas mensuales:");
    Console.WriteLine(OutputFormatters.ToText(PreviewTable(result.MonthlyTable, 20)));
}

void PrintEdsMunicipiosReportResult(EdsMunicipiosReportResult result)
{
    Console.WriteLine("Reporte generado.");
    Console.WriteLine($"Detalle: {Path.GetFullPath(result.DetailCsvPath)}");
    Console.WriteLine($"Promedio: {Path.GetFullPath(result.AverageCsvPath)}");
    Console.WriteLine($"EDS activas: {Path.GetFullPath(result.ActiveEdsCsvPath)}");
    Console.WriteLine($"Filas detalle: {result.DetailTable.Rows.Count}");
    Console.WriteLine($"Filas promedio: {result.AverageTable.Rows.Count}");
    Console.WriteLine($"Filas EDS activas: {result.ActiveEdsTable.Rows.Count}");
    Console.WriteLine();
    Console.WriteLine("Comando equivalente:");
    Console.WriteLine(result.EquivalentCommand);
    Console.WriteLine();
    Console.WriteLine("Primeras filas del promedio:");
    Console.WriteLine(OutputFormatters.ToText(PreviewTable(result.AverageTable, 20)));
}

void PrintEdsInsightsReportResult(EdsInsightsReportResult result)
{
    Console.WriteLine("Reporte generado.");
    Console.WriteLine($"Banderas nacional: {Path.GetFullPath(result.NationalFlagsCsvPath)}");
    Console.WriteLine($"Top ciudades: {Path.GetFullPath(result.TopCitiesCsvPath)}");
    Console.WriteLine($"Banderas top ciudades: {Path.GetFullPath(result.TopCityFlagsCsvPath)}");
    Console.WriteLine($"Top EDS: {Path.GetFullPath(result.TopEdsCsvPath)}");
    Console.WriteLine($"Frontera resumen: {Path.GetFullPath(result.BorderSummaryCsvPath)}");
    Console.WriteLine($"Frontera productos: {Path.GetFullPath(result.BorderProductsCsvPath)}");
    Console.WriteLine();
    Console.WriteLine("Comando equivalente:");
    Console.WriteLine(result.EquivalentCommand);
    Console.WriteLine();
    Console.WriteLine("Primeras filas de banderas nacional:");
    Console.WriteLine(OutputFormatters.ToText(PreviewTable(result.NationalFlagsTable, 20)));
}

void PrintEdsFronteraReportResult(EdsFronteraReportResult result)
{
    Console.WriteLine("Reporte generado.");
    Console.WriteLine($"Resumen ZFD: {Path.GetFullPath(result.SummaryCsvPath)}");
    Console.WriteLine($"Productos ZFD: {Path.GetFullPath(result.ProductsCsvPath)}");
    Console.WriteLine($"Municipios ZFD: {Path.GetFullPath(result.MunicipalitiesCsvPath)}");
    Console.WriteLine($"Manifiesto: {Path.GetFullPath(result.ManifestPath)}");
    Console.WriteLine();
    Console.WriteLine("Comando equivalente:");
    Console.WriteLine(result.EquivalentCommand);
    Console.WriteLine();
    Console.WriteLine(OutputFormatters.ToText(result.SummaryTable));
}

void PrintEdsTopReportResult(EdsTopReportResult result)
{
    Console.WriteLine("Reporte generado.");
    Console.WriteLine($"Top EDS: {Path.GetFullPath(result.CsvPath)}");
    Console.WriteLine($"Manifiesto: {Path.GetFullPath(result.ManifestPath)}");
    Console.WriteLine($"Filas: {result.Table.Rows.Count}");
    Console.WriteLine();
    Console.WriteLine("Comando equivalente:");
    Console.WriteLine(result.EquivalentCommand);
    Console.WriteLine();
    Console.WriteLine(OutputFormatters.ToText(PreviewTable(result.Table, 20)));
}

void PrintEdsPercentilesReportResult(EdsPercentilesReportResult result)
{
    Console.WriteLine("Reporte generado.");
    Console.WriteLine($"Base 12m: {Path.GetFullPath(result.BaseCsvPath)}");
    Console.WriteLine($"Estaciones: {Path.GetFullPath(result.StationsCsvPath)}");
    Console.WriteLine($"Resumen: {Path.GetFullPath(result.SummaryCsvPath)}");
    Console.WriteLine($"Filas base: {result.BaseTable.Rows.Count}");
    Console.WriteLine($"Filas estaciones: {result.StationsTable.Rows.Count}");
    Console.WriteLine($"Filas resumen: {result.SummaryTable.Rows.Count}");
    Console.WriteLine();
    Console.WriteLine("Comando equivalente:");
    Console.WriteLine(result.EquivalentCommand);
    Console.WriteLine();
    Console.WriteLine("Resumen de deciles:");
    Console.WriteLine(OutputFormatters.ToText(PreviewTable(result.SummaryTable, 20)));
}

MdxTable PreviewTable(MdxTable table, int maxRows)
{
    return new MdxTable(
        table.Headers,
        table.Rows.Take(maxRows).ToList(),
        table.Rows.Count > maxRows ? $"Mostrando {maxRows} de {table.Rows.Count} filas." : table.Note);
}

string InteractiveReportOutputDirectory(string reportFolder, string periodLabel)
{
    return Path.Combine("results-interactive-cli", MayoristasReport.CatalogName, reportFolder, periodLabel);
}

string Prompt(string label, string defaultValue)
{
    Console.Write(string.IsNullOrWhiteSpace(defaultValue) ? $"{label}: " : $"{label} [{defaultValue}]: ");
    var value = Console.ReadLine();
    return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
}

int PromptInt(string label, int defaultValue)
{
    while (true)
    {
        var value = Prompt(label, defaultValue.ToString());
        if (int.TryParse(value, out var parsed))
        {
            return parsed;
        }

        Console.WriteLine("Ingrese un numero valido.");
    }
}

(int Year, int Quarter) PreviousCompletedQuarter(DateTime date)
{
    var currentQuarter = ((date.Month - 1) / 3) + 1;
    if (currentQuarter == 1)
    {
        return (date.Year - 1, 4);
    }

    return (date.Year, currentQuarter - 1);
}

(int Year, int Month) PreviousCompletedMonth(DateTime date)
{
    var previousMonth = date.AddMonths(-1);
    return (previousMonth.Year, previousMonth.Month);
}

int InvalidChoice()
{
    Console.Error.WriteLine("Opcion no valida.");
    return 1;
}

internal sealed record CliContext(
    SourceConnection Connection,
    string Format,
    string? OutputPath,
    XmlaDebugOptions DebugOptions);
